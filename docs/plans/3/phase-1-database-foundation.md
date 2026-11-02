---
title: "3 - Phase 1: Database Foundation and Booking Procedure"
tags: [plans, service-scheduler, booking, sql, migrations, time-zone]
ticket: 3
phase: 1
status: planned
---

# Phase 1: Database foundation and booking procedure

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-02, S3-03, S3-06; AC3-03, AC3-05, AC3-13 migration portions; migration/schema prerequisites for T-04 through T-09 and S3-V05/S3-V06; project A-02, A-03, P-01.
- Intended outcome: persist the IANA-to-Windows time-zone mapping, add the dealership mapping FK, and create the same-owner `dbo.CreateAppointment` procedure in one ticket 3 EF migration, with the model snapshot updated. Migration application, ticket 2 upgrade, and down migration are verified against real SQL Server.
- Exclusions: no runtime `EXECUTE` grant, provisioner allowlist change, or setup parity validation (Phase 2); no API, DTO, controller, or booking orchestration (Phases 3-4); no direct runtime procedure tests (Phase 2); no `Appointments` column or index change; no cache or telemetry change.

## Dependencies

- Research verification passed on 2026-11-01; the research fingerprints remain current for this phase.
- Ticket 2 baseline is implemented: `20261031092016_InitialCreate`, appointment entity/checks/FKs/indexes, synthetic seeds, `SchedulerDbContext`, and real SQL Server/Redis test fixtures.
- No previous ticket 3 phase.
- The SQL Server 2022 CU23 Testcontainers fixture is available; the configured `sqlserver` MCP server is not required.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-02, S3-06 | `src/ServiceScheduler.Api/Infrastructure/Persistence/Entities/TimeZoneMapping.cs`, `Configurations/TimeZoneMappingConfiguration.cs`, `Configurations/DealershipConfiguration.cs`, `Configurations/SeedData.cs`, `SchedulerDbContext.cs` | Add a `TimeZoneMapping` entity with `IanaId` (`nvarchar(100)`) as primary key and a required unique `WindowsId` (`nvarchar(100)`); seed `Asia/Ho_Chi_Minh → SE Asia Standard Time` and `Europe/London → GMT Standard Time`; add `FK_Dealerships_TimeZoneMappings_TimeZoneId` from `Dealerships.TimeZoneId` with `ON DELETE NO ACTION`; expose `SchedulerDbContext.TimeZoneMappings`. | `dotnet build --no-restore` succeeds; the model maps the table, unique `WindowsId` index, and dealership FK; the two seed rows match the seeded dealership IANA IDs; no `Appointments` model change appears. | Planned |
| 2 | S3-02, S3-03, S3-06 | New `Migrations/<timestamp>_AddBookingProcedureAndTimeZoneMappings.cs` plus designer and updated `SchedulerDbContextModelSnapshot.cs` | After `dotnet tool restore`, generate the migration with `dotnet ef migrations add AddBookingProcedureAndTimeZoneMappings --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj`; inspect and, if necessary, reorder operations so the order is create `TimeZoneMappings` → insert the two mapping rows → add the dealership FK → create `dbo.CreateAppointment`. Use `migrationBuilder.Sql` for the procedure. The down migration drops the procedure, then the FK, then the table. | `dotnet ef migrations script` shows the required order and the procedure SQL; the updated model snapshot contains the table, unique `WindowsId` index, and dealership FK; no `Appointments` column/index operation is generated; the actual migration ID is recorded in [[database/migration|Migrations]] during implementation. | Planned |
| 3 | S3-02, S3-03 | New migration procedure SQL | Implement `dbo.CreateAppointment` exactly as the research contract: inputs `@DealershipId`, `@CustomerId`, `@VehicleId`, `@ServiceTypeId`, `@TechnicianId`, `@ServiceBayId`, `@StartUtc`, `@EvaluatedAtUtc`; outputs `@AppointmentId`, `@EndUtc`, `@DurationMinutes`, `@CreatedUtc`; errors `51001`-`51006`. Use `SET NOCOUNT ON`, `SET XACT_ABORT ON`, reject `@@TRANCOUNT > 0` with `51003`, normalize inputs to UTC, validate future start before the transaction, set `LOCK_TIMEOUT 5000` and explicit `SERIALIZABLE`, lock the technician then bay rows with `UPDLOCK, HOLDLOCK`, revalidate ownership/scope/duration/active/qualified/opening/shift coverage, check both half-open conflicts independently, insert one `Confirmed` row with `NEWID()`/`SYSUTCDATETIME`, and commit before returning. Roll back on any error and reset session settings on every exit, including early validation. | The procedure compiles and applies on SQL Server 2022 CU23; a valid direct call using the privileged setup connection inserts one row with matching duration/end/UTC offsets and returns the outputs; a deliberately failing call leaves no row and does not leak `XACT_ABORT`, `LOCK_TIMEOUT`, or isolation settings. | Planned |
| 4 | S3-06; S3-V06 precondition | `tests/ServiceScheduler.IntegrationTests/Persistence/MigrationAndSeedTests.cs`, new `tests/ServiceScheduler.IntegrationTests/Booking/BookingMigrationTests.cs` | Extend the migration/seed test to assert `TimeZoneMappings` rows, unique `WindowsId`, the dealership FK, and `dbo.CreateAppointment` in the standard setup database. Add migration tests that apply the ticket 3 migration to an empty database, apply it to a database at `20261031092016_InitialCreate` containing a synthetic appointment, and apply the down migration. Use `IMigrator`/`Database.MigrateAsync` with the setup identity; insert only synthetic probe rows and clean them up. | Fresh migration asserts the exact mapping rows, FK, unique index, and procedure; the ticket 2 upgrade preserves the synthetic appointment; the down migration removes the procedure/FK/table while preserving appointments; tests pass on real SQL Server. | Planned |
| 5 | S3-06; P-04 | `docs/test/3-test.md` | Create the ticket 3 test report from the backend and integration templates; record the phase 1 revision, environment, commands, results, and unexecuted checks. | Report exists with actual command/output evidence and explicit Not run/Blocked rows for later phases. | Planned |

