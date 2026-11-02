---
title: "1 - Init Project: Persistence and Identity Research"
tags: [research, service-scheduler, database, ef-core, security]
ticket: 1
status: verified
updated: 2026-10-31
---

# 1 - Init Project: Persistence and Identity Research

- Ticket: [[1 - Init Project]]
- Parent research: [[research/1/00-overview|Foundation research overview]]
- Part: 2 of 3
- Requirement coverage: S5-S7; AC-4, AC-5; T-16; foundation portions of D-04 and P-04; architecture AS-01-AS-11 and BR-01-BR-09 where relevant.
- Evidence baseline: research baseline revision `25be1577c895204e58bc74b4f80336486d33bb9c`; rechecked at current revision `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` (2026-10-31); [[database/schema]] and [[database/overview]] remain planned models; no entities, migrations, SQL scripts, or seeds exist.

> [!IMPORTANT]
> This part proposes the physical model, migration, identities, and seeds. It does not claim that any table, login, grant, or seed has been created or verified.

## Scope of this part

Cover the shared-domain EF Core model, keys/checks/relationships/indexes, the initial migration, separate setup and runtime SQL identities, synthetic seeds including future shifts, the migration record, and cache impact.

The booking procedure and its `EXECUTE` grant are explicitly outside ticket 1; ticket 3 owns them. Redis and service-catalog caching are outside ticket 1; ticket 2 owns them.

## Current architecture and files

| Component | Actual inspected evidence | Relevant behavior / gap |
| --- | --- | --- |
| Schema reference | [[database/schema]] records the selected G-02 physical contract (table/column names, types, keys, checks, indexes, FK actions) and states mappings/generated SQL/real SQL Server verification remain pending (`docs/database/schema.md:9-63`). | No EF entities, configurations, migrations, or model snapshot exist. |
| Database overview | [[database/overview]] defines relationships and booking integrity rules and records the G-04 `dbo.DealershipOpeningHours` selection (`docs/database/overview.md:9-44`). | No implemented schema; proposed mappings must follow the selected contract. |
| Migration guidance | [[database/migration]] requires EF Core migrations, procedure/grant SQL, generated-SQL review, and real SQL Server verification (`docs/database/migration.md:9-29`). | No migration record exists. |
| Architecture domain model | `docs/architecture.md:165-211` defines entities, UUID/UTC conventions, assumptions, and business rules. | Ticket 1 implements schema only; scheduling rules and booking procedure remain future work. |
| Runtime boundary | `docs/architecture.md:149-153` requires a restricted runtime principal and separate privileged setup identity. | No login, user, role, grant, or permission test exists. |
| Cache reference | [[database/cache]] is a planned Redis inventory; ticket 1 excludes Redis (`docs/tickets/1 - Init Project.md:36`). | Record no cache change now; service-type seed shape is future input to ticket 2. |
| Application code | No `src/`, entity classes, DbContext, migrations, or SQL scripts exist. | All persistence components are proposed additions. |

## Phase 1: Layer coverage sweep

| Component | What to inspect | Status / evidence |
| --- | --- | --- |
| Domain model and EF mappings | Entities, SQL types, required relationships, snapshots, cross-table consistency | Missing — proposed addition in this part. |
| EF Core migrations and seeds | Model snapshot, migration SQL, seed lifecycle, idempotency | Missing — proposed addition. |
| Runtime permissions | Restricted runtime identity, procedure execution, denied direct appointment writes, separate setup identity | Missing — proposed addition; `EXECUTE` grant deferred to ticket 3. |
| Booking procedure and transactions | Technician-then-bay locking, full revalidation, conflict checks, atomic commit/rollback | N/A — ticket 1 excludes the procedure; schema must support ticket 3. |
| Failure and retry handling | Capacity versus dependency errors, deadlocks, timeouts, unknown commits | N/A for ticket 1 booking; setup failures and readiness are covered. |
| API contract and validation | Dealership scope, ownership, timestamp offsets, DTOs | N/A for business endpoints; schema constraints support future validation. |
| Scheduling rules | Duration, qualification, shifts, opening hours, interval boundaries | N/A — ticket 2/3; ticket 1 stores the required data. |
| Resource selection and availability | Authoritative bounded SQL queries, stable order | N/A — ticket 2. |
| Redis catalog cache | Key, payload, TTL, invalidation, fallback | N/A — ticket 2; no cache impact in ticket 1. |
| Configuration and packaging | Setup/runtime connection strings, initialization step | Proposed in this part; packaging details in Part 1. |
| Telemetry | Structured logs, metrics, traces | N/A — ticket 4. |
| Unit tests | Pure rules, validators | Part 3; ticket 1 persistence tests are integration-level. |
| API and dependency integration tests | Real SQL Server, runtime identity, constraints, seeds | Part 3. |
| Operational verification | Migration/seed/restart checks | Local checks proposed. |
| Documentation and delivery | Schema, overview, migration record, cache impact | Existing planned documents must change. |
| UI or new integrations | Custom UI or external integration | N/A — backend-only ticket. |

