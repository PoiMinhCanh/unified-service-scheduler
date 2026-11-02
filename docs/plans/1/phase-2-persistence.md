---
title: "1 - Phase 2: Persistence Model and Initial Migration"
tags: [plans, service-scheduler, foundation, database, ef-core]
ticket: 1
status: implemented
---

# 1 - Phase 2: Persistence model and initial migration

## Requirements and scope

- Ticket: [[1 - Init Project]]
- Research: [[research/1/00-overview|Foundation research overview]] and [[research/1/02-persistence-and-identity|Persistence and identity research]].
- Schema contract: [[database/schema|Schema reference]] owned by G-02, G-03, G-04, G-05, G-29, and G-33.
- Requirement IDs: S5, S7 static-seed portion, S10 test-fixture portion; AC-4 migration portion; AC-5 constraint portion; supports T-16.
- Intended outcome: the complete shared-domain EF Core model, deterministic mappings and constraints, an initial `InitialCreate` migration, and static synthetic seeds that apply to an empty real SQL Server.
- Exclusions: no runtime login/user or grants (Phase 3); no time-relative shifts (Phase 3); no booking procedure or `EXECUTE` grant (ticket 3); no business endpoints; no Redis.
- Assumptions: the Phase 1 host and empty `SchedulerDbContext` build; the G-01 EF Core `10.0.13` packages restore; the Phase 1 runtime connection validation remains satisfied while migrations use a separate design-time/setup connection.

## Dependencies

- Phase 1 solution, project references, central package versions, and `SchedulerDbContext` registration.
- Accepted decisions G-02, G-03, G-04, G-05, G-29, G-33, and G-34; physical names, types, keys, checks, and indexes follow [[database/schema|Schema reference]] exactly.
- Testcontainers `4.14.0` with the G-14 SQL Server image pin is required for the SQL integration tests in this phase.
- Phase 3 depends on the migration, static seeds, and SQL fixture created here.

## Implementation steps

| Step | Requirement IDs | Files / components (planned) | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 2.1 | S5 | `src/ServiceScheduler.Api/Infrastructure/Persistence/Entities/` | Add `Dealership`, `DealershipOpeningHour`, `Customer`, `Vehicle`, `ServiceType`, `Technician`, `TechnicianQualification`, `TechnicianShift`, `ServiceBay`, and `Appointment`; use `Guid` identifiers, required non-nullable members, `DateTimeOffset` UTC instants, and no generated values or SQL defaults. | Entities compile; no scheduling or booking behavior is placed in the entity classes. | Implemented |
| 2.2 | S5, AC-5 | `src/ServiceScheduler.Api/Infrastructure/Persistence/Configurations/` | Map every entity to `dbo` with the schema reference's exact table/column names, `uniqueidentifier`, `datetimeoffset(7)`, `time(0)`, `nvarchar` lengths, and `bit`/`tinyint`/`int` types; configure `ValueGeneratedNever`; add alternate unique keys `(Id, DealershipId)` and `(Id, CustomerId)`; add composite FKs with `DeleteBehavior.NoAction`; add the named checks and indexes, including the two appointment conflict indexes and shift index; remove redundant convention FK indexes. | `OnModelCreating` reproduces the schema contract; each check and index uses its deterministic name. | Implemented |
| 2.3 | S5, AC-4 | `Infrastructure/Persistence/SchedulerDbContext.cs`, `Infrastructure/Persistence/SchedulerDbContextFactory.cs` | Complete the DbContext with the ten `DbSet`s and configurations; add a design-time factory that obtains a setup connection from configuration when supplied and otherwise uses a non-secret local placeholder sufficient for model generation; keep startup validation independent of a live database. | `dotnet ef migrations add` can build the model without runtime secrets or a live database; the API still starts with an unreachable SQL server. | Implemented |
| 2.4 | S5, S7 static portion, AC-4 | `Infrastructure/Persistence/Configurations/` seed data and a shared seed constants type | Add fixed-GUID static seeds via migration `HasData` in FK-safe order: two dealerships using `Asia/Ho_Chi_Minh` and `Europe/London`, weekday opening intervals, customers and vehicles with valid uppercase VINs, positive-duration service types, active and inactive technicians, same-dealership qualifications, and active and inactive bays; add no appointments. | Seeds are deterministic, FK-valid, and contain no secrets or real personal data; tests can reference the constants. | Implemented |
| 2.5 | S5, AC-4 | `src/ServiceScheduler.Api/Migrations/<timestamp>_InitialCreate.cs`, `SchedulerDbContextModelSnapshot.cs` | Generate the initial migration after the model and static seeds are complete; inspect the generated SQL for every table, column, key, check, index, and `NO ACTION` FK; correct mappings rather than hand-editing generated SQL where possible; create no procedure. | Migration applies to an empty real SQL Server and produces the schema contract without extra indexes or constraints. | Implemented |
| 2.6 | S10, AC-4, AC-5 | `tests/ServiceScheduler.IntegrationTests/Fixtures/SqlServerContainerFixture.cs`, collection definition | Add a Testcontainers `MsSql` collection fixture using the pinned SQL Server 2022 Developer image, `ACCEPT_EULA=Y`, and a per-run synthetic `sa` password kept in memory; create one uniquely named database per test class; disable parallel execution for the SQL collection; apply the real migration path and expose the privileged setup connection. | The fixture starts real SQL Server once per collection and initializes each class database through the same migration path used in production setup. | Implemented |
| 2.7 | S5, S7, S10, AC-4, AC-5; TC-06, TC-07, TC-12 | `tests/ServiceScheduler.IntegrationTests/Persistence/MigrationAndSeedTests.cs`, `ConstraintTests.cs` | Assert `InitialCreate` history, expected tables/columns/types, primary and alternate keys, FK actions, named checks and indexes, and static seed counts/identities with no appointments; insert invalid rows with the setup identity for duplicate VIN, non-positive duration, invalid interval, duration/end mismatch, invalid status, cross-dealership appointment, customer/vehicle mismatch, cross-dealership qualification, invalid opening hours, and non-zero UTC offsets. | Each invalid insert fails with the expected constraint; no invalid row or schema change persists; repeat runs do not leak state. | Implemented |
| 2.8 | S5, AC-4, AC-5, AC-9 | `docs/database/schema.md`, `docs/database/overview.md`, `docs/database/migration.md`, `docs/architecture.md` | Replace planned physical placeholders with the implemented mapping and migration facts: actual migration ID/path, entity/configuration paths, generated-SQL review result, seed identifiers, and remaining ticket-3 procedure gap. | Database documents match the migration; planned versus implemented status is explicit. | Implemented |

