---
title: "3 - Appointment Booking and Retrieval: Permissions, Migrations, and Operations"
tags: [research, service-scheduler, booking, permissions, migrations, setup]
ticket: 3
status: verified
updated: 2026-11-01
---

# 3 - Permissions, migrations, and operations

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research overview: [[research/3/00-overview|Ticket 3 research overview]]
- Requirements: S3-03, S3-06; AC3-05, AC3-07, AC3-09, AC3-13; T-08, T-14, T-16; S3-V06; project requirements P-01, P-04, D-03.
- Acceptance labels: `AC3-01`-`AC3-13` are research-local mappings to the ticket's 13 unlabeled acceptance bullets, defined in [[research/3/00-overview|Overview 0.2]]; they are not new ticket IDs.
- Evidence baseline: HEAD `90a6284` plus the staged 2026-11-01 ticket update; inspected provisioner/setup/migration/configuration/Compose/tests/docs. Research-phase probes on 2026-11-01 used the pinned SQL Server 2022 CU23 container.

> [!IMPORTANT]
> The ticket 3 migration, grants/denies, setup validation, configuration, and recovery records do not exist. The permission and migration design below is proposed; every ticket-level setup/upgrade/recovery check is Not run.

## Existing setup and permission boundary

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| `--db-setup` branches before web-host construction and runs `DatabaseSetupRunner`. | `Program.cs`; `Infrastructure/Setup/DatabaseSetupCommand.cs`. | Setup remains the only migration/seed path. |
| Setup order is `MigrateAsync` -> `RuntimePrincipalProvisioner.ProvisionAsync` -> `FutureShiftSeeder.SeedAsync`. | `DatabaseSetupRunner.cs`; `FutureShiftSeeder.cs`. | Add mapping/procedure validation after migrations; grants stay after user creation. |
| The runtime login must be an enabled SQL login with no server-role membership or unexpected server permissions. | `RuntimePrincipalProvisioner.ValidateLoginAsync`. | Keep; no server-level `EXECUTE`/role. |
| The runtime user must be SQL-user-mapped with a matching SID and no database-role membership. | `RuntimePrincipalProvisioner.ValidateUserAsync`. | Keep; no `db_owner`/`db_datawriter`/`db_ddladmin`/etc. |
| Pre- and post-grant validation allow only database `CONNECT` and schema `dbo` `SELECT`; every other explicit permission is rejected. | `RuntimePrincipalProvisioner.ValidateDatabasePermissionsAsync` lines 244-287. | Must Change: allow exact procedure `EXECUTE` and exact appointment DML denies. |
| Grants are applied after user creation because a fresh database migration runs before the runtime user exists. | `ProvisionAsync`/`GrantRuntimePermissionsAsync` lines 32-57, 290-303. | Keep grants/denies in the provisioner, not the migration. |
| Direct runtime appointment `INSERT`/`UPDATE`/`DELETE` and DDL are denied; reads succeed. | Existing `RuntimePermissionTests`. | Preserve; add procedure `EXECUTE` assertion. |
| The initial migration creates no procedure or `EXECUTE` grant. | `20261031092016_InitialCreate.cs`; docs/database/migration.md. | New migration + provisioner change required. |

## Proposed runtime permission model

### Exact grants and denies

For the runtime login `scheduler_app`, after the procedure exists:

```sql
GRANT CONNECT TO [scheduler_app];
GRANT SELECT ON SCHEMA::dbo TO [scheduler_app];
GRANT EXECUTE ON OBJECT::dbo.CreateAppointment TO [scheduler_app];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments TO [scheduler_app];
```

No schema-level `EXECUTE`, no direct appointment `INSERT`/`UPDATE`/`DELETE` grant, no DDL/`ALTER`/`CONTROL`, no owner or fixed database role, and no server role.

### Observed ownership-chaining behavior

A research probe created a simplified `dbo.Appointments` table, a dbo-owned procedure that inserts into it, a `deny_app` user with `CONNECT` and object `EXECUTE`, and `DENY INSERT, UPDATE, DELETE` on the table. Results:

| Action as `deny_app` | Observed result |
| --- | --- |
| `EXEC dbo.InsertAppointment ...` | Succeeded; the row was inserted (counted as `sa`). |
| Direct `INSERT dbo.Appointments ...` | Failed: `Msg 229`, `The INSERT permission was denied on the object 'Appointments'`. |
| `sys.database_permissions` | `DATABASE CONNECT GRANT`; `OBJECT_OR_COLUMN EXECUTE GRANT` on the procedure; `OBJECT_OR_COLUMN INSERT/UPDATE/DELETE DENY` on the table. |

This confirms SQL Server ownership chaining lets the same-owner procedure write the table even with an explicit table `DENY`, while direct runtime DML remains denied. The real procedure must be dbo-owned and reference dbo tables; changing ownership or crossing a broken ownership chain would invalidate this assumption. The probe used simplified objects, not the ticket schema.

### Provisioner allowlist

`ValidateDatabasePermissionsAsync` must accept exactly these explicit permissions and reject everything else:

| `class_desc` | `permission_name` | `state_desc` | Object |
| --- | --- | --- | --- |
| `DATABASE` | `CONNECT` | `GRANT` | database |
| `SCHEMA` | `SELECT` | `GRANT` | `dbo` schema id |
| `OBJECT_OR_COLUMN` | `EXECUTE` | `GRANT` | `OBJECT_ID('dbo.CreateAppointment', 'P')` |
| `OBJECT_OR_COLUMN` | `INSERT` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |
| `OBJECT_OR_COLUMN` | `UPDATE` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |
| `OBJECT_OR_COLUMN` | `DELETE` | `DENY` | `OBJECT_ID('dbo.Appointments', 'U')` |

Additional validation rules:

- Reject `GRANT` on appointment `INSERT`/`UPDATE`/`DELETE`, `DENY` on procedure `EXECUTE`, `GRANT`/`DENY` on any other object, `WITH GRANT OPTION` (`W`), schema `EXECUTE`/`ALTER`/`CONTROL`, database/object ownership, and role membership.
- The pre-grant validation must allow the ticket 2 state (no object permissions) and the ticket 3 state (the exact rows above), so repeated setup is idempotent.
- If `OBJECT_ID('dbo.CreateAppointment', 'P')` is null, setup should fail with a clear message instead of an unclear `GRANT` error. A setup check can verify the procedure exists.
- The validator must use `OBJECT_ID`/`SCHEMA_ID` rather than name-only string comparisons, and must not accept an object in another schema with the same name.
- No changes to the server-level login validation or SID/role checks.

### Setup order and repeated setup

Proposed order:

1. `MigrateAsync` applies the ticket 3 migration: mapping table/seed/FK and `dbo.CreateAppointment`.
2. `TimeZoneMappingValidator` verifies dealership mappings, Windows IDs, and .NET/SQL DST parity; verifies the procedure exists.
3. `RuntimePrincipalProvisioner` creates/validates the login and user; pre-validates permissions; grants/denies; post-validates the exact allowlist.
4. `FutureShiftSeeder` adds missing future shifts.

Fresh database: migration runs before the user exists; grants/denies run after user creation. Upgrade from ticket 2: the user and read grants already exist; the pre-validation sees only `CONNECT`/`SELECT` (allowed); grants/denies are added; post-validation passes. Repeated ticket 3 setup: the pre-validation sees the exact ticket 3 permissions; the allowlist must allow them; re-grant/re-deny is idempotent; post-validation passes. A failed setup is safe to rerun because migrations are transactional and grants/denies are idempotent; the shift seeder only adds missing rows.

### Rollback compatibility

- Applying the migration down drops the procedure, mapping FK/table, and required permissions; existing appointments remain.
- Runtime grants/denies persist after a down migration. Before running a ticket 2 code/setup version, revoke them:
  ```sql
  REVOKE EXECUTE ON OBJECT::dbo.CreateAppointment FROM [scheduler_app];
  REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments FROM [scheduler_app];
  ```
  `REVOKE` removes the explicit grant/deny. Alternatively, drop/recreate the local database. The ticket 2 provisioner would otherwise reject the unexpected permissions.
- A code revert or down migration does not restore deleted appointment data; the local demo uses synthetic data.

## Proposed migration

### Objects and EF mapping

