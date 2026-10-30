# Security design — OWASP Top 10 (2021) mapping

| OWASP risk | Control in this application | Code |
|---|---|---|
| **A01 Broken Access Control** | Deny-by-default fallback policy (every endpoint requires sign-in unless `[AllowAnonymous]`); role policies `CanRun` / `CanReview` / `CanViewAudit`; maker-checker (creator cannot approve) enforced in the service, not only the UI; API uses a separate header scheme so cookies are never accepted there; local-only `returnUrl` (no open redirect) | `Program.cs`, `ReconciliationService.LoadForReviewAsync`, `AccountController` |
| **A02 Cryptographic Failures** | PBKDF2-HMAC-SHA256, 600,000 iterations, 16-byte random salt, constant-time compare; API keys stored only as SHA-256 hashes; HTTPS + HSTS (1 year) in production; `Secure`, `HttpOnly`, `SameSite=Strict` cookies; Data Protection keys persisted to a protected folder | `PasswordHasher`, `ApiKeyValidator`, `Program.cs` |
| **A03 Injection** | All SQL through EF Core LINQ or `FromSqlInterpolated` / `ExecuteSqlInterpolated` (parameterised); stored procedures take typed parameters; database name in `CREATE DATABASE` guarded by `QUOTENAME`; Razor encodes all output; CA2100 analyzer is a build error | `AppDbContext`, `DbInitializer`, `.editorconfig` |
| **A04 Insecure Design** | Maker-checker workflow, optimistic concurrency (`ROWVERSION`) prevents double approval, idempotent finance posting, AS400 control totals reject partial files, append-only audit | `ReconciliationService`, `FinancePostingClient`, `StockFileReader` |
| **A05 Security Misconfiguration** | CSP without `unsafe-inline` (all JS in files), `X-Frame-Options: DENY`, `nosniff`, Referrer-Policy, Permissions-Policy, `Cache-Control: no-store`; `Server` / `X-Powered-By` headers removed; no developer exception page; OpenAPI only in Development; mock finance API disabled outside Development | `SecurityHeadersMiddleware`, `web.config`, `Program.cs` |
| **A06 Vulnerable Components** | CI fails on any vulnerable direct or transitive NuGet package; patched `System.Security.Cryptography.Pkcs` pinned after the scan flagged it | `ci-cd.yml`, `.csproj` |
| **A07 Identification & Authentication Failures** | Lockout after 5 failures for 15 minutes (atomic T-SQL), login rate limit 10/min/IP, identical message and timing for unknown user vs bad password (dummy hash verification), 30-minute sliding session, logout is POST + CSRF | `AuthService`, `usp_RecordLoginAttempt`, `Program.cs` |
| **A08 Software & Data Integrity Failures** | Anti-forgery token on every form (global `AutoValidateAntiforgeryToken`); file signature checks (xlsx must be a ZIP container, text files must not contain binary); package built once in CI and promoted unchanged UAT → PROD | `FileUploadValidator`, `ci-cd.yml` |
| **A09 Logging & Monitoring Failures** | Audit trail for login success/failure/lockout, runs, approvals, downloads, postings — with IP and correlation id, protected by an `INSTEAD OF UPDATE, DELETE` trigger; Serilog structured logs with correlation id; integration log; secrets never logged; correlation id sanitised to prevent log injection | `AuditService`, `001_schema.sql`, `CorrelationIdMiddleware` |
| **A10 SSRF** | Outbound calls go only to the configured finance base URL; no user-supplied URLs are fetched | `FinancePostingClient` |

## File upload controls (OWASP File Upload Cheat Sheet)

- Extension allow-list: `.xlsx .xlsm .csv .txt .dat`
- Size limit 10 MB per file (configurable), 40 MB per request (Kestrel and IIS `maxAllowedContentLength`)
- Content signature check; client-supplied path stripped from the file name
- Files are processed in memory and **never written to disk or served back**; reports are regenerated from the database

## Secrets

No production secret is in source control. Connection strings and the finance API key are supplied as
IIS application-pool environment variables (`ConnectionStrings__StockDb`, `Integration__Finance__ApiKey`).
The seeded passwords are for development only and must be changed before go-live.

## Verified behaviour (manual test run, 2026-10-30)

| Test | Result |
|---|---|
| POST without anti-forgery token | 400 |
| Upload of a non-Excel file renamed to `.xlsx` | Rejected: "not a valid Excel workbook" |
| Officer opens `/Admin/Audit` / posts to `/Runs/Approve` | Redirected to Access denied |
| 5 wrong passwords, then correct password | 6th attempt: account locked |
| `DELETE FROM dbo.AuditLogs` | Blocked by trigger |
| Admin approves own reconciliation | Blocked: segregation of duties |
| Replay of an approval with stale row version | Blocked: "already Posted" |
| API with no key / wrong key / cookie only | 401 |
| SOAP WSDL without key | 401 |