## Procedure contract summary

The final migration SQL is authoritative; the research body is the reviewed contract. The signature and error mapping are fixed for Phases 2-5.

| Parameter | Direction | Type | Meaning |
| --- | --- | --- | --- |
| `@DealershipId` | Input | `uniqueidentifier` | Route dealership. |
| `@CustomerId` | Input | `uniqueidentifier` | Request customer; must exist. |
| `@VehicleId` | Input | `uniqueidentifier` | Request vehicle; must belong to `@CustomerId`. |
| `@ServiceTypeId` | Input | `uniqueidentifier` | Service type; must belong to `@DealershipId`. |
| `@TechnicianId` | Input | `uniqueidentifier` | Candidate technician; dealership-scoped, active, qualified. |
| `@ServiceBayId` | Input | `uniqueidentifier` | Candidate bay; dealership-scoped, active. |
| `@StartUtc` | Input | `datetimeoffset(7)` | Requested start; normalized to UTC inside the procedure. |
| `@EvaluatedAtUtc` | Input | `datetimeoffset(7)` | API-owned evaluation instant for the future-start check. |
| `@AppointmentId` | Output | `uniqueidentifier` | `NEWID()` value inserted. |
| `@EndUtc` | Output | `datetimeoffset(7)` | `DATEADD(minute, DurationMinutes, StartUtc)`. |
| `@DurationMinutes` | Output | `int` | SQL service-duration snapshot. |
| `@CreatedUtc` | Output | `datetimeoffset(7)` | `CAST(SYSUTCDATETIME() AS datetimeoffset(7))`. |