| Object | Change | Notes |
| --- | --- | --- |
| `dbo.TimeZoneMappings` | New table | `IanaId nvarchar(100)` PK; `WindowsId nvarchar(100) NOT NULL`; unique `UX_TimeZoneMappings_WindowsId`; seed `Asia/Ho_Chi_Minh -> SE Asia Standard Time` and `Europe/London -> GMT Standard Time`. |
| `dbo.Dealerships` | New FK | `FK_Dealerships_TimeZoneMappings_TimeZoneId` on `TimeZoneId` -> `TimeZoneMappings.IanaId`, `ON DELETE NO ACTION`. |
| `dbo.CreateAppointment` | New procedure | Proposed contract in [[research/3/02-booking-procedure-and-concurrency|Part 2]]. |
| `dbo.Appointments` | No change | Existing columns/checks/FKs/indexes suffice; no new appointment index. |
| EF model/snapshot | New entity/config/seed/FK | `TimeZoneMapping` entity, `TimeZoneMappingConfiguration`, `SchedulerDbContext.TimeZoneMappings`, `DealershipConfiguration` FK, `SeedData` rows, updated `SchedulerDbContextModelSnapshot`. |

The actual migration filename/ID is generated during implementation (`dotnet ef migrations add ...`) and recorded in [[database/migration|Migrations]]; do not invent an ID in research.

### Required operation order

1. Create `TimeZoneMappings`.
2. Insert the two mapping rows.
3. Add the `Dealerships.TimeZoneId` FK (existing dealership rows must already match; the seeded rows do).
4. Create `dbo.CreateAppointment` (it references the mapping table).
5. Update the model snapshot/mappings in the same change.

EF's generated operation order must be inspected. If `AddForeignKey` is emitted before the mapping `InsertData`, the migration will fail against existing ticket 2 dealership rows; reorder the operations or add the FK via `migrationBuilder.Sql` after the seed. The upgrade test is the gate.

### Upgrade, compatibility, and recovery

- **Fresh database:** initial migration + seed, then ticket 3 migration; mapping rows/FK/procedure; provisioner grants/denies. No backfill.
- **Ticket 2 database:** existing dealership rows match the seeded mappings; the FK add succeeds; existing appointments are preserved. A custom dealership with an unmapped IANA ID would fail the FK; document that a mapping row is required before upgrade. This is a configuration/data compatibility limit, not an appointment-data migration.
- **Repeated setup:** migration history prevents reapplication; provisioner/setup validators tolerate the exact existing state; procedure remains.
- **Down migration:** `DROP PROCEDURE IF EXISTS dbo.CreateAppointment;` then drop the FK, then the mapping table. Existing appointments remain. Runtime permissions require the manual revoke above or a destructive reset.
- **Partial failure:** SQL Server DDL migrations run transactionally by default; a failed migration rolls back. If setup fails after the migration (mapping validation/grant), rerun after fixing the cause. Do not delete migration history as recovery.
- **Procedure missing after manual deletion:** EF migration history will not recreate it; restore the procedure via the migration SQL or recreate the database. Setup validation should detect the missing procedure and fail clearly. Document this in deploy notes.
- **No data backfill or destructive change:** existing appointments are not rewritten; no appointment index/column change.

### Migration verification

- Generate and inspect the migration SQL with `dotnet ef migrations script`.
- Apply to an empty real SQL Server database and to a database at `20261031092016_InitialCreate` containing a synthetic appointment.
- Assert mapping rows/FK/procedure exist, existing appointments survive, runtime permissions are exact, direct DML is denied, and the procedure executes.
- Apply the down migration and assert the procedure/FK/table are gone while appointments remain.
- Record actual commands/results in `docs/test/3-test.md`; unexecuted checks remain Not run.

## Booking configuration

Proposed `Scheduler:Booking` options (non-secret; defaults in `appsettings.json`):

| Key | Default | Validation | Purpose |
| --- | --- | --- | --- |
| `MaxCandidateAttempts` | `5` | Integer 1-20 | Maximum distinct candidate pairs invoked per request. |
| `MaxDeadlockRetries` | `2` | Integer 0-5 | Global whole-attempt replays after a `1205` deadlock. |
| `RetryDelayMilliseconds` | `50` | Integer 0-1000 | Backoff per deadlock retry (50 ms, 100 ms). |
| `RequestBudgetSeconds` | `20` | Integer 1-120 | Hard total budget across reads/attempts. |
| `CommandTimeoutSeconds` | `10` | Integer 1-60; less than the request budget | `SqlCommand.CommandTimeout` for the procedure call. |