## Phase 2: Proposed data model and data flow

### 2.1 Physical model proposal

G-02 is resolved for planning on 2026-10-31: adopt the physical contract in [[database/schema|Schema reference]] for S5/AC-5. That reference now owns exact table/column names, types, lengths, nullability, defaults, key generation, FK actions, and deterministic constraint/index names. All columns are required, IDs are supplied with `ValueGeneratedNever`, and no SQL defaults are selected. Every FK uses delete/update `NO ACTION`. This is a design decision, not an existing schema; generated migration SQL and real SQL Server verification remain pending.

The table below summarizes the model. For index inventory, use the schema reference: it omits redundant opening-hours/service-type/bay indexes covered by leading key columns, and uses `(ServiceTypeId, DealershipId)` for qualification service lookup. Do not create additional indexes from this summary when the schema reference says a key already covers the lookup.

| Entity | Proposed table | Key columns and rules | Relationships and indexes |
| --- | --- | --- | --- |
| Dealership | `dbo.Dealerships` | `Id uniqueidentifier` PK; `Name nvarchar(200) NOT NULL`; `TimeZoneId nvarchar(100) NOT NULL` (IANA). | Unique `Name`; no cascade delete. |
| DealershipOpeningHours | `dbo.DealershipOpeningHours` | `DealershipId uniqueidentifier`; `DayOfWeek tinyint` 0-6; `OpensAt time(0)`; `ClosesAt time(0)`; PK `(DealershipId, DayOfWeek, OpensAt)`; check `ClosesAt > OpensAt`; check `DayOfWeek BETWEEN 0 AND 6`. | FK `DealershipId` -> `Dealerships`; index `(DealershipId, DayOfWeek)`. Multiple intervals per weekday are representable; seed data uses one daytime interval. |
| Customer | `dbo.Customers` | `Id uniqueidentifier` PK; `Name nvarchar(200) NOT NULL`; `Email nvarchar(256) NOT NULL` (synthetic contact). | No cascade delete; email uniqueness is not required for the initial demo. |
| Vehicle | `dbo.Vehicles` | `Id uniqueidentifier` PK; `CustomerId uniqueidentifier`; `Vin nvarchar(17) NOT NULL`; `Make nvarchar(100)`; `Model nvarchar(100)`. | FK `CustomerId` -> `Customers`; unique `Vin`; alternate unique key `(Id, CustomerId)` for ownership-enforcing composite FK; index `CustomerId`. |
| ServiceType | `dbo.ServiceTypes` | `Id uniqueidentifier` PK; `DealershipId uniqueidentifier`; `Name nvarchar(200)`; `DurationMinutes int`; check `DurationMinutes > 0`. | FK `DealershipId` -> `Dealerships`; unique `(DealershipId, Name)`; alternate unique key `(Id, DealershipId)`; index `DealershipId`. |
| Technician | `dbo.Technicians` | `Id uniqueidentifier` PK; `DealershipId uniqueidentifier`; `Name nvarchar(200)`; `IsActive bit NOT NULL`. | FK `DealershipId` -> `Dealerships`; alternate unique key `(Id, DealershipId)`; index `DealershipId`. |
| TechnicianQualification | `dbo.TechnicianQualifications` | `TechnicianId uniqueidentifier`; `ServiceTypeId uniqueidentifier`; `DealershipId uniqueidentifier`; PK `(TechnicianId, ServiceTypeId)`. | Composite FKs `(TechnicianId, DealershipId)` -> `Technicians(Id, DealershipId)` and `(ServiceTypeId, DealershipId)` -> `ServiceTypes(Id, DealershipId)`; index `ServiceTypeId`; this enforces same-dealership qualification. |
| TechnicianShift | `dbo.TechnicianShifts` | `Id uniqueidentifier` PK; `TechnicianId uniqueidentifier`; `StartUtc datetimeoffset(7)`; `EndUtc datetimeoffset(7)`; check `EndUtc > StartUtc`; check UTC offsets zero. | FK `TechnicianId` -> `Technicians`; unique `(TechnicianId, StartUtc, EndUtc)` for idempotent seeding; index `(TechnicianId, StartUtc) INCLUDE (EndUtc)`. |
| ServiceBay | `dbo.ServiceBays` | `Id uniqueidentifier` PK; `DealershipId uniqueidentifier`; `Name nvarchar(200)`; `IsActive bit NOT NULL`. | FK `DealershipId` -> `Dealerships`; unique `(DealershipId, Name)`; alternate unique key `(Id, DealershipId)`; index `DealershipId`. |
| Appointment | `dbo.Appointments` | `Id uniqueidentifier` PK; `DealershipId`, `CustomerId`, `VehicleId`, `ServiceTypeId`, `TechnicianId`, `ServiceBayId`; `StartUtc datetimeoffset(7)`; `EndUtc datetimeoffset(7)`; `DurationMinutes int`; `Status nvarchar(20)`; `CreatedUtc datetimeoffset(7)`; checks for positive duration, `EndUtc > StartUtc`, interval/duration equality, allowed status, and zero UTC offsets. | FKs to `Dealerships`, `Customers`; composite FKs `(VehicleId, CustomerId)` -> `Vehicles(Id, CustomerId)`, `(ServiceTypeId, DealershipId)` -> `ServiceTypes(Id, DealershipId)`, `(TechnicianId, DealershipId)` -> `Technicians(Id, DealershipId)`, `(ServiceBayId, DealershipId)` -> `ServiceBays(Id, DealershipId)`; conflict indexes `(DealershipId, TechnicianId, StartUtc) INCLUDE (EndUtc, Status)` and `(DealershipId, ServiceBayId, StartUtc) INCLUDE (EndUtc, Status)`. |

