---
title: "Scheduler Schema Reference"
tags: [database, service-scheduler, schema]
updated: 2026-11-02
---

# Schema reference

This reference describes the SQL Server model defined by `src/ServiceScheduler.Api/Infrastructure/Persistence/` and the EF Core migrations in `src/ServiceScheduler.Api/Migrations/`. See [[architecture]] for scheduling assumptions and rules.

## Shared conventions

- Physical schema: `dbo`; plural PascalCase table names and PascalCase column names as listed below.
- Every listed column is `NOT NULL`. No SQL default, computed column, identity, or rowversion is selected. Setup/application code must supply every value, including active flags, status, and creation time.
- Every `Id` and every column ending in `Id` is `uniqueidentifier`, except `TimeZoneId` (IANA ID), `TimeZoneMappings.IanaId` (IANA ID), and `TimeZoneMappings.WindowsId` (SQL Server identifier). GUIDs are supplied by seeds/application code, with EF `ValueGeneratedNever`; seeds use fixed GUIDs.
- Every `Name` is `nvarchar(200)`. Other string lengths are explicit below. Retain the database collation; uniqueness follows that collation. Normalize VINs to uppercase before persistence; the unique index alone is not a VIN-format validator.
- UTC instants are `datetimeoffset(7)` with offset zero. Local opening times are `time(0)`; weekday `0` is Sunday through `6` Saturday, independent of SQL `DATEFIRST`.
- All foreign keys use SQL `ON DELETE NO ACTION` and `ON UPDATE NO ACTION`, with EF `DeleteBehavior.NoAction`. No cascade or set-null action is selected; parent deletion/key changes fail while references exist.
- Primary keys are clustered; alternate keys and secondary indexes are nonclustered. GUID clustering is adequate for this small synthetic demo; revisit only with measured scale requirements.

> [!NOTE]
> EF Core does not configure an `ON UPDATE` action. The generated migration omits explicit `ON DELETE`/`ON UPDATE` clauses, SQL Server's default is `NO ACTION`, and the integration tests assert `NO_ACTION` for both catalog columns. The exact index inventory below is present with no convention-generated FK indexes; `ForeignKeyIndexConvention` is disabled in `SchedulerDbContext.ConfigureConventions`.

## Tables and columns

