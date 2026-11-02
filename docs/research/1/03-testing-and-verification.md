---
title: "1 - Init Project: Testing and Verification Research"
tags: [research, service-scheduler, testing, verification]
ticket: 1
status: verified
updated: 2026-10-31
---

# 1 - Init Project: Testing and Verification Research

- Ticket: [[1 - Init Project]]
- Parent research: [[research/1/00-overview|Foundation research overview]]
- Part: 3 of 3
- Requirement coverage: S10; AC-1-AC-7; T-14, T-16; D-04 and the foundation portion of P-04.
- Evidence baseline: research baseline revision `25be1577c895204e58bc74b4f80336486d33bb9c`; rechecked at current revision `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` (2026-10-31); no test projects, fixtures, or executable tests exist.

> [!IMPORTANT]
> Every scenario in this part is planned. No test has been generated or executed, and no status below is Passed. The test phase must record actual commands, revisions, dependencies, and observed results in `docs/test/1-test.md`.

## Scope of this part

Cover the test architecture for ticket 1: xUnit unit and integration projects, WebApplicationFactory, real SQL Server Testcontainers, controllable time, reusable fixtures, migration/seed/configuration/startup/health/OpenAPI/restricted-identity checks, and the operational checks for T-16.

Booking concurrency, procedure locking, Redis behavior, telemetry correlation are outside ticket 1 and must not be claimed as verified here.

## Current architecture and files

| Component | Actual inspected evidence | Relevant behavior / gap |
| --- | --- | --- |
| Testing guide | [[test/README]] describes planned unit/API/integration/operational coverage and says commands are not scaffolded (`docs/test/README.md:8-11,24-42`). | Guidance only; no tests. |
| Backend template | [[test/backend-test-template]] defines requirement coverage, case sections, and execution-record fields. | Use for `docs/test/1-test.md`. |
| Integration template | [[test/integration-test-template]] defines real-dependency setup, identity, concurrency, and evidence fields. | Use for SQL Server cases; ticket-1 subset applies. |
| Test projects | No `tests/` directory exists. | Missing — proposed addition. |
| Test report | No `docs/test/1-test.md` exists. | Proposed addition for the test phase. |
| Application host | No `Program.cs`, API project, or DbContext exists. | Missing — tests cannot be written against a real host yet. |
| Architecture/testing rules | `docs/architecture.md:62,149`, `docs/test/README.md:34`, and `.claude/rules/auth-integrations.md` require real SQL Server and Testcontainers for relevant dependency checks. | Real SQL Server is required for migrations, permissions, and health; in-memory/SQLite is not a substitute. |

## Phase 1: Layer coverage sweep

| Component | What to inspect | Status / evidence |
| --- | --- | --- |
| Unit tests | Options validation, pure foundation helpers, deterministic time | Missing — proposed. |
| API integration tests | WebApplicationFactory startup, configuration, health, OpenAPI, Problem Details | Missing — proposed. |
| SQL Server integration tests | Migrations, schema, constraints, seeds, runtime permissions | Missing — proposed; real SQL Server required. |
| Runtime identity tests | Restricted reads, denied appointment writes, denied schema changes | Missing — proposed; independent runtime connection. |
| Startup/configuration tests | Missing and invalid configuration fail clearly; valid config starts without DB connection | Missing — proposed. |
| Health/outage tests | Liveness independent; readiness fails/recovers | Missing — proposed; T-14. |
| OpenAPI/Swagger tests | Document and UI accessible | Missing — proposed. |
| Operational/Compose checks | Named-volume persistence, clean-checkout commands | Missing — proposed; T-16. |
| Telemetry | Logs/metrics/traces | N/A — ticket 4. |
| Booking concurrency/procedure | Locks, conflicts, final state | N/A — ticket 3. |
| Redis cache | Hit/miss/expiry/outage | N/A — ticket 2. |
| Custom UI | Browser interaction | N/A — backend-only ticket; Swagger UI smoke only. |

## Phase 2: Test data flow and boundaries

Proposed integration-test flow:

```text
xUnit collection fixture
  -> Testcontainers SQL Server (pinned image, real SQL Server)
  -> privileged setup connection
     -> EF migrations
     -> runtime login/user + SELECT-only grants
     -> static seeds + future shifts relative to a controllable clock
  -> WebApplicationFactory<Program>
     -> runtime connection string and test environment
  -> HTTP requests / independent SqlConnection checks
  -> final database state assertions
```

Proposed outage flow:

```text
WebApplicationFactory with a syntactically valid runtime connection
  to a non-listening SQL endpoint
  -> host starts (startup validation does not connect)
  -> GET /health/live  = 200
  -> GET /health/ready = 503
```

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Fixture initialization | Container -> setup identity -> migrations/principal/seeds | Real SQL Server; setup identity only; deterministic IDs and clock. | Missing — proposed. |
| API integration | WebApplicationFactory -> runtime connection -> HTTP | Runtime identity; no setup credentials in the API. | Missing — proposed. |
| Permission checks | Independent runtime `SqlConnection` -> DML/DDL attempts | Runtime login; separate connection from setup. | Missing — proposed. |
| Constraint checks | Setup identity -> invalid inserts -> SQL errors | Setup identity is allowed to write test fixtures; runtime denial is tested separately. | Missing — proposed. |
| Health outage | Factory with non-listening SQL endpoint | Startup must not connect; readiness failure must not crash the process. | Missing — proposed; T-14. |
| Compose/named volume | Host -> Compose SQL Server -> named volume -> restart | Data survives `docker compose down` without `-v`. | Missing — proposed; T-16. |

## Phase 3: UX state matrix

N/A — no custom UI. Swagger UI accessibility is a smoke check, not a user-state matrix. No browser automation is required by the initial release.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict |
| --- | --- | --- | --- | --- |
| `docs/test/README.md` | Testing guidance | Planned guidance; no executed tests (`docs/test/README.md:8-11`). | S10, D-04 | No Change in research; use in test phase |
| `docs/test/backend-test-template.md` | Test report format | Template only. | S10, T-16 | No Change; copy in test phase |
| `docs/test/integration-test-template.md` | Real-dependency evidence format | Template only. | S10, T-14, T-16 | No Change; copy relevant cases |
| `docs/test/1-test.md` | Ticket test report | Does not exist. | S10, T-14, T-16 | Proposed Addition |
| Test project candidates | No `tests/` directory. | Nothing to inspect. | S10 | Missing — proposed addition |

### Gate 2: Call path and boundary verification

1. No test entry point, fixture, factory, or assertion exists.
2. No SQL container configuration or runtime connection can be traced.
3. No health/outage test can run against the missing host.
4. The target code does not execute on any current path because it does not exist; all test items are proposed additions.

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| `dotnet test` | None | N/A | No | Planned command; project/command pending. |
| Integration factory startup | None | Runtime identity | No | Proposed; test after scaffolding. |
| Runtime permission probe | None | Runtime login | No | Proposed; test denial. |
| Health outage probe | None | No DB connection at startup | No | Proposed; T-14. |
| Compose restart probe | None | Host Docker access | No | Proposed; T-16. |

## Phase 4: Code audit

| Classification | File / symbol / component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence |
| --- | --- | --- | --- | --- | --- |
| Proposed Addition | `tests/ServiceScheduler.UnitTests/` | Missing. | Add configuration validator and foundation unit tests; reference the API project. | S10, AC-2, AC-3 | Missing |
| Proposed Addition | `tests/ServiceScheduler.IntegrationTests/` | Missing. | Add WebApplicationFactory, Testcontainers SQL Server, fixtures, and ticket-1 scenarios. | S10, AC-2, AC-4-AC-7 | Missing |
| Proposed Addition | `SqlServerContainerFixture` | Missing. | Start a pinned real SQL Server container per collection; expose setup/runtime connections; apply migrations/principal/seeds. | S10, AC-4, AC-5 | Missing |
| Proposed Addition | `SchedulerApiFactory` | Missing. | Configure runtime connection, environment, and controllable clock for the API host. | S10, AC-2, AC-3, AC-6, AC-7 | Missing |
| Proposed Addition | Migration/seed/constraint tests | Missing. | Assert schema, seeds, identity behavior, and invalid-write rejection. | S10, AC-4, AC-5 | Missing |
| Proposed Addition | Health/OpenAPI/startup tests | Missing. | Assert T-14 and AC-3/AC-6/AC-7 behaviors. | S10, AC-3, AC-6, AC-7, T-14 | Missing |
| Proposed Addition | `docs/test/1-test.md` | Missing. | Record scenarios and executed evidence in the test phase. | S10, T-14, T-16 | Missing |
| No Change | `docs/test/README.md`, templates | General guidance. | No change during research. | Context | Inspected |
| No Change | `docs/review/README.md`, `docs/deploy-notes/README.md` | General guidance. | Use in later phases; no research change. | Context | Inspected |