## Verification

> [!NOTE]
> Implementation was completed on 2026-10-31 and the commands were exercised locally. Formal Passed/Failed/Blocked results are recorded by the test phase in [[test/1-test|the ticket 1 test report]]; the rows below remain the planning baseline.

| Requirement IDs | Scenario / command (planned) | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| S5, AC-4, TC-06 | Apply the initial migration to an empty real SQL Server through the fixture | All ten tables, keys, checks, indexes, and `InitialCreate` history exist; no booking procedure is created. | Not run |
| S5, AC-4 | Inspect `dotnet ef migrations script` output against [[database/schema|Schema reference]] | Names, types, nullability, FK actions, check expressions, index key/include order, and absence of redundant indexes match the contract. | Not run |
| S5, S7, TC-07 | Read static seeds after setup | Two dealerships with supported IANA zones, weekday opening hours, customers/vehicles, service types, technicians/qualifications, and bays exist with fixed identities; `Appointments` is empty. | Not run |
| S5, AC-5, TC-12 | Insert each invalid row listed in step 2.7 with the setup identity | Every attempt fails with the expected unique/check/FK error; final state contains no invalid row. | Not run |
| S5, AC-4 | Rerun migration and static seed application against the same database | Migration history is unchanged and static seeds are not duplicated. | Not run |
| S1, AC-3, T-14 | Retain the Phase 1 startup/outage checks after the model is completed | Startup still does not connect to SQL; liveness `200` and readiness `503` with a non-listening endpoint. | Not run |
| S4, AC-2, AC-3 | `dotnet build --no-restore`; unit and integration test projects | Build succeeds; new persistence tests execute; existing tests remain green. | Not run |
| S5, S11, AC-9 | Review database and architecture changes against the migration | Documents record implemented mappings and retain ticket-3 procedure as planned. | Not run |

## Impacts and remaining decisions

- Database / migrations: creates the full shared-domain schema, composite keys/FKs, named checks/indexes, and static seeds; no runtime identity, shifts, procedure, or `EXECUTE` grant yet.
- Cache: none; the seeded service-type shape becomes future ticket-2 cache input. No key or TTL is added.
- API / configuration: the DbContext now has a complete model but no business endpoints; API startup still validates configuration only.
- Deployment: the migration exists but no executable setup mode or Compose initialization path exists until Phases 3 and 4.
- Open decisions for implementation: design-time factory fallback connection details, exact fixed GUID values, whether EF generates any redundant FK indexes that must be removed, and whether the `DATEADD` duration-snapshot check applies cleanly to `datetimeoffset(7)` on the pinned SQL Server image.
- Handoff: Phase 3 consumes the migration, seed constants, and SQL fixture to provision the runtime identity and future shifts.
