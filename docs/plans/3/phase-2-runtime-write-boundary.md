---
title: "3 - Phase 2: Runtime Write Boundary, Setup Validation, and Direct Procedure Verification"
tags: [plans, service-scheduler, booking, sql, permissions, setup]
ticket: 3
phase: 2
status: planned
---

# Phase 2: Runtime write boundary, setup validation, and direct procedure verification

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-02, S3-03, S3-06; AC3-03, AC3-05, AC3-13; T-04, T-05, T-06, T-08, T-09, and the direct-call portions of T-10/T-11; S3-V05, S3-V06; project P-01, P-04.
- Intended outcome: make the Phase 1 procedure the only runtime appointment write path, grant exactly the documented object `EXECUTE`, deny direct appointment DML, validate time-zone/procedure setup state, and prove the procedure's invariants with independent `scheduler_app` connections without API prechecks.
- Exclusions: no HTTP endpoint, DTO, controller, or booking orchestration (Phases 3-4); no API-level conflict/validation tests (Phase 4); no concurrency or outage checks (Phase 5); no appointment schema/index change.

## Dependencies

- Phase 1 complete: the ticket 3 migration, `TimeZoneMappings`, dealership FK, and `dbo.CreateAppointment` exist and pass fresh/upgrade/down migration checks.
- Research verification passed on 2026-11-01.
- Ticket 2 setup components are implemented: `DatabaseSetupRunner`, `RuntimePrincipalProvisioner`, `SchedulerSetupOptions`, `FutureShiftSeeder`, and the SQL Server fixture with setup/runtime connection strings.
- The runtime login `scheduler_app` is an enabled SQL login with no server roles; the runtime user is SID-matched with no database roles.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-03, S3-06 | `src/ServiceScheduler.Api/Infrastructure/Setup/RuntimePrincipalProvisioner.cs` | Extend the pre/post permission allowlist to accept exactly: database `CONNECT` `GRANT`; schema `dbo` `SELECT` `GRANT`; object `EXECUTE` `GRANT` on `OBJECT_ID('dbo.CreateAppointment', 'P')`; object `INSERT`/`UPDATE`/`DELETE` `DENY` on `OBJECT_ID('dbo.Appointments', 'U')`. Reject `WITH GRANT OPTION`, object read grants, schema `EXECUTE`/`ALTER`/`CONTROL`, other objects/schemas, ownership, and role membership. Grant with `GRANT CONNECT`, `GRANT SELECT ON SCHEMA::dbo`, `GRANT EXECUTE ON OBJECT::dbo.CreateAppointment`, and `DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments`. Fail with a clear message if the procedure or table object ID is missing. Keep the existing login, user, SID, server-role, and database-role checks. | Fresh setup grants only the exact rows; repeated setup is idempotent; an unexpected extra permission or role fails setup; a missing procedure fails with a clear, non-secret message; direct appointment DML remains denied. | Planned |
| 2 | S3-02, S3-06 | New `src/ServiceScheduler.Api/Infrastructure/Setup/TimeZoneMappingValidator.cs` | Validate with the setup connection: every dealership `TimeZoneId` has a `TimeZoneMappings` row; `TimeZoneInfo.FindSystemTimeZoneById(IanaId)` resolves; SQL Server accepts each `WindowsId` with `AT TIME ZONE`; .NET and SQL local conversions agree for the Saigon fixture and the London spring-forward/fall-back fixtures; `dbo.CreateAppointment` exists. Messages name the IANA/Windows IDs and configuration dependency, never credentials. | Seeded mappings pass; an unmapped/invalid IANA or Windows ID fails setup clearly; a missing procedure fails setup; the validator performs no writes to appointments. | Planned |
| 3 | S3-06 | `src/ServiceScheduler.Api/Infrastructure/Setup/DatabaseSetupRunner.cs` | Invoke migrations, then `TimeZoneMappingValidator`, then `RuntimePrincipalProvisioner`, then `FutureShiftSeeder`. Preserve repeated-setup idempotence and the existing setup deadline. | Fresh, repeated, and ticket 2-upgrade setup complete; a validator failure occurs before grants; the shift seeder remains last and does not duplicate shifts. | Planned |
| 4 | S3-02-S3-04; S3-V05 | New `tests/ServiceScheduler.IntegrationTests/Fixtures/BookingProcedureInvoker.cs`; `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulingTestData.cs` | Add a runtime-connection helper that executes `dbo.CreateAppointment` with typed input/output parameters, a configurable command timeout, optional cancellation, and an optional caller-owned transaction for the `51003` check; return outputs or surface the `SqlException`. Add setup-identity probe helpers for `UpdateServiceTypeDurationAsync`, `SetTechnicianActiveAsync`, `SetBayActiveAsync`, and a controlled `UpdateTimeZoneMappingWindowsIdAsync`; reuse the existing appointment insert/count/delete helpers. | Helpers compile and are used by the Phase 2 tests; no helper grants permissions or bypasses the runtime procedure for a booking assertion. | Planned |
| 5 | S3-03, S3-06; T-08 | `tests/ServiceScheduler.IntegrationTests/Security/RuntimePermissionTests.cs` | Extend the existing permission tests to assert the exact `sys.database_permissions` rows for `EXECUTE` and the three DML denies, no schema `EXECUTE`, no direct write grants, and no role membership. Execute a valid procedure call as `scheduler_app` and assert the row; assert direct `INSERT`, `UPDATE`, and `DELETE` on `dbo.Appointments` fail with permission errors and change no row. | Exact permission rows pass; procedure execution succeeds; direct DML is denied; unexpected privileges/roles fail setup; existing read/schema-denial tests remain green. | Planned |
| 6 | S3-02-S3-04; T-04-T-06, T-09, T-10, T-11 direct, S3-V05 | New `tests/ServiceScheduler.IntegrationTests/Booking/BookingProcedureTests.cs` | Direct runtime calls without API prechecks: valid insert and outputs; technician-only `51001`; bay-only `51002`; adjacent/partial/containment/identical intervals; past start and caller-owned transaction `51003`; ownership mismatch and cross-dealership service/resource `51003`/`51004`; inactive/unqualified resource `51004`; partial shift/opening and local-date-span `51005`; end-exactly-at-closing/shift end allowed; explicit `+07:00` and `Z` equivalent instants; London spring-forward/fall-back coverage; controlled invalid mapping `51006`. Assert row counts before/after every failure and issue a later valid call to prove locks were released. | All direct-call cases return the documented error number or success, persist the correct UTC start/end/duration/status/created values, and leave no partial row; a subsequent valid call succeeds after each failure. | Planned |
| 7 | S3-03, S3-06; S3-V06, AC3-13 | New `tests/ServiceScheduler.IntegrationTests/Booking/BookingSetupTests.cs` and `Booking/BookingMigrationUpgradeTests.cs` | Cover fresh setup, repeated setup, excess privilege rejection, missing procedure, invalid mapping, a resolvable-but-DST-inconsistent Windows mapping that must fail parity, ticket 2 upgrade with a synthetic appointment, down migration, and runtime-permission recovery. Use `SqlServerContainerFixture.CreateDatabaseAsync`/`RerunSetupAsync`, `IMigrator` for the ticket 2 target, the setup identity for migrations/probes, and the runtime identity for procedure/DML assertions. | Fresh/repeated/upgrade setup applies the procedure and exact permissions; the synthetic appointment survives; down migration removes the new objects; remaining appointment DML denies are identified and recovered per the documented path; S3-V06 passes. | Planned |
| 8 | S3-06; P-04 | `docs/test/3-test.md`, `docs/deploy-notes/3-deploy-notes.md` | Extend the test report with Phase 2 commands/results and start the deployment notes with migration order, grants/denies, repeated setup, ticket 2 upgrade, rollback/revoke, and recovery steps. | Report and deploy notes contain actual commands, observed results, and explicit unexecuted/manual checks; no acceptance criterion is marked Passed from setup alone. | Planned |

