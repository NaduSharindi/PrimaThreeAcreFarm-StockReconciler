# Prima Three Acre Farms — Stock Reconciler (.NET edition)

Internal web application that compares **ERP stock exports** with **farm stock submissions** for the
Three Acre Farms breeder locations (Kosgama, Makuluwatta, Wewalpanawa, Meegoda, Eswatta, Halwathura),
stores every reconciliation in SQL Server, routes it through a **maker-checker approval**, posts the
approved variance to the **finance system**, and produces the same colour-coded **Excel report** as the
original Python tool (`D:\Meegoda_StockReconciler`).

Developed by **Nadunika Sharindi** · 2026

---

## Quick start

Prerequisites: .NET 10 SDK, SQL Server 2016 SP1+ or LocalDB (installed with Visual Studio).

```powershell
cd D:\PrimaThreeAcreFarm
dotnet test                                   # 35 unit tests
dotnet run --project src\PrimaFarm.StockReconciler.Web --launch-profile http
```

Open **http://localhost:5012**. In Development the app creates `PrimaFarmStockDb` on
`(localdb)\MSSQLLocalDB` and applies `database\001..003*.sql` automatically. Or just double-click `run.bat`.

| Username  | Password       | Role    | Can do |
|-----------|----------------|---------|--------|
| `officer` | `Officer@2026` | Officer | Run reconciliations, download reports |
| `admin`   | `Admin@2026`   | Admin   | Everything + approve/reject (not own runs) + audit |
| `auditor` | `Auditor@2026` | Auditor | Read-only: history, reports, audit & integration logs |

Try it with the synthetic files in `samples\` (Kosgama): ERP `erp_kosgama_616.csv` (or the AS400
extract `erp_kosgama_616_as400.txt`) + farm `farm_kosgama_616.xlsx`. Expected: 5 matched, 2 mismatched,
1 missing in farm, 1 not in ERP, 1 item summed across sheets. Then sign in as `admin` and approve it —
the simulated finance API fails ~30% of calls on purpose, so you can watch the retry policy in the
Integration log.

---

## Architecture

```
 Browser (Razor MVC, cookie auth + CSRF)      ERP gateway / BI (REST, X-Api-Key)      Legacy VB6 / IBM i (SOAP)
                 │                                         │                                    │
                 ▼                                         ▼                                    ▼
 ┌──────────────────────────────── ASP.NET Core 10 (IIS in-process) ──────────────────────────────────┐
 │ Middleware: CorrelationId → Serilog request log → ExceptionHandler → HSTS → Security headers →     │
 │             Static files → Routing → Authentication → Rate limiter → SOAP → Authorization          │
 │                                                                                                     │
 │ Controllers (MVC)  Api/v1 (JSON+XML)  SoapCore endpoint  /health/live  /health/ready                │
 │        │                  │                  │                                                      │
 │        └──────► ReconciliationService ◄──────┘     AuthService · AuditService · ApiKeyValidator     │
 │                 │        │          │                                                               │
 │   StockFileReader  ReconciliationEngine  ExcelReportWriter        FinancePostingClient ─────────────┼──► Finance / core ledger API
 │   (xlsx/csv/AS400)  (pure logic)         (ClosedXML)              (retry · circuit breaker ·        │    (Idempotency-Key)
 │                                                                     timeout · integration log)      │
 └──────────────────────────────────────────────┬──────────────────────────────────────────────────────┘
                                                │ EF Core 10 + stored procedures
                                                ▼
                         SQL Server: Locations · AppUsers · ReconciliationRuns · ReconciliationLines
                                     AuditLogs (append-only trigger) · IntegrationLogs · RunReferenceSeq