## Phase 5: Gap analysis for this part

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact | Status |
| --- | --- | --- | --- | --- | --- |
| G-12 | Risk | Testcontainers SQL Server startup is slow/resource-heavy. | Adopt accepted resolution G-12 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, AC-2 | Mitigation selected; verification pending |
| G-13 | Risk | Simulating SQL Server outage without crashing the API. | Adopt accepted resolution G-13 in the overview's accepted resolutions table; earlier alternatives are superseded. | S9, AC-6, T-14 | Mitigation selected; verification pending |
| G-14 | Risk | Exact Testcontainers package/image compatibility. | Adopt accepted resolution G-14 in the overview's accepted resolutions table; earlier alternatives are superseded. | S3, S10 | Mitigation selected; verification pending |
| G-32 | Decision | How test databases get schema/identity/seeds. | Adopt accepted resolution G-32 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, AC-4, AC-5 | Resolved for planning; execution pending |
| G-36 | Risk | Database state leakage between tests. | Adopt accepted resolution G-36 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, AC-2, AC-5 | Mitigation selected; verification pending |
| G-37 | Risk | WebApplicationFactory startup-failure assertions can be awkward. | Adopt accepted resolution G-37 in the overview's accepted resolutions table; earlier alternatives are superseded. | S4, AC-3 | Mitigation selected; verification pending |
| G-38 | Decision | Controllable time for seeds/tests. | Adopt accepted resolution G-38 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, S7 | Resolved for planning; execution pending |
| G-39 | Risk | Secrets in test connections/logs. | Adopt accepted resolution G-39 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, S6 | Mitigation selected; verification pending |
| G-40 | Decision | Exact OpenAPI/Swagger routes and package. | Adopt accepted resolution G-40 in the overview's accepted resolutions table; earlier alternatives are superseded. | S9, AC-7 | Resolved for planning; execution pending |
| G-41 | Risk | Clean-checkout reproducibility requires a documented manual run. | Adopt accepted resolution G-41 in the overview's accepted resolutions table; earlier alternatives are superseded. | T-16, P-04 | Mitigation selected; verification pending |
| G-42 | Risk | Docker/named-volume checks may be unavailable in some environments. | Adopt accepted resolution G-42 in the overview's accepted resolutions table; earlier alternatives are superseded. | S8, AC-8, T-16 | Mitigation selected; verification pending |
| G-43 | Decision | Whether to add a test-only controller for Problem Details/controller behavior. | Adopt accepted resolution G-43 in the overview's accepted resolutions table; earlier alternatives are superseded. | S1, AC-7 | Resolved for planning; execution pending |

## Phase 6: Test plan and scenarios