The procedure's `SET LOCK_TIMEOUT 5000` is a SQL-side constant chosen below the command timeout; document it in architecture/setup and keep it synchronized with tests. Add `BookingOptions` binding and a startup validator; invalid values fail startup. The existing `TimeProvider.System` registration supplies the clock. No new package, container, or secret is required. The options do not affect ticket 2 reads.

## Cache impact

**No cache change.** The service-catalog key `scheduler:v1:dealership:{dealershipId:D}:service-types`, `{id,name,durationMinutes}` payload, 10-minute TTL, and invalidation rules remain unchanged. Booking reads `ServiceTypes.DurationMinutes` directly from SQL and never calls the catalog cache; retrieval reads the appointment table. Availability remains uncached. A committed appointment is visible to later availability because it queries SQL. No booking-driven cache invalidation is required. During implementation, record this reason in the ticket records (and optionally a one-line note in [[database/cache|Cache]]); do not add a new cache or key.

## Operational verification

| Check | Method | Expected |
| --- | --- | --- |
| Fresh setup | Empty isolated SQL database; run `--db-setup`; inspect objects/permissions; run a valid procedure/API booking. | Migration applies; mapping/FK/procedure exist; runtime `EXECUTE` works; direct DML denied; booking succeeds. |
| Repeated setup | Run `--db-setup` twice; inspect exact permission rows. | No error; one set of permissions; procedure still works; shifts not duplicated. |
| Ticket 2 upgrade | Apply `InitialCreate` only, insert a synthetic appointment, apply ticket 3 migration, rerun setup; inspect. | Existing appointment preserved; mapping/FK/procedure added; grants/denies exact; booking works. |
| Excess privilege rejection | Add an unexpected object grant/role to `scheduler_app`; rerun setup. | Setup fails with a clear message; no silent acceptance. |
| Down migration | Apply down migration; inspect procedure/FK/table/appointments. | Procedure/FK/table gone; appointments remain; manual runtime revoke required before ticket 2 setup. |
| Runtime procedure boundary | Independent runtime connection calls the procedure without API prechecks. | Overlaps -> `51001`/`51002`; invalid inputs -> `51003`-`51006`; valid -> success; direct DML -> permission denied. |
| Redis outage | Stop Redis; book and retrieve. | Booking/retrieval unaffected. |
| SQL outage | Stop SQL; POST/readiness. | POST `503`; readiness `503`; no confirmation. |
| Restart | Book; restart the API host/container; GET. | Appointment remains retrievable from the SQL volume. |
| Clean checkout | Follow README/setup from a clean checkout; run tests. | Documented commands work with synthetic data and generated future times. |

Record actual commands, revisions, dependency versions, observed results, and unexecuted checks in `docs/test/3-test.md`. Create `docs/deploy-notes/3-deploy-notes.md` for migration, permission, upgrade, rollback, and recovery actions; do not copy ticket requirements into it.

## Documentation and deployment records

