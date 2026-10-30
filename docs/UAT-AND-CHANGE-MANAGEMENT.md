# UAT, release and change management

## Branching and version control

| Branch | Purpose | Merges to |
|---|---|---|
| `main` | Production-ready; every commit is deployable | — |
| `develop` | Integration of completed features | `main` via PR after UAT sign-off |
| `feature/<ticket>-<short-name>` | One change request / defect | `develop` via PR (1 reviewer, CI green) |
| `hotfix/<ticket>` | Urgent production fix | `main` **and** `develop` |

Commit messages reference the ticket (`CR-1042: accept "Closing Stock" header`). Database changes are new
numbered, idempotent scripts — existing scripts are never edited after release.

## Definition of done for a change

- [ ] Requirement / defect reference recorded and acceptance criteria agreed with the business user
- [ ] Code reviewed in a pull request; CI green (build with warnings-as-errors, tests, vulnerability scan, SQL validation)
- [ ] Unit test added for the new rule or fixed defect
- [ ] Security impact considered (OWASP checklist in `SECURITY.md`)
- [ ] `README.md` / `RUNBOOK.md` updated if behaviour or operations change
- [ ] UAT passed and signed off by the business owner
- [ ] Change request approved by CAB with deployment window and rollback plan

## UAT test script

Environment: UAT site with a copy of production reference data. Use the files in `samples/` plus one real
anonymised file from each enabled farm.

| # | Scenario | Steps | Expected result | Pass/Fail |
|---|---|---|---|---|
| 1 | Sign-in | Sign in as officer | Dashboard shown, role badge "Officer" | |
| 2 | Lockout | Enter a wrong password 5 times, then the correct one | "Temporarily locked" message; audit shows `LOGIN_FAILED` ×5 + `LOGIN_LOCKED` | |
| 3 | Excel reconciliation | Kosgama, ERP `erp_kosgama_616.csv`, farm `farm_kosgama_616.xlsx` | 5 matched, 2 mismatched, 1 missing, 1 not in ERP, 1 summed | |
| 4 | AS400 extract | Same, with ERP `erp_kosgama_616_as400.txt` and farm `farm_kosgama_616.csv` | 5 / 2 / 1 / 1 / 0 | |
| 5 | Corrupt AS400 file | Delete the last (T) line of the extract and upload | Rejected: "no trailer (T) record" | |
| 6 | Wrong file type | Rename a PDF to `.xlsx` and upload | Rejected: "not a valid Excel workbook" | |
| 7 | Disabled farm | Try Halwathura | Tile disabled; API returns 400 "not enabled" | |
| 8 | Excel report | Download the report from scenario 3 | 6 tabs, colour-coded, matches screen totals | |
| 9 | Maker-checker | Officer runs; admin opens the run | Approve/Reject shown to admin only; admin cannot approve own run | |
| 10 | Approve & post | Admin approves | Status **Posted**, journal number shown, integration log row with HTTP 201 | |
| 11 | Finance outage | Stop the finance API, approve a run, restart it, press Retry | Status PostingFailed, then Posted; only **one** journal created | |
| 12 | Reject | Admin rejects with no reason / with a reason | Without: browser blocks it; with: status Rejected, reason shown | |
| 13 | Auditor | Sign in as auditor | Can view history/audit/integration log; cannot open *New reconciliation* | |
| 14 | REST API | Run the curl examples in the README | JSON and XML responses; 401 without key | |
| 15 | SOAP | Call `GetRunSummary` with `samples/soap_get_run_summary.xml` | SOAP response with run totals | |
| 16 | Session timeout | Stay idle for 30 minutes, click any link | Redirected to sign-in | |

Sign-off: Business owner ____________  IT lead ____________  Date ________

## Release checklist (production)

1. CAB approval reference: ________ · deployment window: ________
2. Confirm the latest successful backup (`msdb.dbo.backupset`) is less than 24 hours old; take an ad-hoc COPY_ONLY backup
3. Pipeline `deploy-prod` approved by the change manager. It applies DB scripts, then runs `iis-deploy.ps1`
   (app_offline → backup → copy → start → health check → automatic rollback on failure)
4. Smoke test: sign in, open history, download one report, `GET /health/ready` = Healthy
5. Monitor logs and the integration log for 30 minutes; close the change with evidence
6. **Rollback:** `iis-deploy.ps1 -Rollback`. Database scripts are additive, so the previous release keeps working against the new schema