### 2.2 Key design rationale

- **UUIDs and UTC:** Use `Guid` primary keys mapped to `uniqueidentifier` and `DateTimeOffset` mapped to `datetimeoffset(7)`. Configure `ValueGeneratedNever` for seeded/application-supplied IDs; seeds use fixed GUIDs. Store only UTC instants with offset zero, enforced by checks and by converting application values with `ToUniversalTime()`.
- **Cross-table integrity:** Individual entity FKs alone do not prove that an appointment's service type, technician, and bay belong to its dealership, or that its vehicle belongs to its customer. Alternate unique keys on `(Id, DealershipId)` and `(Id, CustomerId)` plus composite FKs make those relationships enforceable in SQL Server. This is a proposed persistence-level defense in addition to future application and procedure validation.
- **Qualifications:** Add `DealershipId` to `TechnicianQualifications` so composite FKs can enforce that both the technician and service type belong to the same dealership. The schema reference now records the three-column table; the migration and mapping must implement it.
- **Opening hours:** Store local weekday intervals as `time(0)` values, not UTC, because opening hours are local wall-clock rules. Ticket 2 converts them to UTC using the dealership's IANA time zone. `OpensAt`/`ClosesAt` on the same weekday exclude overnight intervals, consistent with AS-06's initial scope.
- **Appointment snapshot:** Copy `DurationMinutes` and compute `EndUtc` from `StartUtc`; the check `EndUtc = DATEADD(minute, DurationMinutes, StartUtc)` preserves the snapshot relationship. Later service-type duration changes cannot alter stored appointments.
- **Status:** Use a bounded string with a check for the initial `Confirmed` state. Future states require a migration and a documented status contract; do not invent additional states now.
- **FK actions:** Use restrict/no-action behavior to preserve appointment and catalog history. Cascading deletes are not assumed. Seed ordering must insert referenced rows first.
- **Indexes:** The two appointment indexes support bounded technician/bay conflict queries; the shift index supports eligibility queries. Use the exact names, key order, included columns, and unfiltered index inventory selected in [[database/schema]]; compare the generated migration against that contract.

### 2.3 Proposed check constraints

Selected SQL Server expressions to include in the migration, with exact names defined in [[database/schema]] (generated SQL and execution verification pending):

