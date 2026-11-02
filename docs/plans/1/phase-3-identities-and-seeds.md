---
title: "1 - Phase 3: Setup Mode, Runtime Identity, and Seeds"
tags: [plans, service-scheduler, foundation, database, security]
ticket: 1
status: implemented
---

# 1 - Phase 3: Setup mode, runtime identity, and seeds

## Requirements and scope

- Ticket: [[1 - Init Project]]
- Research: [[research/1/00-overview|Foundation research overview]] and [[research/1/02-persistence-and-identity|Persistence and identity research]]; testing strategy from [[research/1/03-testing-and-verification|Testing and verification research]].
- Requirement IDs: S6, S7; AC-4, AC-5; S10 fixture portion; T-14 readiness portion; supports T-16.
- Intended outcome: an explicit privileged `--db-setup` mode that applies migrations, provisions the restricted runtime identity and grants, and seeds time-relative future shifts idempotently; the API runs only with the runtime identity and never performs setup.
- Exclusions: no booking procedure or `EXECUTE` grant (ticket 3); no Redis; no telemetry; no administrative CRUD; no dedicated setup login beyond local `sa`.
- Assumptions: the Phase 2 migration and static seeds are complete; setup runs against an isolated local SQL Server or a Testcontainers database; `sa` is used only for setup/fixture initialization; the runtime password is supplied through local configuration and never committed.

## Dependencies

- Phase 2 `InitialCreate` migration, static seed constants, `SchedulerDbContext`, and `SqlServerContainerFixture`.
- Accepted decisions G-06, G-07, G-08, G-15, G-16, G-28, G-30, G-31, G-32, G-34, G-38, and G-39.
- The Phase 1 configuration/host must remain startable without setup credentials; the setup branch executes before web-host construction.
- The Phase 4 Compose `db-setup` service consumes this mode.

## Implementation steps

| Step | Requirement IDs | Files / components (planned) | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 3.1 | S6, S7 | `src/ServiceScheduler.Api/Program.cs`, `Infrastructure/Setup/DatabaseSetupRunner.cs` | Detect the exact `--db-setup` argument before `WebApplication.CreateBuilder`; run migration, principal provisioning, and shift seeding through the runner; return a nonzero exit code on failure; never execute setup in normal API mode. | The API starts without setup configuration; `--db-setup` exits without starting HTTP; setup failure is observable as a nonzero exit. | Implemented |
| 3.2 | S4, S6, AC-3 | `Infrastructure/Setup/SchedulerSetupOptions.cs`, setup-mode validation | Bind and validate `ConnectionStrings:Setup` and `Scheduler:RuntimePassword`; require a parseable privileged connection, a non-empty runtime password, and the runtime login name `scheduler_app`; keep setup options out of API-mode registration. | Setup-mode validation names missing or invalid setup keys; the API never binds `ConnectionStrings:Setup` or the runtime password. | Implemented |
| 3.3 | S6, AC-5 | `Infrastructure/Setup/RuntimePrincipalProvisioner.cs` and supporting SQL | Idempotently create the `scheduler_app` login in `master` and the same-named user in the application database; grant `CONNECT` and `GRANT SELECT ON SCHEMA::dbo` only; apply grants on rerun; validate existing login/user mappings and fail on unexpected ownership, privilege, or role state; do not rotate passwords on ordinary rerun and never print them. | First setup and reruns complete without duplicate principals; the runtime principal has only the selected grants; unexpected privileged state fails setup. | Implemented |
| 3.4 | S7, AC-4, T-16 | `Infrastructure/Setup/FutureShiftSeeder.cs` | Inject `TimeProvider`, capture the setup clock once, resolve each dealership's IANA zone, start at the next local calendar day, seed weekday 09:00-17:00 shifts within the following 28 local calendar days (end exclusive) for active technicians, convert each boundary to UTC, and add only missing intervals using the unique `(TechnicianId, StartUtc, EndUtc)` key; do not delete historical shifts. | Seeded shifts are deterministic for a fixed clock, valid and future-dated relative to that clock, and reruns do not duplicate them; unsupported zones fail clearly. | Implemented |
| 3.5 | S6, S7, AC-4 | `Infrastructure/Setup/DatabaseSetupRunner.cs`, `Program.cs` | Orchestrate migrations, principal provisioning, and shift seeding in order; apply the setup-mode 120-second cancellation deadline; do not use unbounded retries; keep partial-failure behavior idempotent so a corrected rerun can complete. | An empty database initializes end to end; a failed step exits nonzero; rerunning after correction completes without duplicate data. | Implemented |
| 3.6 | S10, AC-4, AC-5 | `tests/ServiceScheduler.IntegrationTests/Fixtures/SqlServerContainerFixture.cs`, `SchedulerApiFactory.cs` | Extend the fixture to invoke the same setup component used by `--db-setup`, create the shared runtime login in `master` and map it to a user in each class database, expose separate setup and runtime connections, and supply the runtime connection to the API factory; generate per-container credentials in memory and keep them out of logs. | Tests initialize real SQL Server through the production setup path; API integration tests use the restricted runtime identity and no setup credentials. | Implemented |
| 3.7 | S6, S7, S10, AC-4, AC-5; TC-05, TC-07, TC-08, TC-09, TC-10, TC-11, TC-13 | `Persistence/MigrationAndSeedTests.cs`, `Persistence/ConstraintTests.cs`, `Security/RuntimePermissionTests.cs`, `Health/HealthEndpointTests.cs` | Add or extend tests for static seeds, future shifts and idempotent reruns under `FakeTimeProvider` fixed at `2026-11-02T00:00:00Z`, runtime reads and `CURRENT_USER`, denied appointment `INSERT`/`UPDATE`/`DELETE`, denied `CREATE`/`ALTER`/`DROP`, restricted role membership, and readiness `200` after initialization. | Every permission test uses an independent runtime connection; final stored state is asserted; initialized readiness returns `200`; no test grants extra privileges. | Implemented |
| 3.8 | S11, AC-9 | `docs/setup-guide.md`, `docs/database/schema.md`, `docs/database/overview.md`, `docs/database/migration.md`, `docs/database/cache.md`, `docs/architecture.md` | Record the implemented setup command, required configuration names, identity names (not passwords), grant boundary, seed identifiers and shift horizon, rerun behavior, and no-cache-impact note. | Documentation matches the implemented setup path and keeps ticket-3 procedure work planned. | Implemented |

