---
title: "3 - Appointment Booking and Retrieval: Deployment Notes"
tags: [deploy-notes, service-scheduler, booking, sql, permissions]
ticket: 3
status: executed-local
---

# Ticket 3 - Deployment notes

## Scope and references

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Change summary: add appointment confirmation/retrieval, the same-owner `dbo.CreateAppointment` procedure, the IANA-to-Windows time-zone mapping and dealership FK, exact runtime `EXECUTE`/appointment-DML-deny permissions, setup validation, booking options, concurrency/failure handling, OpenAPI/Problem Details updates.
- Implementation revision / artifact: working tree on HEAD `90a6284`; local Compose images built from `src/ServiceScheduler.Api/Dockerfile`.
- Related records: [[plans/3/README|Implementation plan]], [[test/3-test|Test report]], [[database/schema|Schema]], [[database/migration|Migrations]], [[database/cache|Cache]], [[architecture]], [[setup-guide]].

## Prerequisites and deployment order

- Target environment: isolated local demo only; synthetic data; no authentication.
- Required services: SQL Server 2022 CU23 (pinned image), Redis `7.4.12-alpine` (pinned digest), the API, and the one-shot `db-setup`.
- Required configuration: `ConnectionStrings:Runtime`, `ConnectionStrings:Setup`, `Scheduler:RuntimePassword`, `ConnectionStrings:Cache`, the non-secret `Scheduler:Cache:*` defaults, and the non-secret `Scheduler:Booking:*` defaults. Secrets stay in the ignored local `.env` or shell environment.
- Deployment order: SQL Server healthy -> `db-setup` completes migrations -> time-zone/procedure validation -> runtime user/grants -> future shifts -> API starts. The API does not depend on Redis health.
- Database compatibility: additive. The ticket 2 database keeps its appointments; the new mapping rows match the seeded dealership IANA IDs. A custom dealership with an unmapped IANA ID must receive a mapping row before upgrade. No appointment backfill or rewrite.
- Downtime/backup: local demo only; no production migration or backup procedure is claimed.

## Deployment steps

1. **Database migration:** Run the normal setup path (`--db-setup`) or `dotnet ef database update` against the target local database. The ticket 3 migration is `20261101161530_AddBookingProcedureAndTimeZoneMappings`; it creates `dbo.TimeZoneMappings`, inserts `Asia/Ho_Chi_Minh -> SE Asia Standard Time` and `Europe/London -> GMT Standard Time`, adds `FK_Dealerships_TimeZoneMappings_TimeZoneId`, and creates `dbo.CreateAppointment`. Verify the generated script order with `dotnet ef migrations script`.
2. **Setup validation and permissions:** `--db-setup` runs mapping/procedure validation after migrations, then provisions `scheduler_app` with `CONNECT`, schema `dbo` `SELECT`, object `EXECUTE` on `dbo.CreateAppointment`, and object `INSERT`/`UPDATE`/`DELETE` `DENY` on `dbo.Appointments`. A missing procedure, missing dealership mapping, invalid Windows ID, or .NET/SQL DST disagreement fails setup. Repeated setup is idempotent. Do not add schema `EXECUTE`, direct write grants, DDL, or roles.
3. **Application/configuration:** Deploy the API artifact with `ConnectionStrings:Runtime` and `ConnectionStrings:Cache`. Booking defaults are in `appsettings.json`: `MaxCandidateAttempts=5`, `MaxDeadlockRetries=2`, `RetryDelayMilliseconds=50`, `RequestBudgetSeconds=20`, `CommandTimeoutSeconds=10`; invalid values fail startup. The procedure's SQL `LOCK_TIMEOUT` is 5 seconds, below the 10-second command timeout. No new package or container is required.
4. **Cache:** No cache change, no key/payload/TTL/invalidation action. Booking reads SQL duration and never the catalog cache; retrieval reads the appointment table; availability remains uncached and sees committed appointments.
5. **OpenAPI/Problem Details:** Deploy with the implemented `capacity_unavailable` code and the POST/GET operation documentation. Known procedure errors are mapped before the global SQL exception handler; unexpected errors remain sanitized `500`.