| Table | Proposed check | Purpose |
| --- | --- | --- |
| DealershipOpeningHours | `ClosesAt > OpensAt`; `DayOfWeek BETWEEN 0 AND 6` | Valid local interval and weekday. |
| ServiceTypes | `DurationMinutes > 0` | Positive configured duration. |
| TechnicianShifts | `EndUtc > StartUtc`; `DATEPART(TZOFFSET, StartUtc) = 0 AND DATEPART(TZOFFSET, EndUtc) = 0` | Positive interval and UTC storage. |
| Appointments | `DurationMinutes > 0`; `EndUtc > StartUtc`; `EndUtc = DATEADD(minute, DurationMinutes, StartUtc)`; `Status IN (N'Confirmed')`; zero offsets on `StartUtc`, `EndUtc`, `CreatedUtc` | Positive duration, consistent snapshot interval, allowed status, UTC storage. |

> [!NOTE]
> The equality check assumes `DurationMinutes` is an `int` minute count and `DATEADD` preserves the `datetimeoffset` offset. Verify it on real SQL Server; if a check fails, review and record a design revision rather than silently removing the snapshot invariant.

### 2.4 Data sources for new fields

| Field | Source table/column or input | Type / nullability | Derivation and validation | Persistence / response / cache impact |
| --- | --- | --- | --- | --- |
| `Id` values | Seed constants; later appointment input/procedure | `uniqueidentifier`, PK | Fixed GUIDs for seeds; application-generated for new appointments later; no database default assumed. | Stored; future response IDs. Not cached in ticket 1. |
| `TimeZoneId` | Seeded dealership configuration | `nvarchar(100)`, required | Validate with `TimeZoneInfo.FindSystemTimeZoneById`; use IANA IDs. | Stored; ticket 2 uses it for local opening hours. |
| Opening intervals | Seeded configuration | `time(0)` local values | Positive interval on a weekday 0-6; one daytime interval per seeded weekday. | Stored; ticket 2 converts to UTC for availability. |
| `DurationMinutes` (service type) | Seeded service type | `int`, required | Positive; copied into appointments at confirmation. | Stored; future catalog cache payload may include it in ticket 2. |
| `StartUtc` / `EndUtc` | Seeded shifts and future booking input; server derivation | `datetimeoffset(7)`, required | Explicit offset input later; normalize to UTC; end derived from duration; half-open interval semantics. | Stored; future responses; availability is uncached. |
| `Status` | Server-set booking state | `nvarchar(20)`, required | Initial check allows `Confirmed` only. | Stored; future response. |
| `CreatedUtc` | Server clock via `TimeProvider` later | `datetimeoffset(7)`, required | UTC; no client input. | Stored; future response. |
| `Vin` | Seeded vehicle | `nvarchar(17)`, required | Normalize to uppercase; unique index. | Stored; not a telemetry metric label; future response if designed. |
| Ownership/dealership IDs | Seeded data and future booking input | `uniqueidentifier`, required FKs | Composite FKs enforce vehicle ownership and dealership consistency. | Stored; future responses/procedure. |

### 2.5 Setup and migration write path

Proposed flow:

```text
explicit --db-setup mode using privileged local sa connection
  -> EF Core Database.MigrateAsync
     -> schema, keys, checks, indexes, __EFMigrationsHistory, static HasData seeds
  -> idempotent principal script
     -> runtime login/user and least-privilege grants
  -> idempotent future-shift seeder (injected TimeProvider/current UTC)
  -> setup completes; API starts with runtime connection
```

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Migration | Setup identity -> `SchedulerDbContext.Database.MigrateAsync` -> SQL Server | Privileged identity; schema and history only; no runtime credentials. | Missing — proposed. |
| Static seeds | Migration `HasData` -> catalog/master tables | Deterministic fixed GUIDs; valid FK order; no appointments. | Missing — proposed. |
| Future shifts | Setup seeder -> `TechnicianShifts` | Time-relative, idempotent, horizon documented; uses seeded technicians. | Missing — proposed. |
| Runtime principal | Setup identity -> server `master` login creation -> application database user/grants | Separate from runtime; least privilege; no secrets in tracked files. | Missing — proposed. |
| Setup failure | Migration/script/seed failure -> non-zero exit | API must not start or report ready with a partially initialized schema. | Missing — proposed. |

### 2.6 Runtime read path

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Readiness | API -> runtime login -> `SELECT 1`/`CanConnect` | Runtime identity; no schema writes. | Missing — Part 1/3. |
| Future catalog/discovery reads | API -> EF Core `AsNoTracking` query -> response | Runtime `SELECT`; bounded/indexed queries. | Ticket 2; schema supports it. |
| Future appointment retrieval | API -> dealership-scoped query -> response | Runtime `SELECT`; out-of-dealership returns `404` per ticket 3. | Ticket 3; schema supports it. |

