---
title: "Scheduler Database Migrations"
tags: [database, migrations, service-scheduler]
updated: 2026-11-02
---

# Database migrations

EF Core migrations define the SQL Server schema, static seeds, time-zone mappings, and booking procedure. The privileged setup command applies migrations before validating mappings and the procedure, provisioning the runtime identity, and extending synthetic technician shifts. Normal API startup does not apply migrations.

## Migration files

Files are under `src/ServiceScheduler.Api/Migrations/`; `SchedulerDbContextModelSnapshot.cs` represents the current EF model.

| Migration | Objects and compatibility |
| --- | --- |
| `20261031092016_InitialCreate` | Creates dealerships, opening hours, customers, vehicles, service types, technicians, qualifications, shifts, bays, and appointments, including keys, constraints, indexes, and fixed-ID seeds. Time-relative shifts are added by setup. |
| `20261101161530_AddBookingProcedureAndTimeZoneMappings` | Creates and seeds `dbo.TimeZoneMappings`, adds the dealership time-zone FK, and creates `dbo.CreateAppointment`. Existing dealership IANA IDs must have a mapping before upgrade; the migration does not rewrite appointments. |

Runtime grants and appointment DML denies are applied by `RuntimePrincipalProvisioner` after migrations because the runtime user may not yet exist during schema creation. Redis and telemetry configuration do not require application database migrations.

## Apply and inspect migrations

From the repository root, configure `ConnectionStrings__Setup` and `Scheduler__RuntimePassword` as described in [[setup-guide]], then run:

```powershell
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -- --db-setup
```

For EF tooling and generated SQL:

```powershell
dotnet tool restore
dotnet ef migrations list --no-connect --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
dotnet ef migrations script --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -o migration.sql
```

`--no-connect` lists project migrations without contacting SQL Server. To inspect applied migrations, omit that flag and export the setup connection. Host commands do not automatically load `.env`.

## Compatibility and recovery

Setup reruns use EF migration history and add only missing synthetic shifts. They do not rotate an existing runtime login password. Keep runtime and setup credentials consistent with the local database.

The booking migration's `Down` method drops the procedure, dealership mapping FK, and mapping table while preserving existing appointments. Appointment DML denies remain on the surviving table, while procedure permissions disappear with the procedure. Restoring older setup code may require explicit permission cleanup; rerunning the current setup reinstates its restricted runtime boundary. Do not grant direct appointment writes as a workaround.

For an isolated synthetic demo, recreating the database and rerunning setup provides a fresh state. `docker compose down` preserves named volumes; `docker compose down -v` deletes local database and telemetry data. A code revert or down migration does not restore deleted data.

## Developing a schema change

1. Define compatibility, defaults or backfill, and how existing rows remain valid.
2. Update the EF mappings and generate a migration with the pinned local tool.
3. Inspect generated SQL for data loss, constraints, FK actions, indexes, procedure ownership, and runtime permissions.
4. Exercise empty-database initialization, upgrade with existing data, repeated setup, and relevant recovery paths against real SQL Server.
5. Run affected persistence, booking, concurrency, and permission tests; review cache invalidation if source data changes.
6. Update [[database/schema|Schema]], [[database/overview|Relationships]], and [[database/cache|Cache]] alongside the code. Keep execution results in dedicated test records.

See [[database/maintenance|Maintenance guideline]] for the complete change checklist.