## Permission model

The runtime principal is limited to the following explicit permissions. Any other explicit row or role is a setup failure.

| Class | Permission | State | Object |
| --- | --- | --- | --- |
| `DATABASE` | `CONNECT` | `GRANT` | database |
| `SCHEMA` | `SELECT` | `GRANT` | `dbo` schema ID |
| `OBJECT_OR_COLUMN` | `EXECUTE` | `GRANT` | `OBJECT_ID('dbo.CreateAppointment', 'P')` |
| `OBJECT_OR_COLUMN` | `INSERT` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |
| `OBJECT_OR_COLUMN` | `UPDATE` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |
| `OBJECT_OR_COLUMN` | `DELETE` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |

Ownership chaining is a required assumption: the procedure and referenced tables are dbo-owned, the procedure is static and parameterized, and the runtime user receives only object `EXECUTE`. Changing ownership or crossing a broken ownership chain invalidates the direct-write denial and must fail the tests.

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet build --no-restore` and `dotnet format --verify-no-changes`. | Build succeeds; formatting is clean. | Not run |
| Fresh setup (S3-V06) | Empty isolated database; run `--db-setup`; inspect `sys.database_permissions`, `sys.database_role_members`, `sys.objects`, and a valid procedure call. | Migration/validator/provisioner/seeder succeed; exact permission rows; procedure executes; direct DML denied; no roles. | Not run |
| Repeated setup (S3-V06) | Run `--db-setup` twice; inspect permission rows and shifts. | Second run succeeds; one set of permissions; shifts are not duplicated; procedure still executes. | Not run |
| Excess privilege (S3-V06) | Add an unexpected object grant or role to `scheduler_app`; rerun setup; clean up. | Setup fails with a clear message; no silent acceptance. | Not run |
| Missing procedure / invalid or inconsistent mapping (S3-V06) | Drop the procedure, set a mapping `WindowsId` to an invalid value, or set a resolvable-but-DST-inconsistent Windows ID; rerun setup; restore via migration SQL or a controlled probe. | Setup fails clearly, including the .NET/SQL parity check; the failure is recoverable without deleting migration history; no appointment write occurs. | Not run |
| Ticket 2 upgrade (S3-V06, AC3-13) | Create a database at `20261031092016_InitialCreate`, insert a synthetic appointment, apply the ticket 3 migration and full setup; inspect and book. | Appointment preserved; mapping/FK/procedure added; exact grants/denies; valid booking succeeds; direct DML denied. | Not run |
| Down migration/recovery (S3-V06) | Apply the down migration; inspect procedure/FK/table/appointments and remaining runtime permissions; revoke remaining object permissions; rerun the ticket 2-style allowlist check. | New objects are gone; appointments remain; remaining appointment DML denies are identified and revoked; recovery is documented. | Not run |
| Permission tests (T-08) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~RuntimePermission"` | Exact rows, procedure execution, and denied direct DML pass. | Not run |
| Direct procedure matrix (T-04-T-06, T-09, S3-V05) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingProcedure"` | `51001`-`51006`, boundaries, DST, atomicity, and lock-release cases pass against real SQL Server with the runtime identity. | Not run |
| Setup tests (S3-V06) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingSetup"` and `~BookingMigrationUpgrade` | Fresh/repeated/upgrade/excess-privilege/recovery cases pass. | Not run |