The accepted resolutions in [[research/1/00-overview#Accepted resolutions for G-03 through G-43 (2026-10-31)]] govern this part: database-per-class isolation, shared setup path, fixed fake time, exact documentation routes, and no test-only controller. All scenarios remain Not run.

### 6.1 Test projects and proposed files

| Project / file | Purpose |
| --- | --- |
| `tests/ServiceScheduler.UnitTests/Configuration/DatabaseOptionsValidatorTests.cs` | Missing, malformed, and valid connection-string validation without a database. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/SqlServerContainerFixture.cs` | Pinned real SQL Server container, setup/runtime connections, migrations, principal, and seeds. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs` | WebApplicationFactory with runtime connection, test environment, and controllable clock. |
| `tests/ServiceScheduler.IntegrationTests/Startup/StartupValidationTests.cs` | Missing/invalid configuration fails clearly; valid configuration starts without a DB connection. |
| `tests/ServiceScheduler.IntegrationTests/Startup/ProblemDetailsTests.cs` | Unknown-route Problem Details status, body, trace ID, and secret/stack-trace exclusion. |
| `tests/ServiceScheduler.IntegrationTests/Health/HealthEndpointTests.cs` | Liveness/readiness with available and unavailable SQL Server; T-14. |
| `tests/ServiceScheduler.IntegrationTests/OpenApi/OpenApiTests.cs` | OpenAPI document and Swagger UI accessibility. |
| `tests/ServiceScheduler.IntegrationTests/Persistence/MigrationAndSeedTests.cs` | Migration history, expected tables, seeds, future shifts. |
| `tests/ServiceScheduler.IntegrationTests/Persistence/ConstraintTests.cs` | FK/check/unique constraint enforcement using the setup identity. |
| `tests/ServiceScheduler.IntegrationTests/Security/RuntimePermissionTests.cs` | Runtime read success; denied appointment writes; denied schema changes; restricted role assertions. |
| `docs/test/1-test.md` | Executed scenario record using [[test/backend-test-template]]. |

### 6.2 Scenario matrix

| ID | Requirement IDs | Level | Scenario | Expected result | Status |
| --- | --- | --- | --- | --- | --- |
| TC-01 | S4, AC-3 | Unit + API integration | Start with the runtime connection string missing. | Startup fails with a clear message naming the required configuration; no server starts. | Not run |
| TC-02 | S4, AC-3 | Unit + API integration | Start with a malformed runtime connection string. | Startup fails with a clear validation message; no SQL connection attempt is required to detect it. | Not run |
| TC-03 | S9, AC-6, T-14 | API integration | Valid but non-listening SQL endpoint. | `GET /health/live` returns `200`. | Not run |
| TC-04 | S9, AC-6, T-14 | API integration | Same non-listening SQL endpoint. | `GET /health/ready` returns `503`. | Not run |
| TC-05 | S9, AC-6, T-14, AC-7 | API integration | Initialized real SQL Server container. | `GET /health/ready` returns `200`. | Not run |
| TC-06 | S5, AC-4 | SQL integration | Apply migrations to an empty database. | All expected tables, keys, checks, indexes, and `InitialCreate` migration history exist. | Not run |
| TC-07 | S7, AC-4 | SQL integration | Run the seed path. | Seeded dealerships, opening hours, customers, vehicles, service types, technicians, qualifications, and bays exist with deterministic identities; appointments are empty. | Not run |
| TC-08 | S7, AC-4, T-16 | SQL integration | Inspect seeded future shifts relative to the controllable clock. | Active technicians have future shifts covering the documented horizon; rerunning seeds does not duplicate them. | Not run |
| TC-09 | S6, AC-5 | SQL integration | Open an independent runtime connection and read seeded data. | Reads succeed and `CURRENT_USER` is the restricted database user `scheduler_app`. | Not run |
| TC-10 | S6, AC-5 | SQL integration | Attempt direct runtime `INSERT`/`UPDATE`/`DELETE` on appointments. | Each attempt fails with a permission error; no appointment row is added or changed. | Not run |
| TC-11 | S6, AC-5 | SQL integration | Attempt runtime `CREATE TABLE`/`ALTER TABLE`. | Each attempt fails with a permission error; no schema object is created or changed. | Not run |
| TC-12 | S5, AC-5 | SQL integration | Insert invalid rows with the setup identity: duplicate VIN, non-positive duration, invalid interval, duration/end mismatch, invalid status, cross-dealership appointment, customer/vehicle mismatch, cross-dealership qualification. | Each attempt fails with the expected check/unique/FK error; no invalid row is persisted. | Not run |
| TC-13 | S6, AC-5 | SQL integration | Query runtime role/permission membership. | Runtime is not `sysadmin`, `db_owner`, `db_datawriter`, or `db_ddladmin`; appointment write/DDL permissions are absent. | Not run |
| TC-14 | S9, AC-7 | API integration | Request the OpenAPI document. | `200` with a JSON document; document metadata is present; an empty `paths` object is valid for ticket 1. | Not run |
| TC-15 | S9, AC-7 | API integration | Request Swagger UI. | Swagger UI page loads and references the OpenAPI document. | Not run |
| TC-16 | S10, AC-2 | Command | Run unit and integration test projects. | Both execute and pass; record counts and revision. | Not run |
| TC-17 | S4, AC-3 | Command | Run `dotnet format --verify-no-changes`. | Exit code 0. | Not run |
| TC-18 | AC-1, AC-3, T-16 | Operational | Clean checkout: restore, build, format, test, Compose startup, migration/seed, API startup, health/OpenAPI checks. | Documented commands work in order; record actual output. | Not run |
| TC-19 | S8, AC-8, T-16 | Operational | Start Compose, initialize, stop/restart without deleting the named volume. | Data and migration history persist while the volume remains; `docker compose down -v` is documented as destructive. | Not run |
| TC-20 | S1, AC-7 | API integration | Request an unknown route with Problem Details middleware enabled. | `404` with a consistent Problem Details body and trace ID; no stack trace or secret. | Not run |
| TC-21 | S9, AC-6, T-14 | Operational/integration | Start an initialized database, stop SQL Server, confirm readiness `503`, restart SQL Server, confirm readiness `200`. | Readiness fails during the outage and recovers when the database returns; liveness remains `200` throughout. | Not run |

### 6.3 Test fixture and data design

- Use `Testcontainers.MsSql` with the same pinned SQL Server 2022 Developer image/tag as Compose; set `ACCEPT_EULA=Y` and a synthetic test password.
- Start one container per SQL collection with parallelization disabled and an isolated database per class. Mutating tests roll back probe transactions or remove their own rows in `finally`; no reset library.
- Apply the real EF migrations with the privileged setup connection; create the runtime login/user and grants with the real principal script; seed through the real seed path.
- Use fixed GUIDs for seed assertions and unique GUID suffixes for permission/constraint probes.
- Use `TimeProvider.System` normally and `FakeTimeProvider` fixed at `2026-11-02T00:00:00Z` for seed tests, passed to the shared setup component; avoid `Thread.Sleep` and arbitrary waits.
- For outage tests, use a syntactically valid runtime connection to a non-listening endpoint; do not stop the shared container in a way that breaks other tests.
- Use independent `SqlConnection` instances for permission and constraint tests; do not reuse the WebApplicationFactory connection.
- Keep secrets out of reports; record configuration key names and identity names, not passwords.

### 6.4 Planned commands

These commands are proposed and must be confirmed after scaffolding; working directories and exact project/test filters are pending.

```powershell
dotnet restore
dotnet build --no-restore
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
dotnet format --verify-no-changes
docker compose config
docker compose up -d sqlserver
# run the documented privileged initialization/setup command
docker compose up -d api
```

### 6.5 Operational checks for T-16

1. Clone/copy the checkout to a clean temporary directory and use the documented prerequisites.
2. Run restore, build, format, and test commands; record the exact revision, SDK, package, and container versions.
3. Run `docker compose config` and start SQL Server; wait for its healthcheck.
4. Run the privileged initialization step; verify schema, runtime login, and seeds.
5. Start the API; check `/health/live`, `/health/ready`, `/openapi/v1.json`, and `/swagger/index.html`.
6. Verify runtime reads and denied appointment writes/schema changes using the implemented test or an independent SQL connection.
7. Stop SQL Server; verify liveness remains `200` and readiness returns `503`; restart SQL Server and verify readiness returns to `200`.
8. Stop and restart the stack without removing the named volume; verify persisted data.
9. Record Passed/Failed/Blocked/Not run with commands, observed output, and limitations. A missing Docker daemon or image is Blocked, not Passed.

## Rollback or forward recovery

N/A for production data — tests use isolated Testcontainers databases and local Compose volumes. Cleanup drops test databases/containers; `docker compose down -v` is destructive and documented. No test has persistent effects outside the isolated local environment.

## Phase 7: Part scope verification

| Change / scenario | Requirement IDs | Direct requirement or necessary dependency | Verification |
| --- | --- | --- | --- |
| Unit test project and validator tests | S10, AC-2, AC-3 | Required test foundation and startup validation | TC-01, TC-02, TC-16. |
| Integration project and SQL fixture | S10, AC-2, AC-4, AC-5 | Required real-dependency verification | TC-06-TC-13, TC-16. |
| WebApplicationFactory startup tests | S10, AC-3, AC-7 | Required API/config coverage | TC-01, TC-02, TC-14, TC-15. |
| Health/outage tests | S9, AC-6, T-14 | Required liveness/readiness evidence | TC-03-TC-05. |
| Migration/seed/constraint tests | S5-S7, AC-4, AC-5 | Required schema and seed evidence | TC-06-TC-08, TC-12. |
| Runtime permission tests | S6, AC-5 | Required restricted-identity evidence | TC-09-TC-11, TC-13. |
| OpenAPI/Swagger tests | S9, AC-7 | Required API client evidence | TC-14, TC-15. |
| Format/test commands | S4, S10, AC-2, AC-3 | Required local checks | TC-16, TC-17. |
| Compose/clean-checkout checks | S8, AC-1, AC-8, T-16 | Required reproducibility | TC-18, TC-19. |
| Booking concurrency, Redis, telemetry | Excluded | Not ticket 1 | No claims; tickets 2-4. |

## Handoffs

| Finding / decision | Target | Required action |
| --- | --- | --- |
| Tests depend on proposed table/identity/seed names. | Part 2 | Align the actual migration, principal script, and seed IDs with the test fixtures; update tests and docs together. |
| Health outage test requires startup validation not to connect. | Part 1 | Keep connection reachability in readiness, not startup. |
| OpenAPI/Swagger package and routes are selected by G-01/G-11/G-40. | Part 1 | Verify `/openapi/v1.json` and `/swagger/index.html` through TC-14/TC-15. |
| Booking procedure and concurrency evidence are absent. | Ticket 3 | Add protected-write and concurrency tests; do not reuse ticket-1 results as booking proof. |
| Redis cache evidence is absent. | Ticket 2 | Add hit/miss/expiry/outage tests. |