### 2.7 Denied write path

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Direct appointment write | Runtime login -> `INSERT`/`UPDATE`/`DELETE` on `Appointments` | No write grant; permission error; no row change. | Missing — Part 3 test. |
| Schema change | Runtime login -> `CREATE TABLE`/`ALTER TABLE`/`DROP` | No DDL grant/role; permission error. | Missing — Part 3 test. |
| Future booking write | Runtime login -> booking procedure `EXECUTE` | Granted in ticket 3; procedure owns the write through ownership chaining. | Deferred — ticket 3. |

### 2.8 Failure and recovery behavior

- Migration failure leaves the EF migration transaction rolled back where SQL Server supports it; the setup step exits non-zero and the API is not started by Compose.
- Principal/seed scripts must be idempotent so a retry after a partial setup can complete without duplicate rows or login errors.
- SQL Server unavailability during setup blocks initialization; after API startup, it makes readiness fail while liveness stays available.
- No booking retry, deadlock, or unknown-commit behavior exists in ticket 1; those belong to ticket 3.

## Phase 3: UX state matrix

N/A — no custom UI and no ticket-1 business endpoint. The schema supports future API states but does not introduce a user-visible workflow by itself.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict |
| --- | --- | --- | --- | --- |
| `docs/database/schema.md` | Planned tables/columns | Selected G-02 physical contract; no mappings, migration, or generated-SQL evidence (`docs/database/schema.md:9-63`). | S5, AC-5 | Must Change after migration |
| `docs/database/overview.md` | Relationships/integrity | Planned relationships; opening-hours storage selected by G-04 (`docs/database/overview.md:9-44`). | S5, AC-5 | Must Change after migration |
| `docs/database/migration.md` | Migration guidance/record | Guidance exists; migration record table is empty (`docs/database/migration.md:22-29`). | S5, AC-4 | Must Change after migration |
| `docs/database/cache.md` | Cache impact | Planned Redis inventory; no implemented keys (`docs/database/cache.md:9-26`). | S7, cache | Need Review / no change now |
| Entity/DbContext/migration candidates | No `src/` or C# files exist. | Nothing to inspect. | S5-S7 | Missing — proposed addition |

### Gate 2: Call path and boundary verification

1. No EF entity, DbContext, migration, login, or seed call path exists.
2. No database identity or grant can be inspected in the current checkout.
3. No cache path exists; ticket 1 has no Redis dependency.
4. The target code does not execute on any current path because it does not exist; every persistence/identity item is a proposed addition.

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| Setup/migration command | None | Privileged setup identity; migration transaction | No | Proposed; create and verify. |
| API runtime read | None | Restricted runtime login; SELECT | No | Proposed; create and verify. |
| Direct appointment write | None | Runtime login; no write grant | No | Proposed; test denial. |
| Schema change | None | Runtime login; no DDL grant | No | Proposed; test denial. |
| Future booking procedure | None | Runtime `EXECUTE`; ownership chain | No | Deferred to ticket 3. |

## Phase 4: Code audit

| Classification | File / symbol / component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence |
| --- | --- | --- | --- | --- | --- |
| Must Change | `docs/database/schema.md` | Selected G-02 physical contract; no migration or mapping evidence. | Record actual tables, columns, types, keys, checks, indexes, FK actions, and migration references. | S5, AC-5 | `docs/database/schema.md:9-63` |
| Must Change | `docs/database/overview.md` | Planned relationships with the G-04 opening-hours decision; no implemented schema. | Record implemented relationships and composite integrity details; the planned opening-hours table is already selected. | S5, AC-5 | `docs/database/overview.md:9-44` |
| Must Change | `docs/database/migration.md` | Empty migration record. | Record initial migration ID/path, objects, recovery, and verification. | S5, AC-4 | `docs/database/migration.md:22-29` |
| Must Change | `docs/architecture.md` | Planned domain/schema/identity sections. | Record implemented schema/identity choices and retain ticket-3 procedure as planned. | S5-S7, AC-9 | `docs/architecture.md:165-211` |
| Need Review | `docs/database/cache.md` | No cache implementation. | Add a note that ticket 1 creates no cache key; service-type seed shape is future ticket-2 input. | S7, cache assessment | `docs/database/cache.md:9-26` |
| Proposed Addition | Entity classes and EF configurations under `src/ServiceScheduler.Api/Infrastructure/Persistence` | Missing. | Model the shared domain with UUID/UTC mappings, alternate keys, composite FKs, checks, and indexes. | S5, AC-5 | Missing |
| Proposed Addition | `SchedulerDbContext` and design-time factory | Missing. | Register the DbContext; support migration creation without runtime secrets. | S5, AC-4 | Missing |
| Proposed Addition | Initial EF migration and model snapshot | Missing. | Create schema, constraints, indexes, static seeds, and migration history. | S5, AC-4 | Missing |
| Proposed Addition | Runtime-principal SQL/scripts | Missing. | Create runtime login/user and least-privilege SELECT grants; no write/DDL roles. | S6, AC-5 | Missing |
| Proposed Addition | Setup/seeding executable or script | Missing. | Apply migrations, create principal, seed static data and future shifts idempotently. | S6, S7, AC-4 | Missing |
| No Change | `docs/test/*` templates, review/deploy guides | General guidance, not ticket-specific implementation. | No change in research; use them in later phases. | Context | Inspected |