```

### Repository layout

| Path | Purpose |
|------|---------|
| `src/PrimaFarm.StockReconciler.Web/` | ASP.NET Core MVC + Web API + SOAP application |
| `  Services/Reconciliation/` | File readers (Excel, CSV, AS400 fixed-width), comparison engine, Excel report |
| `  Services/Integration/` | Outbound finance posting client |
| `  Services/Security/` | PBKDF2 password hashing, login/lockout, API keys |
| `  Services/Soap/` | SOAP service contract for legacy consumers |
| `  Infrastructure/` | Correlation id, security headers, API-key auth handler, upload validation |
| `  Controllers/Api/` | REST API v1 + simulated finance API (dev only) |
| `database/` | T-SQL: schema, stored procedures/views, seed data, backup & maintenance jobs |
| `tests/` | xUnit tests (engine, file formats, AS400 control totals, security) |
| `samples/` | Synthetic input files, REST JSON and SOAP XML request samples |
| `deploy/iis-deploy.ps1` | IIS deployment with backup, health check and automatic rollback |
| `.github/workflows/ci-cd.yml` | Build → test → vulnerability scan → SQL validation → UAT → PROD |
| `docs/` | Runbook, security (OWASP) mapping, UAT & change management |

---

## How the job requirements are covered

| Requirement | Where in this project |
|---|---|
| **C#, .NET / ASP.NET Core / MVC** | .NET 10, Razor MVC views, DI, options pattern, middleware, typed HttpClient |
| **Web API / REST** | `api/v1/locations`, `api/v1/reconciliations` (GET search/paging, GET by ref, GET variances, POST 201 + Location), ProblemDetails errors, OpenAPI at `/openapi/v1.json` (dev) |
| **SOAP, JSON / XML** | SoapCore endpoint `/soap/StockReconciliation.asmx` (WSDL); REST content negotiation `Accept: application/xml`; DataContract XML |
| **SQL Server, T-SQL** | `database/*.sql`: constraints, CHECKs, FKs, covering indexes, SEQUENCE, view, 7 stored procedures (paging with OFFSET/FETCH + `COUNT(*) OVER()`, `OPTION (RECOMPILE)` for optional filters, batched purge, atomic lockout), MERGE seed, INSTEAD OF trigger |
| **Entity Framework** | `AppDbContext`: entity mapping, keyless SP result sets, `FromSqlInterpolated` (parameterised), OUTPUT parameters, `ROWVERSION` optimistic concurrency, retry-on-failure execution strategy |
| **System integration (core banking / payments / third party)** | `FinancePostingClient`: posts the approved variance journal with Idempotency-Key, standard resilience pipeline (retry + jitter, circuit breaker, timeouts), full request/response logging to `IntegrationLogs`; inbound REST & SOAP for other systems |
| **IBM i / AS400, DB2, RPG** | ERP side accepts the IBM i fixed-width extract (H/D/T records, RPG-style trailing sign, implied decimals) and **rejects the file if trailer control totals don't match** |
| **Git and CI/CD** | `.gitignore` (no secrets / real data), GitHub Actions: warnings-as-errors build, tests + coverage, vulnerable package gate, SQL scripts applied twice on a SQL Server container, artifact, UAT and PROD environments with approval gates |
| **IIS, Windows Server, troubleshooting** | `web.config` (in-process, request limits, header removal), `iis-deploy.ps1` (app_offline, backup, robocopy, health check, rollback), Data Protection key persistence, `docs/RUNBOOK.md` |
| **OWASP / secure coding** | See `docs/SECURITY.md` — PBKDF2 600k, lockout, rate limiting, deny-by-default authZ, RBAC, CSRF, CSP + secure headers, upload signature checks, parameterised SQL, no secrets in config, generic errors with correlation id, dependency scanning |
| **Incidents & production issues** | Correlation id on every response/log/audit/integration row, Serilog rolling files, `/health/ready`, error page shows the reference to quote |
| **Requirements, design, testing, UAT, deployment** | This README (design), `docs/UAT-AND-CHANGE-MANAGEMENT.md` (UAT scripts, release checklist), unit tests, CI pipeline |
| **Change management, version control, documentation** | Numbered idempotent DB scripts, approval-gated environments, rollback procedure, docs folder |
| **Performance monitoring, audit, business continuity** | Request timing logs, health checks, append-only audit trail, integration log, `900_backup_and_maintenance.sql` (backup with CHECKSUM + VERIFYONLY, CHECKDB, DR restore drill) |

---

## REST API examples

```bash
KEY="X-Api-Key: dev-erp-gateway-key-2026"     # Development key (only its SHA-256 hash is in config)

curl -H "$KEY" http://localhost:5012/api/v1/locations
curl -H "$KEY" -H "Accept: application/xml" http://localhost:5012/api/v1/reconciliations/REC-20261030-000001
curl -H "$KEY" "http://localhost:5012/api/v1/reconciliations/REC-20261030-000001/variances?top=10"
curl -H "$KEY" -H "Content-Type: application/json" --data @samples/api_reconcile_request.json http://localhost:5012/api/v1/reconciliations

# SOAP
curl -H "$KEY" "http://localhost:5012/soap/StockReconciliation.asmx?wsdl"
curl -H "$KEY" -H "Content-Type: text/xml" \
     -H 'SOAPAction: "http://primagroup.lk/threeacrefarms/stock/v1/IStockReconciliationSoapService/GetRunSummary"' \
     --data @samples/soap_get_run_summary.xml http://localhost:5012/soap/StockReconciliation.asmx
```

Generate a key hash for a new API client:
`powershell -c "[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('the-new-key')))"`
and add it under `ApiClients:Clients` (environment variable `ApiClients__Clients__<Name>` in production).

---

## Configuration

| Setting | Dev value | Production |
|---|---|---|
| `ConnectionStrings:StockDb` | LocalDB | App-pool env var `ConnectionStrings__StockDb` (Windows auth to SQL) |
| `Database:AutoDeploy` | `true` | `false` — DBA runs `database/0*.sql` via change request |
| `Integration:Finance:BaseUrl` / `ApiKey` | in-app mock | real endpoint; key via env var `Integration__Finance__ApiKey` |
| `MockFinance:Enabled` | `true` (30% simulated 503s) | `false` |
| `DataProtection:KeyPath` | empty | shared folder, so sign-ins survive recycles / web farm |
| `Security:*` | 5 attempts / 15 min lockout / 30 min idle timeout / 10 MB uploads | per policy |