## Exit criteria

- The runtime principal has exactly the documented `CONNECT`, schema `SELECT`, procedure `EXECUTE`, and appointment DML `DENY` rows, with no roles or DDL.
- Direct `INSERT`/`UPDATE`/`DELETE` on `dbo.Appointments` as `scheduler_app` is denied; a valid procedure execution succeeds and persists a correct row.
- Direct runtime procedure calls without API prechecks enforce ownership, scope, duration, active/qualified eligibility, opening/shift coverage, future start, and both half-open conflicts; failures leave no row and release locks.
- Setup validation rejects missing/invalid mappings and a missing procedure; fresh, repeated, ticket 2-upgrade, and down-migration recovery checks are recorded.
- `docs/test/3-test.md` contains Phase 2 commands/results; `docs/deploy-notes/3-deploy-notes.md` records the operational actions.
- No HTTP booking behavior or end-to-end API success is claimed by this phase.

## Impacts and remaining decisions

- Database/migrations: no additional schema objects beyond Phase 1; the migration and procedure are now operable by the runtime identity.
- Permissions: the protected write boundary is established; migrations/seeds remain on the privileged setup identity.
- Cache: none; booking is not yet exposed and does not read Redis.
- Configuration: none in this phase; booking options arrive in Phase 3.
- Deployment: migration order, grants/denies, repeated setup, upgrade, rollback, and recovery are recorded in the ticket 3 deploy notes.
- Remaining decisions: no blocking decision. The exact recovery behavior for permissions on a down-dropped procedure must be observed and recorded rather than assumed; the surviving appointment DML denies must be handled before a ticket 2 setup version runs.
- SQL Server may leave a default `EXECUTE` grant to `public` on a newly created procedure. The permission tests assert the explicit `scheduler_app` rows; implementation should inspect role/public-derived execution and document the observed state. Such a default does not grant direct appointment table writes and must not be "fixed" by adding schema `EXECUTE` or a write grant.
