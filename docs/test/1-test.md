---
title: "1 - Backend Tests"
tags: [test, service-scheduler]
ticket: 1
status: executed
updated: 2026-10-31
---

# 1 - Backend tests

> [!IMPORTANT]
> Executed on 2026-10-31 against the ticket 1 implementation based on HEAD `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` with the staged working tree. Result: 21 of 21 cases **Passed**, 0 Failed, 0 Blocked, 0 Not run. Automated suites: 17 unit tests and 29 integration tests passed with 0 failures or skips. TC-21 readiness recovery succeeded without an API restart but took up to about 10 seconds after the SQL Server restart in one controlled cycle; see [[#TC-21: Readiness recovers after a database outage|TC-21]] and [[#Issues and follow-up|Issues and follow-up]].

## Scope and prerequisites

- Ticket: [[1 - Init Project]]
- Research / plan / reviewed revision: research verified 2026-10-31; plan verified 2026-10-31; implementation present in the staged working tree based on HEAD `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1`. The implementation is uncommitted; a temporary clean copy was materialized from the staged index, and `git diff` reported no unstaged changes.
- Requirements and exclusions: S1-S11, AC-1-AC-9, T-14, T-16; foundation portions of D-01-D-04 and P-04. Booking endpoints/procedure, Redis, Serilog/OpenTelemetry are excluded and were not exercised.
- Environment: .NET SDK `10.0.401` (official win-x64 archive, installed side-by-side outside the repository because the host default SDKs were `8.0.100`, `9.0.320`, and `10.0.403` and `global.json` disables roll-forward); Docker server `25.0.3`; Docker Compose `v2.24.6-desktop.1`; Linux `x86_64` engine with 8.2 GiB available; SQL Server image `mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04@sha256:0ec7739e1c5ec2f57861facbe1f2b74f1d3e147c7c97edf91eeea920c5944d9c`.
- Clean copy: materialized outside the repository with `git -c core.autocrlf=false checkout-index --all --prefix=<temporary directory>`. The host has `core.autocrlf=true` and the repository has no `.gitattributes`; a first materialization without the override wrote CRLF and failed format verification. The index and working tree are LF (`git ls-files --eol` reported `i/lf w/lf`), and sampled files in the clean copy matched the working-tree SHA-256 values.
- Configuration source: a temporary ignored `.env` copied from `.env.example` with per-run synthetic passwords for `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, and the matching connection strings. Secret values were not recorded in this report.
- Migration / seed baseline: `20261031092016_InitialCreate`; fixed synthetic dealership/customer/vehicle/service/technician/qualification/bay identities; time-relative weekday shifts from the setup clock; no appointments.
- Test projects and executed commands:

  ```powershell
  dotnet restore
  dotnet build --no-restore
  dotnet format --verify-no-changes
  dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
  dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
  ```

- Setup and cleanup: the SQL fixture started one pinned container per collection, created one uniquely named database per test class through the production setup component, and dropped those databases when the collection fixture was disposed. Operational Compose checks used the named `scheduler-sql-data` volume; `docker compose down -v` was executed as the documented destructive reset, and the temporary stack was removed after the run.

## Requirement coverage

| Requirement ID | Scenario IDs | Level | Automated test file / symbol or manual case | Status |
| --- | --- | --- | --- | --- |
| S1, AC-7 | TC-20 | API integration | `Startup/ProblemDetailsTests.cs`; structural controller/OpenAPI registration (G-10) | Passed |
| S2, S3, AC-1 | TC-16, TC-17 | Command | `dotnet restore`, `dotnet build`, `dotnet format --verify-no-changes`, solution/project files | Passed |
| S4, AC-3 | TC-01, TC-02 | Unit + API integration | `Configuration/DatabaseOptionsValidatorTests.cs`; `Startup/StartupValidationTests.cs` | Passed |
| S5, AC-4, AC-5 | TC-06, TC-07, TC-12 | SQL integration | `Persistence/MigrationAndSeedTests.cs`; `Persistence/ConstraintTests.cs` | Passed |
| S6, AC-5 | TC-09, TC-10, TC-11, TC-13 | SQL integration | `Security/RuntimePermissionTests.cs` | Passed |
| S7, AC-4 | TC-07, TC-08 | SQL integration | `Persistence/MigrationAndSeedTests.cs` (`StaticSeeds...`, `FutureShifts...`) | Passed |
| S8, AC-8 | TC-18, TC-19 | Operational | `compose.yaml`, `src/ServiceScheduler.Api/Dockerfile`, [[deploy-notes/1-deploy-notes\|deployment notes]] | Passed |
| S9, AC-6, AC-7, T-14 | TC-03, TC-04, TC-05, TC-14, TC-15, TC-21 | API integration + operational | `Health/HealthEndpointTests.cs`; `Health/InitializedReadinessTests.cs`; `OpenApi/OpenApiTests.cs` | Passed (TC-21 recovery delay up to about 10 s) |
| S10, AC-2 | TC-01-TC-21 | Unit/API/SQL integration | Both test projects and SQL fixtures | Passed |
| S11, AC-9 | Documentation review | Manual | README, architecture, setup guide, database records, deploy notes | Passed with open low-severity follow-ups |
| T-16 | TC-18, TC-19 | Operational | Clean-checkout command sequence, Compose restart, named-volume persistence, destructive reset | Passed |

## Test cases

### TC-01: Missing runtime configuration fails startup

> [!NOTE]
> Covers: S4, AC-3. Level: unit + API integration.

- Preconditions and synthetic data: no valid `ConnectionStrings:Runtime`; no SQL server required.
- **Step 1:** Run `DatabaseOptionsValidator.Validate` with a null, empty, or whitespace runtime value.
    - **Expected:** Validation fails and the message names `ConnectionStrings:Runtime`.
- **Step 2:** Start the API host with an empty runtime connection through `WebApplicationFactory`.
    - **Expected:** Startup fails before serving requests and identifies the missing key without exposing a value.
- Cleanup: dispose the factory.
- Execution status: Passed
- Observed result / evidence: `DatabaseOptionsValidatorTests` theory cases for null, empty, and whitespace values passed as part of the 17/17 unit test run. `StartupValidationTests.MissingRuntimeConnectionFailsStartup` passed as part of the 29/29 integration run; the failure identified `ConnectionStrings:Runtime` without a configuration value.

### TC-02: Malformed runtime configuration fails startup

> [!NOTE]
> Covers: S4, AC-3. Level: unit + API integration.

- **Step 1:** Validate or start the host with a malformed runtime connection string.
    - **Expected:** Validation fails with a clear `ConnectionStrings:Runtime` message; no SQL connection is required to detect it.
- Cleanup: dispose the factory.
- Execution status: Passed
- Observed result / evidence: `DatabaseOptionsValidatorTests.MalformedConnectionStringIsRejected`, missing-component, `sa`, and infinite-timeout cases passed; `StartupValidationTests.MalformedRuntimeConnectionFailsStartup` passed. No SQL connection was opened by validation.

### TC-03: Liveness remains available during a database outage

> [!NOTE]
> Covers: S9, AC-6, T-14. Level: API integration.

- Preconditions: runtime connection points to a syntactically valid non-listening endpoint.
- **Step 1:** Request `GET /health/live`.
    - **Expected:** `200` while the process runs.
- Execution status: Passed
- Observed result / evidence: `HealthEndpointTests.LivenessIsAvailableWhenDatabaseIsUnreachable` passed. Operationally, Compose returned `live=200` during all three SQL Server stops.

### TC-04: Readiness fails during a database outage

> [!NOTE]
> Covers: S9, AC-6, T-14. Level: API integration.

- **Step 1:** Request `GET /health/ready` against the non-listening endpoint.
    - **Expected:** `503` within the configured check timeout.
- Execution status: Passed
- Observed result / evidence: `HealthEndpointTests.ReadinessFailsWhenDatabaseIsUnreachable` passed. Operationally, Compose returned `ready=503` during each SQL Server stop, with live remaining `200`.

### TC-05: Readiness succeeds against an initialized database

> [!NOTE]
> Covers: S9, AC-6, T-14, AC-7. Level: API integration.

- Preconditions: class database initialized through `DatabaseSetupRunner`; runtime connection uses `scheduler_app`.
- **Step 1:** Request `GET /health/ready`.
    - **Expected:** `200`; liveness remains independent.
- Execution status: Passed
- Observed result / evidence: `InitializedReadinessTests.ReadinessReportsHealthyForAnInitializedDatabaseUsingTheRuntimeIdentity` passed. In Compose, `ready=200` and `live=200` against the freshly initialized database and after the named-volume restart.

### TC-06: Initial migration creates the schema contract

> [!NOTE]
> Covers: S5, AC-4. Level: SQL integration.

- **Step 1:** Apply the migration to an empty real SQL Server database through the fixture.
    - **Expected:** All ten domain tables, primary/alternate keys, named checks/indexes, composite FKs with `NO_ACTION`, and `InitialCreate` history exist; no procedure exists.
- **Step 2:** Inspect `dotnet ef migrations script` output against [[database/schema|Schema]].
    - **Expected:** Names, types, nullability, FK actions, check expressions, and index inventory match the contract with no redundant convention indexes.
- Execution status: Passed
- Observed result / evidence: `MigrationAndSeedTests.InitialMigrationCreatesTheSchemaContract` passed against the real SQL Server container. `dotnet tool restore` and `dotnet-ef migrations script` generated the 239-line `InitialCreate` script; inspection found the ten `dbo` domain tables, primary keys, alternate unique keys for the composite associations, named UTC/duration/interval/status checks, `NO ACTION` composite and single-column foreign keys, the named appointment/shift/catalog indexes, unique VIN and shift indexes, the `20261031092016_InitialCreate` history insert, and no stored procedure. Operational queries reported 11 `sys.tables` (ten domain tables plus `__EFMigrationsHistory`), 10 check constraints, and 14 foreign keys.

### TC-07: Static seeds are deterministic

> [!NOTE]
> Covers: S5, S7, AC-4. Level: SQL integration.

- **Step 1:** Read seeded rows after setup.
    - **Expected:** Two supported IANA-zone dealerships, weekday opening hours, customers/vehicles with uppercase 17-character VINs, positive-duration service types, active/inactive technicians and bays, same-dealership qualifications, and zero appointments; fixed identities match `SeedData`.
- Execution status: Passed
- Observed result / evidence: `MigrationAndSeedTests.StaticSeedsAreDeterministicAndAppointmentsAreEmpty` passed. Operational counts after fresh setup were 2 dealerships, 2 customers, 2 vehicles, 3 service types, 4 technicians, 5 qualifications, 4 active/inactive bays, 0 appointments; `Asia/Ho_Chi_Minh` and `Europe/London` were present and both zones resolved at setup.

### TC-08: Future shifts cover the horizon and reruns do not duplicate them

> [!NOTE]
> Covers: S7, AC-4, T-16. Level: SQL integration.

- Preconditions: fixed setup clock `2026-11-02T00:00:00Z`.
- **Step 1:** Inspect shifts for active technicians.
    - **Expected:** Weekday 09:00-17:00 local intervals for the next 28 local calendar days, stored with zero UTC offset; no shifts for inactive technicians.
- **Step 2:** Rerun the production setup component.
    - **Expected:** Shift identities and counts are unchanged; migration history is unchanged; no duplicate user/login/grants.
- Execution status: Passed (automated principal/permission assertion added 2026-11-02; see R-05 resolution)
- Observed result / evidence: `MigrationAndSeedTests.FutureShiftsCoverTheHorizonAndRerunIsIdempotent` passed: 40 shifts, 20 per active technician, weekday 09:00-17:00 local, zero UTC offsets, first local day 2026-11-03 and last 2026-11-30, no inactive-technician shifts, and identical shift IDs and migration history after the in-test setup rerun. Operationally, `docker compose down` followed by `up` preserved the same first shift ID (`FC0AF7B9-F909-4D55-9AEC-0455725EE7DD`) and 40 shifts, and queries after the rerun reported one `scheduler_app` login, one database user, and two permission rows. The automated test did not assert principal/permission counts at this revision (R-05); the operational query supplied that evidence for this run. The 2026-11-02 re-review extended the automated test to assert one `scheduler_app` login, one database user, and six permission rows before and after setup (the count includes the ticket 3 procedure/deny grants); see [[review/1-review|the ticket 1 review]] R-05 resolution.

### TC-09: Runtime identity can read seeded data

> [!NOTE]
> Covers: S6, AC-5. Level: SQL integration.

- **Step 1:** Open an independent runtime connection and read dealerships.
    - **Expected:** Reads succeed and `CURRENT_USER` is `scheduler_app`.
- Execution status: Passed
- Observed result / evidence: `RuntimePermissionTests.RuntimeReadsSeededDataAsRestrictedUser` passed. An independent operational `sqlcmd` login as `scheduler_app` after the SQL restart returned `DbName=ServiceScheduler`, `LoginName=scheduler_app`, and 2 dealerships.

### TC-10: Direct runtime appointment writes are denied

> [!NOTE]
> Covers: S6, AC-5. Level: SQL integration.

- **Step 1:** Attempt `INSERT`, `UPDATE`, and `DELETE` on `dbo.Appointments` with the runtime identity.
    - **Expected:** Each attempt fails with a permission error and `Appointments` remains unchanged.
- Execution status: Passed
- Observed result / evidence: `RuntimePermissionTests.RuntimeCannotWriteAppointmentsDirectly` passed; each statement failed with a permission error and the final appointment count remained 0.

### TC-11: Runtime schema changes are denied

> [!NOTE]
> Covers: S6, AC-5. Level: SQL integration.

- **Step 1:** Attempt `CREATE TABLE`, `ALTER TABLE`, and `DROP TABLE` with the runtime identity.
    - **Expected:** Each attempt fails with a permission error; no probe object or column exists and `Dealerships` remains.
- Execution status: Passed
- Observed result / evidence: `RuntimePermissionTests.RuntimeCannotChangeSchema` passed; no probe object or column persisted.

### TC-12: Invalid rows are rejected by database constraints

> [!NOTE]
> Covers: S5, AC-5. Level: SQL integration.

- **Step 1:** Insert duplicate VIN, non-positive duration, invalid shift/opening interval, duration/end mismatch, unsupported status, cross-dealership appointment, customer/vehicle mismatch, cross-dealership qualification, invalid weekday, and non-zero UTC offsets with the setup identity.
    - **Expected:** Every attempt fails with the expected unique/check/FK constraint and leaves no probe row.
- Execution status: Passed
- Observed result / evidence: All eleven `ConstraintTests` facts passed, including duplicate VIN, non-positive duration, invalid shift interval, duration snapshot mismatch, unsupported status, cross-dealership appointment, customer/vehicle mismatch, cross-dealership qualification, invalid opening-hours interval and weekday, non-UTC shift and appointment values, and the valid-appointment rollback probe.

### TC-13: Runtime role and permission surface is restricted

> [!NOTE]
> Covers: S6, AC-5. Level: SQL integration.

- **Step 1:** Query role membership and effective permissions from the runtime connection.
    - **Expected:** Not `sysadmin`, `db_owner`, `db_datawriter`, `db_ddladmin`, `db_securityadmin`, or `db_datareader`; no appointment write or DDL permission; schema `SELECT` is allowed.
- Execution status: Passed
- Observed result / evidence: `RuntimePermissionTests.RuntimeHasOnlyTheExpectedPermissions` and `SetupFailsWhenRuntimeUserHasUnexpectedRoleMembership` passed. The operational principal query found one runtime login, one runtime user, and two permission rows (`CONNECT` and schema `SELECT`), with no write or DDL grant.

### TC-14: OpenAPI document is available

> [!NOTE]
> Covers: S9, AC-7. Level: API integration.

- **Step 1:** Request `GET /openapi/v1.json`.
    - **Expected:** `200` with valid JSON containing document metadata; an empty `paths` object is acceptable in ticket 1.
- Execution status: Passed
- Observed result / evidence: `OpenApiTests.OpenApiDocumentIsAvailable` passed. The Compose probe returned `openapi=200` with valid JSON (`openapi` 3.1.1, title/version/servers present, empty `paths` as allowed by G-26).

### TC-15: Swagger UI is available

> [!NOTE]
> Covers: S9, AC-7. Level: API integration.

- **Step 1:** Request `GET /swagger/index.html` and the served initializer script.
    - **Expected:** The UI loads and references `/openapi/v1.json`.
- Execution status: Passed
- Observed result / evidence: `OpenApiTests.SwaggerUiIsAvailableAndReferencesTheOpenApiDocument` passed. The Compose probe returned `swagger=200`; `/swagger/index.js` contained one reference to `/openapi/v1.json`.

### TC-16: Unit and integration test projects execute

> [!NOTE]
> Covers: S10, AC-2. Level: command.

- **Step 1:** Run both test projects with the pinned SDK.
    - **Expected:** Both execute and pass; the report records counts, revision, dependency versions, and any blocked SQL/Docker prerequisite.
- Execution status: Passed
- Observed result / evidence: `dotnet test .../ServiceScheduler.UnitTests.csproj --no-build` reported `Failed: 0, Passed: 17, Skipped: 0, Total: 17`; `dotnet test .../ServiceScheduler.IntegrationTests.csproj --no-build` reported `Failed: 0, Passed: 29, Skipped: 0, Total: 29` in 42 s. The integration run started the pinned SQL Server container and completed without a blocked prerequisite. The 46 total tests cover TC-01-TC-15 and TC-20.

### TC-17: Format verification passes

> [!NOTE]
> Covers: S4, AC-3. Level: command.

- **Step 1:** Run `dotnet format --verify-no-changes`.
    - **Expected:** Exit code `0`.
- Execution status: Passed
- Observed result / evidence: Exit code `0` in the working tree and in the clean LF copy. Caveat: a first clean copy materialized while the host `core.autocrlf=true` setting converted files to CRLF failed with `ENDOFLINE` errors in `SchedulerSetupOptionsTests.cs`; the repository content and working tree are LF. See [[#Issues and follow-up|Issues and follow-up]].

### TC-18: Clean-checkout commands work in order

> [!NOTE]
> Covers: AC-1, AC-3, T-16. Level: operational.

- **Step 1:** From a clean checkout, run restore, build, format, unit/integration tests, `docker compose config`, `docker compose up -d --wait`, and the health/OpenAPI/Swagger checks.
    - **Expected:** Documented commands execute in order; record actual output, SDK/package/image versions, and any blocked prerequisite.
- Execution status: Passed
- Observed result / evidence: In the clean copy, `dotnet restore` exited `0`; `dotnet build --no-restore` exited `0` with 0 warnings and 0 errors; `dotnet format --verify-no-changes` exited `0`; both test projects passed 17/17 and 29/29. `docker compose config --quiet` exited `0` with services `sqlserver`, `db-setup`, and `api`. A first `up` attempt against a reused named volume failed because the volume retained the earlier synthetic `sa` password; after the documented `docker compose down -v` reset, `docker compose up -d --build --wait` exited `0`, SQL became healthy, `db-setup` exited `0`, the API started, and live/ready/OpenAPI/Swagger returned `200`. The clean copy was materialized from the staged index because the implementation is uncommitted; see the clean-copy and line-ending notes above.

### TC-19: Named-volume restart preserves data

> [!NOTE]
> Covers: S8, AC-8, T-16. Level: operational.

- **Step 1:** Stop/restart the stack without removing the named volume.
    - **Expected:** Database, migration history, runtime identity, and seeds persist; no duplicate shifts.
- **Step 2:** Run `docker compose down -v` and reinitialize.
    - **Expected:** The reset is destructive as documented and a fresh setup succeeds.
- Execution status: Passed
- Observed result / evidence: Step 1: `docker compose down` exited `0` and left `unified-service-scheduler_scheduler-sql-data` present; `docker compose up -d --wait` exited `0`, `db-setup` exited `0`, readiness returned `200`, migration history remained `20261031092016_InitialCreate`, the shift count remained 40 with first ID `FC0AF7B9-F909-4D55-9AEC-0455725EE7DD`, and the principal counts remained one login and one user. Step 2: `docker compose down -v` exited `0` and the volume was absent; `docker compose up -d --wait` exited `0`, `db-setup` exited `0`, live/ready returned `200`, migration history contained `20261031092016_InitialCreate`, `sys.tables` reported 11 tables (ten domain plus history), and the fresh data contained 40 shifts and 0 appointments.

### TC-20: Unknown route returns Problem Details

> [!NOTE]
> Covers: S1, AC-7. Level: API integration.

- **Step 1:** Request an unknown route.
    - **Expected:** `404` with `application/problem+json`, status/title/trace ID, and no stack trace or configuration value.
- Execution status: Passed
- Observed result / evidence: `ProblemDetailsTests.UnknownRouteReturnsProblemDetailsWithTraceId` passed. The Compose probe returned `404` with `Content-Type: application/problem+json` and a body containing RFC 9110 type, `title=Not Found`, `status=404`, and a trace ID; no stack trace or configuration value was present.

### TC-21: Readiness recovers after a database outage

> [!NOTE]
> Covers: S9, AC-6, T-14. Level: operational/integration.

- **Step 1:** With an initialized database, stop SQL Server and request liveness/readiness.
    - **Expected:** Liveness remains `200`; readiness returns `503`.
- **Step 2:** Restart SQL Server and request readiness again.
    - **Expected:** Readiness returns `200`; liveness remained available throughout.
- Execution status: Passed (recovery delay observed)
- Observed result / evidence: Step 1 passed in all five observed cycles: `live=200` and `ready=503` while SQL Server was stopped. Step 2 initially appeared to fail: after SQL Server restarted and the container health check reported healthy, `/health/ready` remained `503` across four quick requests (the health-check log showed `Unhealthy completed after 3.6713ms`, then 0.9499ms and 2.9177ms), and an API restart then produced `200`. Controlled reruns from the repository root using the same implementation and pinned images, with the same running API process and without an API restart, showed that readiness recovers on its own: one cycle returned `200` after five `503` responses at 2-second retry intervals (about 10 seconds), and another returned `200` on the first request. Direct `sqlcmd` connectivity as `scheduler_app` and the SQL Server database state (`ONLINE`) confirmed the database itself was reachable during the delay. Conclusion: T-14 recovery is met without restarting the API, with a bounded readiness delay of up to about 10 seconds after the SQL Server restart; the earlier API restart was a timing artifact. Recommended follow-up: if immediate readiness restoration is required, consider invalidating or retrying the readiness connection pool within the check, and document the observed delay.

## Execution record

| Date / time and zone | Revision | Case / actual command | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| 2026-10-31T18:00+07:00 | HEAD `ba36ac8`; staged ticket 1 working tree | SDK setup and clean-copy materialization | Official SDK `10.0.401` installed side-by-side; clean copy created with `git -c core.autocrlf=false checkout-index`; `git diff` empty; sampled SHA-256 values matched the working tree | Passed | SDK `dotnet --version` = `10.0.401` |
| 2026-10-31T18:00+07:00 | Same | `dotnet restore`; `dotnet build --no-restore`; `dotnet format --verify-no-changes` in the clean copy | Restore exit `0`; build exit `0`, 0 warnings, 0 errors; format exit `0` | Passed | Clean-copy command output |
| 2026-10-31T18:05+07:00 | Same | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | `Failed: 0, Passed: 17, Skipped: 0, Total: 17` | Passed | Test summary |
| 2026-10-31T18:06+07:00 | Same | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | `Failed: 0, Passed: 29, Skipped: 0, Total: 29`, 42 s, real SQL Server container | Passed | Test summary |
| 2026-10-31T18:07+07:00 | Same | `dotnet tool restore`; `dotnet-ef migrations script --no-build -o /tmp/initial-migration.sql` | Exit `0`; 239-line script; ten domain tables, named checks/indexes, 14 FKs, history row, no procedure | Passed | `/tmp/initial-migration.sql` inspection |
| 2026-10-31T18:10+07:00 | Same | `docker compose config --quiet`; fresh `docker compose up -d --build --wait` after `down -v` | Exit `0`; SQL healthy; `db-setup` exit `0`; API started; live/ready/OpenAPI/Swagger `200`; unknown route `404 application/problem+json` | Passed | Compose output, HTTP probes, SQL queries |
| 2026-10-31T18:12+07:00 | Same | Operational database queries after fresh setup | 11 `sys.tables` (ten domain plus history), 10 checks, 14 FKs, `20261031092016_InitialCreate`, 2/2/2/3/4/5/4/40/0 row counts, 1 runtime login, 1 runtime user, 2 permissions | Passed | `sqlcmd` output |
| 2026-10-31T18:14+07:00 | Same | TC-19 step 1: `docker compose down`; `docker compose up -d --wait` | Volume present after `down`; setup exit `0`; ready `200`; migration and 40 shifts unchanged; same first shift ID; 1 login/1 user | Passed | Compose and `sqlcmd` output |
| 2026-10-31T18:15+07:00 | Same | TC-19 step 2: `docker compose down -v`; `docker compose up -d --wait` | Volume removed; fresh setup exit `0`; live/ready `200`; migration history, 11 tables, 40 shifts, 0 appointments | Passed | Compose and `sqlcmd` output |
| 2026-10-31T18:13+07:00 to 18:17+07:00 | Same | TC-21 initial cycles: stop SQL; request live/ready; start SQL; request ready; restart API once in cycle 1 | Step 1: live `200`, ready `503`. Step 2: first cycle saw `503` across four quick requests and an API restart then returned `200`; later cycles returned `200` on the first request. The API restart was later identified as a timing artifact | Passed (initial observation) | Health-check API log, SQL `ONLINE` and runtime `sqlcmd` checks, HTTP probes |
| 2026-10-31T18:25+07:00 | Same | TC-21 controlled reruns: same running API process, stop/start SQL, poll readiness with 2-second retries | Step 1: ready `503`. Step 2: one cycle recovered `200` after five `503` retries (about 10 seconds); a second cycle recovered `200` on the first request; no API restart was used | Passed (bounded recovery delay) | HTTP probes; SQL healthy before polling |
| 2026-10-31T18:17+07:00 | Same | Cleanup: `docker compose down -v` | Exit `0`; temporary stack and volume removed | Passed | Docker state |

## Issues and follow-up

- **TC-21 / T-14 (open, low; observed behavior):** Readiness recovery after a SQL Server restart can take up to about 10 seconds while the connection pool and health check recover; controlled reruns returned `200` without an API restart. If immediate recovery is required, consider invalidating or retrying the readiness connection pool within the check and document the delay. T-14 is met with this bounded recovery delay.
- **Line endings after checkout (open, low):** The repository has no `.gitattributes`, and the host `core.autocrlf=true` caused a plain checkout to write CRLF; `dotnet format --verify-no-changes` then failed with `ENDOFLINE` errors even though the index and working tree are LF. Add `.gitattributes` (for example `* text=auto eol=lf`) or document the required Git setting in [[setup-guide]].
- **Reused volume after credential change (resolved, low; related to review R-04):** The first Compose start reused the prior named volume, whose `sa` password differed from the new `.env`; SQL Server ignored the new password and the health check failed until `docker compose down -v`. `.env.example`, the README, and the setup guide now state that both placeholder values appear in four places, and the troubleshooting table records the reset requirement. The historical observation remains valid for the recorded run.
- **R-05 coverage gap (resolved, low):** TC-08's original automated rerun assertion covered shift identities, counts, and migration history but not login/user/grant counts. This run verified those counts operationally (1/1/2) after a Compose rerun. The 2026-11-02 re-review extended `FutureShiftsCoverTheHorizonAndRerunIsIdempotent` to assert one login, one user, and six permission rows before and after setup; the current ticket 3 schema adds the procedure `EXECUTE` grant and three table denies to the ticket 1 baseline.
- **Review findings R-02, R-03, R-04 (resolved, low):** Stale architecture compatibility wording, the `dotnet ef migrations list` connection guidance, and `.env.example` credential duplication were corrected on 2026-11-02 in `docs/architecture.md`, `docs/setup-guide.md`, `.env.example`, and `README.md`; see [[review/1-review|the ticket 1 review]] re-review. R-01's evidence-labeling concern is superseded by this execution record.
- **Unexecuted scope:** Ticket 1 has no business endpoint, so controller/DTO/async/cancellation behavior remains structural evidence only (G-10). Booking procedure, Redis, Serilog/OpenTelemetry remain outside ticket 1 and are not claimed.
- **Retest revision and results:** TC-21 was rerun with controlled polling on 2026-10-31 and passed without an API restart; no code change was made. All cases remain valid for the recorded revision unless the implementation changes.