| Table                          | Columns and purpose                                                                                                                                                                                                                                 | Keys and relationships                                                                                                                                                                                                                                                                                                                                              |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `dbo.Dealerships`              | `Id`; `Name`; `TimeZoneId nvarchar(100)` (IANA)                                                                                                                                                                                                     | PK `Id`; unique `Name`; FK `TimeZoneId` -> `TimeZoneMappings(IanaId)`                                                                                                                                                                                                                                                                                                |
| `dbo.TimeZoneMappings`         | `IanaId nvarchar(100)` (canonical IANA ID); `WindowsId nvarchar(100)` (SQL Server `AT TIME ZONE` identifier)                                                                                                                                        | PK `IanaId`; unique `WindowsId`; referenced by `Dealerships.TimeZoneId`                                                                                                                                                                                                                                                                                             |
| `dbo.DealershipOpeningHours`   | `DealershipId`; `DayOfWeek tinyint`; `OpensAt time(0)`; `ClosesAt time(0)`                                                                                                                                                                          | PK `(DealershipId, DayOfWeek, OpensAt)`; FK `DealershipId` -> `Dealerships(Id)`                                                                                                                                                                                                                                                                                     |
| `dbo.Customers`                | `Id`; `Name`; `Email nvarchar(256)` (synthetic contact)                                                                                                                                                                                             | PK `Id`; email is not unique                                                                                                                                                                                                                                                                                                                                        |
| `dbo.Vehicles`                 | `Id`; `CustomerId`; `Vin nvarchar(17)`; `Make nvarchar(100)`; `Model nvarchar(100)`                                                                                                                                                                 | PK `Id`; FK `CustomerId` -> `Customers(Id)`; alternate key `(Id, CustomerId)`; globally unique `Vin`                                                                                                                                                                                                                                                                |
| `dbo.ServiceTypes`             | `Id`; `DealershipId`; `Name`; `DurationMinutes int`                                                                                                                                                                                                 | PK `Id`; FK `DealershipId` -> `Dealerships(Id)`; alternate key `(Id, DealershipId)`; unique `(DealershipId, Name)`                                                                                                                                                                                                                                                  |
| `dbo.Technicians`              | `Id`; `DealershipId`; `Name`; `IsActive bit`                                                                                                                                                                                                        | PK `Id`; FK `DealershipId` -> `Dealerships(Id)`; alternate key `(Id, DealershipId)`                                                                                                                                                                                                                                                                                 |
| `dbo.TechnicianQualifications` | `TechnicianId`; `ServiceTypeId`; `DealershipId`                                                                                                                                                                                                     | PK `(TechnicianId, ServiceTypeId)`; composite FKs `(TechnicianId, DealershipId)` -> `Technicians(Id, DealershipId)` and `(ServiceTypeId, DealershipId)` -> `ServiceTypes(Id, DealershipId)`                                                                                                                                                                         |
| `dbo.TechnicianShifts`         | `Id`; `TechnicianId`; `StartUtc datetimeoffset(7)`; `EndUtc datetimeoffset(7)`                                                                                                                                                                      | PK `Id`; FK `TechnicianId` -> `Technicians(Id)`; unique `(TechnicianId, StartUtc, EndUtc)`                                                                                                                                                                                                                                                                          |
| `dbo.ServiceBays`              | `Id`; `DealershipId`; `Name`; `IsActive bit`                                                                                                                                                                                                        | PK `Id`; FK `DealershipId` -> `Dealerships(Id)`; alternate key `(Id, DealershipId)`; unique `(DealershipId, Name)`                                                                                                                                                                                                                                                  |
| `dbo.Appointments`             | `Id`; `DealershipId`; `CustomerId`; `VehicleId`; `ServiceTypeId`; `TechnicianId`; `ServiceBayId`; `StartUtc datetimeoffset(7)`; `EndUtc datetimeoffset(7)`; `DurationMinutes int` (snapshot); `Status nvarchar(20)`; `CreatedUtc datetimeoffset(7)` | PK `Id`; FKs `DealershipId` -> `Dealerships(Id)`, `CustomerId` -> `Customers(Id)`; composite FKs `(VehicleId, CustomerId)` -> `Vehicles(Id, CustomerId)`, `(ServiceTypeId, DealershipId)` -> `ServiceTypes(Id, DealershipId)`, `(TechnicianId, DealershipId)` -> `Technicians(Id, DealershipId)`, `(ServiceBayId, DealershipId)` -> `ServiceBays(Id, DealershipId)` |

Each FK is a required many-to-one relationship; each parent may have zero or more dependents. The opening-hours composite key allows multiple same-day intervals; overlapping opening intervals/shifts are not prevented by these keys. The initial status check accepts only `Confirmed`.

## Constraint and index contract

Use these deterministic names, preserving column order from the tables above: `PK_<Table>`; `AK_<Table>_<Columns>` for alternate keys; `FK_<Dependent>_<Principal>_<DependentColumns>` for foreign keys; `UX_<Table>_<Columns>` for other unique indexes; `IX_<Table>_<Columns>` for nonunique indexes. `<Columns>` joins column names with underscores, without the `dbo` prefix. For example, `FK_Appointments_Vehicles_VehicleId_CustomerId` references `AK_Vehicles_Id_CustomerId`. Each relationship has one FK; do not add redundant single-column FKs alongside the composite FKs.

| Check name                            | SQL expression                                                                                                     |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `CK_DealershipOpeningHours_DayOfWeek` | `[DayOfWeek] BETWEEN 0 AND 6`                                                                                      |
| `CK_DealershipOpeningHours_Interval`  | `[ClosesAt] > [OpensAt]`                                                                                           |
| `CK_ServiceTypes_DurationMinutes`     | `[DurationMinutes] > 0`                                                                                            |
| `CK_TechnicianShifts_Interval`        | `[EndUtc] > [StartUtc]`                                                                                            |
| `CK_TechnicianShifts_UtcOffsets`      | `DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0`                                          |
| `CK_Appointments_DurationMinutes`     | `[DurationMinutes] > 0`                                                                                            |
| `CK_Appointments_Interval`            | `[EndUtc] > [StartUtc]`                                                                                            |
| `CK_Appointments_DurationSnapshot`    | `[EndUtc] = DATEADD(minute, [DurationMinutes], [StartUtc])`                                                        |
| `CK_Appointments_Status`              | `[Status] IN (N'Confirmed')`                                                                                       |
| `CK_Appointments_UtcOffsets`          | `DATEPART(TZOFFSET, [StartUtc]) = 0 AND DATEPART(TZOFFSET, [EndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedUtc]) = 0` |