## Post-deployment verification

- [x] A fresh isolated Compose project/volume applied both migrations, validated the mapping/procedure, provisioned the exact runtime permissions, seeded shifts, and started a healthy API (`/health/live` and `/health/ready` `200`).
- [x] Booking and retrieval succeeded while Redis was stopped; readiness remained `200`, and recovery restored Healthy readiness.
- [x] During a SQL Server stop, POST returned `503 dependency_unavailable` and readiness returned `503`; after restart, readiness recovered to `200` (observed after a short warm-up) with no confirmation.
- [x] An API restart preserved the previously booked appointment and returned it through GET.
- [x] Full suites passed: 133 unit and 154 integration tests, including real SQL Server procedure/locking/permission/setup/upgrade/concurrency/timeout/outage checks. The 2026-11-02 re-review reran 135 unit and 157 integration tests after the retry, budget, and ownership-validation fixes.
- [x] Setup rejects runtime database/object ownership in addition to unexpected permissions and role membership. The re-review integration tests apply and revoke `ALTER AUTHORIZATION` for the database, `dbo.Appointments`, and `dbo.CreateAppointment`; SQL Server rejects altering `dbo` schema ownership, so that branch is code-inspected.
- [x] A clean-directory copy without build artifacts and a fresh Compose project/volume started and passed the former smoke/concurrency harnesses (since removed from the implementation); a separate git clone was not used because the implementation is uncommitted.
- [ ] Human execution of the generated ticket 3 manual checklist remains pending; acceptance is not claimed from the automated evidence alone.

Observed commands and results are recorded in [[test/3-test|the ticket 3 test report]].

## Rollback or forward recovery

- **Down migration:** Apply the ticket 3 down migration. It drops `dbo.CreateAppointment`, the dealership FK, and `dbo.TimeZoneMappings`; existing appointments remain. The procedure `EXECUTE` grant disappears with the procedure.
- **Runtime permissions after a down migration:** The appointment DML `DENY` rows reference the surviving `dbo.Appointments` table and may remain. Revoke them before running a ticket 2-style provisioner:
  `REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments FROM [scheduler_app];`
  Alternatively, recreate the local database. Do not add direct write grants.
- **Failed validation after migration:** Fix the mapping/procedure/grant state and rerun `--db-setup`; migrations are transactional and repeated setup is idempotent. Do not delete migration history.
- **Missing procedure after manual deletion:** EF migration history will not recreate it automatically. Reapply the migration SQL or recreate the local database; setup validation should fail clearly rather than allowing booking.
- **Application rollback:** Redeploy the previous API artifact. Existing appointments remain in SQL but the previous version has no booking endpoints. Remove the booking configuration only if the target version does not understand it.
- **Destructive local reset:** `docker compose down -v` removes all local synthetic data, including appointments; use only for an explicitly confirmed isolated local environment.
- **Observed local environment note:** The pre-existing `unified-service-scheduler` named volume used a different local SA password than the current `.env`; the operational run used a separate Compose project (`scheduler-t3`) and fresh volume instead of deleting that volume. No secrets are recorded here.

## Execution record

| Environment | Date / time and zone | Revision | Actions and observed results | Evidence / follow-up |
| --- | --- | --- | --- | --- |
| Local Windows 11; Docker `25.0.3`; Compose `v2.24.6-desktop.1`; SDK `10.0.403`; SQL Server 2022 CU23; Redis `7.4.12-alpine` | 2026-11-02 (local) | HEAD `90a6284` + ticket 3 working tree | `docker compose -p scheduler-t3 up -d --build --wait` built the API/setup images, applied both migrations, logged mapping/procedure validation and runtime provisioning, seeded shifts, and reported healthy containers. `/health/live` and `/health/ready` returned `200`. The former smoke and concurrency harnesses (since removed) passed. Redis stop: booking/retrieval passed and readiness stayed `200`; SQL stop: POST and readiness returned `503`, then readiness returned `200` after recovery. API restart preserved retrieval (`200`). | [[test/3-test|Test report]] execution record; no human checklist execution yet. |
