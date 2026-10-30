# Operations runbook — Stock Reconciler

## Service overview

| Item | Value |
|---|---|
| Hosting | IIS on Windows Server, ASP.NET Core Module V2, in-process, app pool `PrimaStock` (No Managed Code) |
| Database | SQL Server, database `PrimaFarmStockDb` |
| Dependencies | Finance ledger API (outbound HTTPS) |
| Health | `GET /health/live` (process up) · `GET /health/ready` (database reachable, returns JSON) |
| Logs | `<site>\logs\prima-stock-YYYYMMDD.log` (90 days), Windows Event Log for IIS/ANCM start-up errors |
| Audit | `dbo.AuditLogs` (UI: Audit trail) · Integration calls: `dbo.IntegrationLogs` (UI: Integration log) |

## First response to an incident

1. **Get the reference.** Every error page shows a correlation id; every response has an `X-Correlation-ID` header.
2. **Find the request:** `Select-String -Path D:\inetpub\PrimaStock\logs\*.log -Pattern "<correlation id>"`
3. **Find related rows:**
   ```sql
   SELECT * FROM dbo.AuditLogs       WHERE CorrelationId = '<id>';
   SELECT * FROM dbo.IntegrationLogs WHERE CorrelationId = '<id>';
   ```
4. Check `/health/ready`. If the database is `Unhealthy`, go to *Database unreachable* below.

## Common problems

| Symptom | Likely cause | Fix |
|---|---|---|
| **HTTP 500.30 / 500.31** "ANCM failed to start" | Hosting Bundle missing / wrong .NET version, or start-up exception (bad config) | Install the .NET 10 Hosting Bundle, then `iisreset`. Temporarily set `stdoutLogEnabled="true"` in `web.config`, reproduce, read `logs\stdout_*.log`, set it back to `false` |
| **HTTP 502.5 / 503** | App pool stopped (rapid-fail protection after repeated crashes) | Event Viewer → Application log (source *IIS AspNetCore Module V2*); fix the cause; `Start-WebAppPool PrimaStock` |
| Users signed out after every recycle | Data Protection keys not persisted | Set `DataProtection:KeyPath` to a folder the app-pool identity can write |
| **413 Request Entity Too Large** | File above IIS / Kestrel limit | Within policy? Raise `maxAllowedContentLength` in `web.config` **and** `Security:MaxUploadBytes` |
| "No stock lines could be read from the farm file" | Farm changed its template (new header text) | Open the file; header row must be in the first 20 rows, with an item-code header (*Item no / Item Code / Item number*) and a quantity header (*On-hand, C/B, Balance…, or a date*). Add new header synonyms in `StockFileReader` and release through change management |
| "AS400 control total mismatch" | IBM i extract incomplete (job ended early / FTP truncated) | Do **not** edit the file. Ask the IBM i operator to re-run the extract job and re-send |
| Approval shows **PostingFailed** | Finance API down, circuit breaker open, or key rejected | Integration log → HTTP status / error. 401: rotate `Integration__Finance__ApiKey`. 5xx / timeout: wait for the finance team, then press **Retry finance posting**. Retries are safe because of the Idempotency-Key (no double posting) |
| Account locked | 5 failed attempts | Lock clears after 15 minutes, or an admin runs: `UPDATE dbo.AppUsers SET FailedLoginCount=0, LockoutEndUtc=NULL WHERE Username='x'` (log the change request) |
| Slow history page | Missing statistics / index fragmentation | `EXEC sp_updatestats;` check `sys.dm_db_index_physical_stats` for `IX_Runs_*`, rebuild if > 30% |

## Database unreachable

1. `Test-NetConnection <sqlserver> -Port 1433`
2. Check the SQL Server service, then `SELECT state_desc FROM sys.databases WHERE name = 'PrimaFarmStockDb'`
3. Is the app-pool identity still a database user? (`IIS APPPOOL\PrimaStock` or the gMSA)
4. EF Core retries transient errors 3 times; persistent failure means infrastructure, so escalate to the DBA.

## Business continuity

- **Backups:** `database/900_backup_and_maintenance.sql` (full daily, differential 6-hourly, log 15-minute, `CHECKSUM` + `RESTORE VERIFYONLY`).
- **Restore drill:** quarterly, on the DR server, using the commented restore block in the same script; record the RTO achieved.
- **Application rollback:** `.\deploy\iis-deploy.ps1 -SitePath ... -AppPoolName PrimaStock -HealthUrl ... -Rollback` restores the previous release (last 10 kept).
- **Manual fallback:** if the application is unavailable during month-end, farms keep submitting their Excel files; reconcile them once service is restored (runs are dated when created).