## Phase 5: Gap analysis for this part

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact | Status |
| --- | --- | --- | --- | --- | --- |
| G-02 | Decision | Exact physical names, column lengths, defaults, and FK actions. | Adopt the physical contract in [[database/schema]] and section 2.1; implement and compare generated SQL against it. | S5, AC-5 | Resolved for planning; migration/SQL verification pending |
| G-03 | Decision | How to enforce dealership ownership consistency in persistence. | Adopt accepted resolution G-03 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5, AC-5, BR-01, BR-02 | Resolved for planning; execution pending |
| G-04 | Decision | Opening-hours storage. | Adopt accepted resolution G-04 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5, AS-06 | Resolved for planning; execution pending |
| G-05 | Decision | Appointment status encoding. | Adopt accepted resolution G-05 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5 | Resolved for planning; execution pending |
| G-06 | Decision | Static versus time-relative seeds. | Adopt accepted resolution G-06 in the overview's accepted resolutions table; earlier alternatives are superseded. | S7, AC-4 | Resolved for planning; execution pending |
| G-07 | Decision | Setup/seeding mechanism. | Adopt accepted resolution G-07 in the overview's accepted resolutions table; earlier alternatives are superseded. | S6, S7, AC-4 | Resolved for planning; execution pending |
| G-08 | Decision | Setup identity choice. | Adopt accepted resolution G-08 in the overview's accepted resolutions table; earlier alternatives are superseded. | S6, AC-4 | Resolved for planning; execution pending |
| G-28 | Decision | Runtime grants. | Adopt accepted resolution G-28 in the overview's accepted resolutions table; earlier alternatives are superseded. | S6, AC-5 | Resolved for planning; execution pending |
| G-29 | Risk | Composite FKs and `DATEADD` checks may generate complex SQL or behave differently than expected. | Adopt accepted resolution G-29 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5, AC-5 | Mitigation selected; verification pending |
| G-30 | Risk | Seeder reruns may duplicate shifts or leave an expired demo horizon. | Adopt accepted resolution G-30 in the overview's accepted resolutions table; earlier alternatives are superseded. | S7, AC-4, T-16 | Mitigation selected; verification pending |
| G-31 | Risk | Runtime login creation may require server-level permissions and may not be idempotent. | Adopt accepted resolution G-31 in the overview's accepted resolutions table; earlier alternatives are superseded. | S6, AC-4 | Mitigation selected; verification pending |
| G-32 | Decision | How tests obtain a seeded initialized database. | Adopt accepted resolution G-32 in the overview's accepted resolutions table; earlier alternatives are superseded. | S10, AC-4, AC-5 | Resolved for planning; execution pending |
| G-33 | Decision | VIN uniqueness scope. | Adopt accepted resolution G-33 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5 | Resolved for planning; execution pending |
| G-34 | Risk | Opening-hour/shift data validity (overlaps, time-zone support). | Adopt accepted resolution G-34 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5, S7, AS-03, AS-06 | Mitigation selected; verification pending |
| G-35 | Decision | Migration rollback/recovery. | Adopt accepted resolution G-35 in the overview's accepted resolutions table; earlier alternatives are superseded. | S5, S8 | Resolved for planning; execution pending |

## Phase 6: Proposed changes and verification