Create the unique indexes listed in the table plus nonunique indexes on `Vehicles(CustomerId)`, `Technicians(DealershipId)`, `TechnicianQualifications(ServiceTypeId, DealershipId)`, `Appointments(CustomerId)`, `Appointments(VehicleId, CustomerId)`, and `Appointments(ServiceTypeId, DealershipId)`. Add the following query indexes:

| Index name                                           | Key columns                              | Included columns | Filter |
| ---------------------------------------------------- | ---------------------------------------- | ---------------- | ------ |
| `IX_TechnicianShifts_TechnicianId_StartUtc`          | `(TechnicianId, StartUtc)`               | `EndUtc`         | None   |
| `IX_Appointments_DealershipId_TechnicianId_StartUtc` | `(DealershipId, TechnicianId, StartUtc)` | `EndUtc, Status` | None   |
| `IX_Appointments_DealershipId_ServiceBayId_StartUtc` | `(DealershipId, ServiceBayId, StartUtc)` | `EndUtc, Status` | None   |

Time-zone mapping uses `PK_TimeZoneMappings` (clustered on `IanaId`), `UX_TimeZoneMappings_WindowsId` (unique), and `FK_Dealerships_TimeZoneMappings_TimeZoneId` with `NO_ACTION` delete/update behavior.

Do not add indexes duplicating a selected key/index's leading columns: the opening-hours PK covers `(DealershipId, DayOfWeek)`; service-type/bay unique indexes cover `DealershipId`; shift uniqueness covers `TechnicianId`. The qualification PK covers technician lookup. Review EF-generated FK indexes against the selected inventory and remove redundant convention indexes; validate the two appointment resource indexes with the conflict queries. No filtered index is selected initially.

## Cross-table integrity and indexes

Appointment service type, technician, and bay must belong to its dealership; its vehicle must belong to its customer. Qualifications must be dealership consistent. The selected composite keys/FKs enforce these relationships; generated SQL and invalid-insert tests must verify that implementation follows this contract. Individual entity FKs alone do not establish this consistency.

The two appointment resource indexes support confirmed-appointment conflict queries using `Status` as a predicate. A unique start-time index alone does not prevent overlapping intervals.

`dbo.CreateAppointment` enforces eligibility, duration, opening/shift coverage, and overlap rules inside its locked transaction. It is created by `20261101161530_AddBookingProcedureAndTimeZoneMappings`. See [[database/overview|Overview]].

## Read queries and supporting indexes

The catalog and availability operations read the existing schema only; Redis stores an optional service-catalog copy outside SQL Server. [[database/cache|Cache]] owns the Redis inventory.

The availability queries map to the existing objects as follows:

| Query | SQL predicates | Supporting object |
| --- | --- | --- |
| Service/dealership lookup | `ServiceTypes.Id`, `ServiceTypes.DealershipId`, join `Dealerships.Id` | `PK_ServiceTypes`, `AK_ServiceTypes_Id_DealershipId`, `PK_Dealerships` |
| Opening coverage | `DealershipId`, `DayOfWeek`, `OpensAt <= local start`, `ClosesAt >= local end` | `PK_DealershipOpeningHours` |
| Technician eligibility | `DealershipId`, `IsActive`, qualification by service/dealership, one shift covering the interval, no confirmed half-open overlap | `IX_Technicians_DealershipId`, `IX_TechnicianQualifications_ServiceTypeId_DealershipId`, `IX_TechnicianShifts_TechnicianId_StartUtc`, `IX_Appointments_DealershipId_TechnicianId_StartUtc` |
| Bay eligibility | `DealershipId`, `IsActive`, no confirmed half-open overlap, ordered by name/ID | `UX_ServiceBays_DealershipId_Name`, `IX_Appointments_DealershipId_ServiceBayId_StartUtc` |