| Error | Meaning | Phase 2+ mapping |
| --- | --- | --- |
| `51001` | Technician conflict | Candidate retry; `409` when exhausted. |
| `51002` | Bay conflict | Candidate retry; `409` when exhausted. |
| `51003` | Invalid request, ownership/scope, invalid duration/interval, or caller-owned transaction | Direct callers receive `51003`; the API path maps it to `400 validation_error` because the API never owns a transaction. |
| `51004` | Resource not eligible (missing/inactive/unqualified) | Pair retry; `409` when exhausted. |
| `51005` | Opening/shift coverage unavailable or interval spans local dates | Pair retry; `409` when exhausted. |
| `51006` | Missing/invalid SQL time-zone mapping | Sanitized `500 internal_error`; no retry. |

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build baseline | `dotnet restore` then `dotnet build --no-restore` from the repository root. | Restore and build succeed with no new warnings. | Not run |
| Migration script (S3-03, S3-06) | `dotnet tool restore` then `dotnet ef migrations script --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj` | Script creates `TimeZoneMappings`, inserts the two rows, adds the dealership FK, and creates `dbo.CreateAppointment` in that order; no `Appointments` column/index change. | Not run |
| Fresh migration (S3-02, S3-06) | Apply migrations to an empty real SQL Server database with the setup identity; inspect `sys.tables`, `sys.indexes`, `sys.foreign_keys`, `sys.objects`, and mapping rows. | Mapping table, unique `WindowsId` index, dealership FK, and procedure exist; mapping rows are exactly the two supported pairs; appointment schema is unchanged. | Not run |
| Ticket 2 upgrade (S3-V06, AC3-13) | Create a database at `20261031092016_InitialCreate`, insert one synthetic appointment, apply the ticket 3 migration, and re-inspect. | Objects are added; the synthetic appointment and its associations remain; no appointment rewrite occurs. | Not run |
| Down migration (S3-V06) | Apply the ticket 3 down migration (or `dotnet ef database update 20261031092016_InitialCreate`). | Procedure, dealership FK, and mapping table are gone; existing appointments remain. | Not run |
| Procedure smoke (S3-03) | Direct call through the privileged setup connection with seeded Saigon IDs, a future start inside opening/shift coverage, and an explicit `@EvaluatedAtUtc`. | One `Confirmed` appointment is inserted; outputs match the stored ID/end/duration/created values; UTC offsets are zero; a failing call leaves no row. | Not run |
| Affected tests (S3-06) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingMigration"` and `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~MigrationAndSeed"`. | Migration/seed/procedure checks pass; no unrelated test is weakened. | Not run |
| Format | `dotnet format --verify-no-changes`. | No formatting changes are required. | Not run |

## Exit criteria

- The ticket 3 migration applies to an empty database and to a ticket 2 database containing a synthetic appointment.
- `TimeZoneMappings`, its unique `WindowsId` index, the dealership FK, and `dbo.CreateAppointment` exist with the documented contract; no `Appointments` column or index change is introduced.
- The procedure compiles, inserts one valid appointment through the privileged setup connection, and leaves no partial row on failure.
- The down migration removes the new procedure/FK/table and preserves existing appointments.
- `docs/test/3-test.md` records Phase 1 commands, results, environment, and remaining checks.
- Runtime grants, API behavior, concurrency, and setup idempotence are explicitly not claimed by this phase.

## Impacts and remaining decisions

- Database/migrations: yes; one additive migration for the mapping table/seed/FK and the booking procedure. No appointment backfill, rewrite, or index addition.
- Cache: none; booking never reads Redis and no cache key changes.
- Runtime permissions: deferred to Phase 2; the procedure exists but is not executable by `scheduler_app` yet.
- Configuration: none in this phase; booking options are introduced in Phase 3.
- Deployment: the migration ID, fresh/upgrade/down behavior, and data-preservation limits are recorded in the test report and carried into the deploy notes in Phase 2.
- Remaining decisions: none blocking. The exact generated migration filename is intentionally deferred to implementation and must be recorded; if EF emits the dealership FK before the mapping seed, reorder the operations or add the FK through migration SQL after the seed.