| Record | Required update |
| --- | --- |
| `README.md` | Booking status, POST/GET endpoints, booking options, migration/setup changes, actual verification links. |
| `docs/architecture.md` | Procedure contract, error numbers, mapping, locking/limits, DTOs, `409`, cache no-impact, permissions, deployment. |
| `docs/setup-guide.md` | Booking config keys/defaults, migration/grant order, setup/upgrade/recovery commands. |
| `docs/database/schema.md` | `TimeZoneMappings` table/columns/indexes/FK, procedure signature/errors, no appointment index change, runtime grants/denies. |
| `docs/database/overview.md` | Mapping relationship/procedure write path. |
| `docs/database/migration.md` | Ticket 3 migration record, objects, compatibility/backfill (none), recovery, verification. |
| `docs/database/cache.md` | No key/payload/TTL change and reason (booking never reads cache). |
| `docs/README.md` | Ticket 3 research/plan/test/deploy records as they are created. |
| `docs/test/3-test.md` | Requirement coverage and executed evidence. |
| `docs/deploy-notes/3-deploy-notes.md` | Operational steps and execution record. |

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `RuntimePrincipalProvisioner.cs` | Rejects all non-CONNECT/SELECT permissions. | Allow exact procedure `EXECUTE` and appointment DML denies; add grants; keep roles/SID checks. | S3-03, S3-06 |
| Must Change | `DatabaseSetupRunner.cs` | No mapping/procedure validation. | Add `TimeZoneMappingValidator` after migrations; verify procedure existence; keep provisioning order. | S3-02, S3-06 |
| Must Change | New migration/model/snapshot | No mapping/procedure. | Add mapping table/seed/FK and procedure; update EF model/snapshot; verify operation order/upgrade. | S3-03, S3-06 |
| Must Change | `appsettings.json`, `BookingOptions`, validator | No booking config. | Add defaults/ranges; fail startup on invalid values. | S3-03, S3-04 |
| Must Change | `RuntimePermissionTests.cs`, migration/setup tests | No procedure grant/deny/upgrade/repeated-setup coverage. | Add exact permission, repeated, upgrade, excess-privilege, and down-migration checks. | S3-03, S3-06 |
| Must Change | README/architecture/setup/database/cache/docs index/deploy notes/test report | Booking remains planned; no operational records. | Record implemented behavior and actual evidence. | S3-06 |
| No Change | `SchedulerSetupOptions.cs` | Setup connection/password validation; separate identity. | Keep; no new secret. | S3-06 |
| No Change | `DatabaseSetupCommand.cs` | 120-second setup deadline and failure exit. | Keep; new work fits the deadline. | S3-06 |
| No Change | Existing `scheduler_app` SELECT/read tests | Read boundary. | Retain; add `EXECUTE`/DML assertions. | S3-03 |
| No Change | Cache configuration/keys | Catalog cache only. | No cache change. | S3-02, S3-06 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| M3-01 | Decision | How to grant procedure execution without widening runtime access? | Object-level `EXECUTE` only; retain schema `SELECT`; no schema `EXECUTE`/role. | Resolved. |
| M3-02 | Decision | Are explicit DML denies compatible with the procedure? | Yes, under dbo ownership chaining; probe verified procedure insert succeeds and direct DML fails with 229. | Resolved; probed. |
| M3-03 | Decision | How to keep repeated setup idempotent when permissions already exist? | Allow exact ticket 3 rows in pre/post validation; re-grant/re-deny is idempotent. | Resolved. |
| M3-04 | Risk | EF migration order may add the dealership FK before mapping seed rows. | Verify generated migration; reorder or add FK after seed; upgrade test is the gate. | Resolved in design; verify at implementation. |
| M3-05 | Risk | Custom dealership IANA IDs without mappings fail the FK/upgrade. | Document mapping requirement; seeded dealerships covered; add mapping before upgrade for custom data. | Resolved as a compatibility limit. |
| M3-06 | Risk | Ticket 2 provisioner rejects leftover ticket 3 grants after rollback. | Document explicit revoke or destructive reset; test recovery. | Resolved; recovery step. |
| M3-07 | Risk | Procedure missing after manual deletion; setup won't recreate via history. | Setup validator detects missing procedure; restore via migration SQL or recreate DB. | Resolved; documented. |
| M3-08 | Decision | Where do grants live? | Provisioner after user creation; migration creates procedure/mapping. A migration-time grant would fail on a fresh DB before the user exists. | Resolved. |
| M3-09 | Decision | Are new packages/containers required? | No; `Microsoft.Data.SqlClient` is already available; existing SQL/Testcontainers/Compose suffice. | Resolved. |
| M3-10 | Risk | Setup validation could fail on legitimate Windows/IANA differences. | Validate only resolvable IDs and representative DST fixtures; document mapping ownership; fix mapping rather than bypass. | Resolved. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S3-03, AC3-05 | Fresh/repeated/permission tests; procedure `EXECUTE`; direct DML and DDL denied; direct-call conflicts. | SQL Server |
| S3-06, AC3-13 | Fresh/repeated setup, ticket 2 upgrade, excess privilege rejection, down migration, data preservation. | SQL Server + local smoke |
| S3-02, S3-06 | Mapping table/seed/FK, Windows ID/DST validation, procedure execution. | SQL Server |
| AC3-07, AC3-09 | Redis/SQL outage, restart, Compose/clean-checkout commands. | API + SQL + Redis + smoke |
| S3-06, AC3-09 | README/architecture/setup/database/cache/deploy/test records with actual evidence. | Documentation review |

All checks are **Not run**. Detailed test files/commands are in [[research/3/04-verification-and-documentation|Part 4]].