## Verification

> [!NOTE]
> Implementation was completed on 2026-10-31 and the commands were exercised locally. Formal Passed/Failed/Blocked results are recorded by the test phase in [[test/1-test|the ticket 1 test report]]; the rows below remain the planning baseline.

| Requirement IDs | Scenario / command (planned) | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| S6, S7, AC-4 | Run `--db-setup` against an empty real SQL Server database with setup configuration supplied | Setup exits `0`; migrations, runtime principal/grants, and shifts exist; no HTTP server starts. | Not run |
| S6, S7, AC-4, T-16 | Run the same setup a second time | Exit `0`; migration history unchanged; no duplicate users, grants, or shift intervals. | Not run |
| S6, AC-5, TC-09 | Open an independent runtime connection and read seeded data | Reads succeed; `CURRENT_USER` is `scheduler_app`. | Not run |
| S6, AC-5, TC-10 | Attempt runtime `INSERT`, `UPDATE`, and `DELETE` on `Appointments` | Each attempt fails with a permission error; no appointment row is added or changed. | Not run |
| S6, AC-5, TC-11 | Attempt runtime `CREATE TABLE`, `ALTER TABLE`, and `DROP` | Each attempt fails with a permission error; no schema object changes. | Not run |
| S6, AC-5, TC-13 | Inspect runtime role membership and permissions | Runtime is not `sysadmin`, `db_owner`, `db_datawriter`, `db_ddladmin`, or `db_securityadmin`; direct appointment write and DDL permissions are absent. | Not run |
| S7, AC-4, TC-07 | Read seeded static data after setup | Deterministic dealerships, opening hours, customers, vehicles, service types, technicians, qualifications, and bays exist; appointments are empty. | Not run |
| S7, AC-4, T-16, TC-08 | Inspect shifts with the fixed clock and rerun the seeder | Active technicians have non-overlapping weekday 09:00-17:00 shifts covering the following 28 local calendar days; rerun adds no duplicates. | Not run |
| S4, S6, AC-3 | Start API mode without `ConnectionStrings:Setup` or `Scheduler:RuntimePassword` | API starts normally with runtime configuration only; setup keys are not required or loaded. | Not run |
| S6, AC-4 | Present an existing runtime principal with unexpected privileged membership | Setup fails clearly rather than accepting or silently repairing the state. | Not run |
| S9, AC-6, T-14, TC-05 | Request `/health/ready` against the initialized database using the runtime identity | `200`; liveness remains independent. | Not run |
| S4, AC-2, AC-3 | `dotnet build --no-restore`; unit and integration test projects | Build succeeds; seed, identity, permission, and readiness tests execute; existing tests remain green. | Not run |

## Impacts and remaining decisions

- Database / migrations: no new schema beyond Phase 2; setup creates the runtime login/user and grants, and adds time-relative shifts to `TechnicianShifts`.
- Identity boundary: setup uses privileged local `sa`; the API and tests use `scheduler_app` with `CONNECT` and `SELECT ON SCHEMA::dbo` only. Ticket 3 adds only the booking-procedure `EXECUTE` grant.
- Cache: none; no Redis dependency is introduced.
- API / configuration: adds setup-only options and an executable mode; API-mode startup remains independent of setup credentials.
- Deployment: Compose will call this mode in Phase 4; deployment notes must cover rerun, database recreation, and password resupply without exposing secrets.
- Open decisions for implementation: exact parameterization strategy for principal SQL (validated identifiers and parameterized commands where the SQL grammar permits), seed horizon edge handling around DST/invalid local times, and the precise shape of the unexpected-privilege validation.
- Handoff: Phase 4 wraps this mode in Compose; the test phase records the executed permission and seed evidence.