The accepted resolutions in [[research/1/00-overview#Accepted resolutions for G-03 through G-43 (2026-10-31)]] govern this part, including `--db-setup` mode in the API executable, local `sa` setup, `scheduler_app` runtime identity, migration `HasData`, and the exact shift horizon. Earlier optional alternatives are superseded.

### 6.1 Dependency-ordered changes

| Step | Requirement IDs | Proposed files / components | Change and dependency | Verification |
| --- | --- | --- | --- | --- |
| 2.1 | S5 | Entity classes and EF configurations | Define entities, mappings, keys, checks, composite FKs, indexes, and delete behavior. | Model builds; generated SQL review. |
| 2.2 | S5, AC-4 | `SchedulerDbContext`, DI registration, design-time factory | Add DbContext and migration tooling; ensure startup validation does not require a live database. | `dotnet ef migrations add` succeeds. |
| 2.3 | S5, AC-4 | `Migrations/InitialCreate` and model snapshot | Generate the initial schema and deterministic static seeds. | Migration applies to empty real SQL Server; schema assertions. |
| 2.4 | S6, AC-5 | Runtime-principal SQL/scripts | Create runtime login/user and SELECT-only grants; separate from setup identity. | Runtime read succeeds; direct writes/DDL denied. |
| 2.5 | S7, AC-4 | Setup/seeding component | Apply migrations, create principal, seed static data and future shifts idempotently. | Empty database initializes; rerun is idempotent. |
| 2.6 | S7, T-16 | Seed documentation and commands | Document seeded identifiers, future-shift horizon, rerun behavior, and reset path. | Setup guide/update docs. |
| 2.7 | S5-S7, AC-5 | Database documentation | Update schema, overview, migration record, architecture, and cache impact. | Document review against migration. |

### 6.2 Proposed seeding details

- Dealerships: at least two synthetic dealerships with IANA time zones supported by the target host; weekday opening hours.
- Customers and vehicles: synthetic identities with fixed GUIDs; vehicle `CustomerId` ownership; normalized VINs.
- Service types: positive durations per dealership; at least one service type per dealership.
- Technicians: active and inactive synthetic technicians; qualifications for the seeded service types at the same dealership.
- Shifts: capture the setup clock once; start at the next dealership-local day, seed weekdays 09:00-17:00 for 28 local calendar days (end exclusive), and convert each boundary to UTC. Add missing intervals only; do not delete historical shifts.
- Bays: active bays (and at least one inactive bay if useful for eligibility tests) per dealership.
- Appointments: none seeded by ticket 1; ticket 3 creates confirmed appointments through the protected procedure. Tests may insert invalid rows with the setup identity solely to prove constraints.
- Do not seed secrets, real customer data, or production identifiers.

### 6.3 Proposed identity and grant details

- Setup identity: SQL `sa` in the isolated Compose SQL Server (local administrator password from `.env`); no additional setup login. Only setup mode receives this connection string.
- Runtime identity: selected SQL login `scheduler_app` with a same-named database user in the application database.
- Runtime grants: `CONNECT` and `GRANT SELECT ON SCHEMA::dbo`; no `db_owner`, `db_datawriter`, `db_ddladmin`, `db_securityadmin`, or `sysadmin`; no direct `INSERT`/`UPDATE`/`DELETE`; no `EXECUTE` in ticket 1.
- Future ticket 3: grant `EXECUTE` on the booking procedure only; verify ownership chaining does not require direct appointment write grants.
- Connection strings: runtime uses the restricted login; setup uses the privileged login; local `Encrypt=True;TrustServerCertificate=True` is scoped to the Compose connection and documented as a local limitation.
- Passwords come from environment/.env placeholders, are never committed, and are not written to logs or test reports.

### 6.4 Proposed initialization and idempotency

1. Start SQL Server and wait for its healthcheck.
2. Run the privileged setup step: apply EF migrations, create/update the runtime login/user, apply grants, seed static data, and extend future shifts.
3. Verify setup exit code; only then start the API.
4. Rerun behavior: EF migrations are recorded; principal scripts use `IF NOT EXISTS`/idempotent grants; static seeds use fixed keys; shifts use the unique interval index.
5. Reset behavior: `docker compose down -v` removes the named volume and all local data; document this as destructive and separate from normal restart.

## Rollback or forward recovery

| Step | Objects / migration IDs | Recovery action | Prerequisites / identity | Compatibility and data-loss limits | Verification |
| --- | --- | --- | --- | --- | --- |
| Initial schema | Proposed `InitialCreate` (ID pending) | `dotnet ef database update 0` on an empty local database, or drop/recreate the local database/volume and rerun setup. | Privileged setup identity; isolated local environment. | Down migration drops schema; deleting the volume removes all synthetic data. | Planned; Not run. |
| Runtime principal/grants | Proposed principal scripts | Rerun scripts; recreate login/user if the database was recreated. | Setup identity with server/database admin rights. | Password resupply required; no data restoration. | Planned; Not run. |
| Seeds | Proposed static/seed component | Rerun idempotent setup; extend shift horizon if expired. | Setup identity. | Additive/upsert; do not delete existing appointments. | Planned; Not run. |

No production data exists. Document that a down migration or code revert does not restore deleted data.

## Cache impact

- Ticket 1 does not implement Redis or any cache key, TTL, or invalidation.
- The seeded `ServiceTypes` shape (dealership scope, name, positive duration) is the future source for ticket 2's planned `scheduler:v1:dealership:{id}:service-types` catalog cache.
- No cache-registry entry or refresh dashboard is added in ticket 1.
- When ticket 2 implements the cache, seed/catalog changes must be assessed for affected-key invalidation or namespace versioning. Record this as a handoff.

### Database impact record

- Ticket / requirement: Ticket 1 / S5-S7, AC-4, AC-5, T-16.
- Status: planned; verification: Not run.
- Tables and columns (before -> after): none -> proposed `Dealerships`, `DealershipOpeningHours`, `Customers`, `Vehicles`, `ServiceTypes`, `Technicians`, `TechnicianQualifications`, `TechnicianShifts`, `ServiceBays`, `Appointments`.
- Relationships and integrity rules: composite FKs enforce vehicle ownership and dealership consistency; positive durations; UTC `datetimeoffset(7)`; interval/duration checks; restrict/no-action deletes.
- Constraints, indexes, and procedures: keys/checks/indexes proposed; booking procedure and `EXECUTE` grant deferred to ticket 3.
- Migration ID/path and EF mapping references: pending `InitialCreate` under `src/ServiceScheduler.Api/Migrations`.
- Existing data, seeds, compatibility, and recovery: empty local database; static synthetic seeds plus future-shift seeder; reset via local database/volume; no production data.
- Cache impact (or reason for none): none in ticket 1; service-type seed shape is future ticket-2 cache input.
- Database documents updated: schema and overview record the selected G-02 physical contract (2026-10-31); migration record, implemented mapping references, architecture implementation status, and cache note remain pending implementation.
- Verification commands, observed results, and evidence links: planned migration/constraint/permission tests; Not run.

## Phase 7: Part scope verification

| Change | Requirement IDs | Direct requirement or necessary dependency | Verification |
| --- | --- | --- | --- |
| Entities/mappings for shared domain | S5, AC-5 | Required schema model | Migration/constraint tests. |
| Initial migration and snapshot | S5, AC-4 | Required database initialization | Apply to empty real SQL Server. |
| Composite keys/FKs and checks | S5, AC-5, BR-01, BR-02 | Required relationship/ownership integrity | Invalid-insert tests. |
| Runtime principal/grants | S6, AC-5 | Required restricted write boundary | Runtime read; denied writes/DDL. |
| Setup identity and initialization | S6, S7, AC-4 | Required privilege separation and reproducible setup | Setup rerun/idempotency checks. |
| Static and future-shift seeds | S7, AC-4, T-16 | Required synthetic data | Seed assertions and horizon checks. |
| Migration/cache documentation | S5-S7, AC-9 | Required documentation maintenance | Document review. |
| Booking procedure/EXECUTE grant | Excluded | Ticket 3 dependency | No change in ticket 1. |
| Redis cache | Excluded | Ticket 2 dependency | No change in ticket 1. |

## Handoffs

| Finding / decision | Target | Required action |
| --- | --- | --- |
| Proposed table/column names, composite FKs, and checks. | Part 3 | Build integration assertions against the actual migration, not the proposal. |
| Runtime login and SELECT-only grants. | Part 3 | Use the runtime identity for read/denial tests; use setup identity for constraint tests. |
| Seeder horizon and fixed identifiers. | Part 3 / demo scripts | Use the same IDs and future-time strategy in tests and demo documentation. |
| No booking procedure or `EXECUTE` grant. | Ticket 3 | Add the protected write path without weakening direct-write denial. |
| Service-type seed shape. | Ticket 2 | Define cache key/payload/TTL/invalidation. |
| Local certificate trust setting. | Part 1 / deployment notes | Keep scoped to the local Compose connection. |
