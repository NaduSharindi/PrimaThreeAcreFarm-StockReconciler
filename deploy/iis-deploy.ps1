<#
.SYNOPSIS
    Deploys the Stock Reconciler to IIS with backup, health check and automatic rollback.

.EXAMPLE
    .\iis-deploy.ps1 -PackagePath .\publish -SitePath D:\inetpub\PrimaStock -AppPoolName PrimaStock -HealthUrl https://primastock.local/health/ready

.EXAMPLE
    # Manual rollback to the most recent backup
    .\iis-deploy.ps1 -SitePath D:\inetpub\PrimaStock -AppPoolName PrimaStock -HealthUrl https://primastock.local/health/ready -Rollback

.NOTES
    Prerequisites on the server: IIS, ASP.NET Core Hosting Bundle (.NET 10), WebAdministration module.
    Server-specific settings (connection string, finance API key) live in the app-pool environment,
    so they are never overwritten by a deployment.
#>
[CmdletBinding()]
param(
    [string]$PackagePath,
    [Parameter(Mandatory)] [string]$SitePath,
    [Parameter(Mandatory)] [string]$AppPoolName,
    [Parameter(Mandatory)] [string]$HealthUrl,
    [string]$BackupRoot = "D:\Deployments\PrimaStock\backups",
    [int]$KeepBackups = 10,
    [switch]$Rollback
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

function Write-Step($msg) { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $msg" -ForegroundColor Cyan }

function Stop-Site {
    # app_offline.htm makes the ASP.NET Core Module shut the app down gracefully and show a maintenance page.
    Set-Content -Path (Join-Path $SitePath "app_offline.htm") -Value "<h1>Stock Reconciler is being updated. Please retry in a minute.</h1>"
    Start-Sleep -Seconds 3
    if ((Get-WebAppPoolState $AppPoolName).Value -ne "Stopped") { Stop-WebAppPool $AppPoolName }
    while ((Get-WebAppPoolState $AppPoolName).Value -ne "Stopped") { Start-Sleep -Milliseconds 500 }
}

function Start-Site {
    Remove-Item (Join-Path $SitePath "app_offline.htm") -ErrorAction SilentlyContinue
    Start-WebAppPool $AppPoolName
}

function Test-Health {
    for ($i = 1; $i -le 12; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $HealthUrl -UseBasicParsing -TimeoutSec 10
            if ($r.StatusCode -eq 200 -and $r.Content -match '"Healthy"') { return $true }
        } catch { }
        Start-Sleep -Seconds 5
    }
    return $false
}

function Restore-Backup($zip) {
    Write-Step "Rolling back to $zip"
    Stop-Site
    Get-ChildItem $SitePath -Exclude "logs", "app_offline.htm" | Remove-Item -Recurse -Force
    Expand-Archive -Path $zip -DestinationPath $SitePath -Force
    Start-Site
}

New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null

if ($Rollback) {
    $latest = Get-ChildItem $BackupRoot -Filter *.zip | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No backup found in $BackupRoot" }
    Restore-Backup $latest.FullName
    if (-not (Test-Health)) { throw "Rollback completed but health check is still failing - escalate to on-call." }
    Write-Step "Rollback succeeded."
    exit 0
}

if (-not (Test-Path $PackagePath)) { throw "Package not found: $PackagePath" }

# 1) Backup current release (everything except logs)
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path $BackupRoot "PrimaStock_$stamp.zip"
if (Test-Path (Join-Path $SitePath "*.dll")) {
    Write-Step "Backing up current release to $backup"
    Get-ChildItem $SitePath -Exclude "logs" | Compress-Archive -DestinationPath $backup
}

# 2) Deploy
Write-Step "Stopping $AppPoolName"
Stop-Site
Write-Step "Copying new release"
robocopy $PackagePath $SitePath /MIR /XD logs /XF app_offline.htm appsettings.Production.json /NFL /NDL /NJH /NJS /R:3 /W:5 | Out-Null
if ($LASTEXITCODE -ge 8) { Restore-Backup $backup; throw "robocopy failed with exit code $LASTEXITCODE" }
Write-Step "Starting $AppPoolName"
Start-Site

# 3) Verify, roll back automatically on failure
Write-Step "Health check $HealthUrl"
if (-not (Test-Health)) {
    if (Test-Path $backup) { Restore-Backup $backup }
    throw "Deployment failed health check and was rolled back."
}

# 4) Housekeeping
Get-ChildItem $BackupRoot -Filter *.zip | Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepBackups | Remove-Item -Force
Write-Step "Deployment succeeded."