`tests/ServiceScheduler.IntegrationTests/Availability/AvailabilitySqlTests` asserts that generated SQL contains the dealership, active-state, and service predicates; the shift-bound `<=`/`>=` and half-open `<`/`>` comparison operators; `Status = Confirmed`; `NOT EXISTS`; and stable ordering. A behavioral test also covers a shift that starts after the requested start. Evaluate execution plans on representative data before changing these indexes; the local seeds are not a production workload.

## Time-zone mapping and booking procedure

| Object | Contract |
| --- | --- |
| `dbo.TimeZoneMappings` | `IanaId nvarchar(100)` PK and `WindowsId nvarchar(100)` unique; seeded `Asia/Ho_Chi_Minh -> SE Asia Standard Time` and `Europe/London -> GMT Standard Time`; required because SQL Server `AT TIME ZONE` accepts Windows identifiers, not IANA identifiers. |
| `Dealerships` FK | `FK_Dealerships_TimeZoneMappings_TimeZoneId` on `TimeZoneId` -> `TimeZoneMappings(IanaId)`, `NO_ACTION`; a dealership with an unmapped IANA ID cannot be created until a mapping row exists. |
| `dbo.CreateAppointment` | Same-owner, static, parameterized procedure with inputs `@DealershipId`, `@CustomerId`, `@VehicleId`, `@ServiceTypeId`, `@TechnicianId`, `@ServiceBayId`, `@StartUtc`, `@EvaluatedAtUtc` and outputs `@AppointmentId`, `@EndUtc`, `@DurationMinutes`, `@CreatedUtc`. Rejects a caller-owned transaction; owns one `SERIALIZABLE` transaction with `XACT_ABORT ON`, `LOCK_TIMEOUT 5000`, technician-then-bay `UPDLOCK, HOLDLOCK`, full revalidation, independent half-open conflict checks, insert, and commit. |
| Procedure errors | `51001` technician conflict; `51002` bay conflict; `51003` invalid request/ownership/scope/interval or caller-owned transaction; `51004` resource not eligible; `51005` opening/shift coverage unavailable; `51006` missing/invalid SQL time-zone mapping. Lock timeout `1222`, command timeout `-2`, deadlock `1205`, and classified connectivity remain SQL/dependency outcomes. |
| Runtime permissions | `CONNECT`; schema `dbo` `SELECT`; object `EXECUTE` on `dbo.CreateAppointment`; object `INSERT`/`UPDATE`/`DELETE` `DENY` on `dbo.Appointments`; no schema `EXECUTE`, direct write grant, DDL, ownership, or role. |
| Appointment table/indexes | Unchanged. The existing `IX_Appointments_DealershipId_TechnicianId_StartUtc` and `IX_Appointments_DealershipId_ServiceBayId_StartUtc` indexes, including `EndUtc`/`Status`, cover the procedure predicates; no new appointment index was added. |
| Upgrade/recovery | Mapping rows are seeded and the FK is added after the seed; seeded dealership rows match. The down migration drops the procedure, FK, and mapping table while preserving appointments. Appointment DML denies on the surviving table may require an explicit `REVOKE` when restoring an earlier setup version; see [[database/migration|Migrations]]. |

## Source files and seeds

- Mappings: `Infrastructure/Persistence/Configurations/` implements the table, column, key, check, index, and composite-FK contract; `SchedulerDbContext` disables EF's convention FK indexes so only the selected inventory is created.
- Migrations: `src/ServiceScheduler.Api/Migrations/20261031092016_InitialCreate.cs`, `20261101161530_AddBookingProcedureAndTimeZoneMappings.cs`, and `SchedulerDbContextModelSnapshot.cs`.
- Static seeds: `Infrastructure/Persistence/Configurations/SeedData.cs` uses fixed GUIDs for two dealerships, time-zone mappings, weekday opening hours, customers, vehicles, service types, technicians, qualifications, and bays. No appointment or shift is seeded statically; setup adds future shifts.

Update this reference when mappings, migrations, or data contracts change. Follow [[database/maintenance|Maintenance guideline]].
