---
title: "3 - Appointment Booking and Retrieval: Test Report"
tags: [test, service-scheduler, booking, sql, concurrency]
ticket: 3
status: executed-automated
---

# 3 - Appointment booking and retrieval: test report

## Scope and prerequisites

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/00-overview|Ticket 3 research overview]]
- Plan: [[plans/3/README|Ticket 3 implementation plan]]
- Manual checklist: [[test/3-manual-test|Ticket 3 manual tests]] (generated 2026-11-02; all cases Not run)
- Review: [[review/3-review|Ticket 3 review]] (findings R-01-R-04 fixed and re-verified 2026-11-02; acceptance pending)
- Deployment notes: [[deploy-notes/3-deploy-notes|Ticket 3 deployment notes]]
- Requirements and exclusions: S3-01 through S3-06, AC3-01 through AC3-13, T-01 through T-14 and T-16, and S3-V01 through S3-V06. Authentication, cancellation/rescheduling, idempotent POST, vehicle-overlap protection, telemetry infrastructure, and a custom UI are excluded.
- Reviewed revision: working tree on HEAD `90a6284`; ticket 3 implementation is uncommitted.
- Environment: Windows 11, .NET SDK `10.0.403`, Docker `25.0.3`, Docker Compose `v2.24.6-desktop.1`, xUnit v3, WebApplicationFactory, Testcontainers.MsSql/Redis, SQL Server 2022 CU23 image, Redis `7.4.12-alpine`.
- Configuration source: ignored local `.env` for Compose interpolation and test factory in-memory overrides; no secrets are recorded here.
- Migration/seed baseline: ticket 2 `20261031092016_InitialCreate` plus ticket 3 `20261101161530_AddBookingProcedureAndTimeZoneMappings`; synthetic seeded dealership/customer/vehicle/service/technician/bay data.
- Test projects: `tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj` and `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj`; working directory is the repository root.
- Setup and cleanup: migrations/seeds/probes use the privileged setup identity; runtime behavior uses `scheduler_app`; test classes own their databases and delete only their own probe rows.

> [!IMPORTANT]
> Automated and operational checks passed on 2026-11-01/2026-11-02. The generated manual checklist has been created (all cases Not run) but has not been executed by a human; ticket acceptance is not claimed from this report alone. Review findings R-01-R-04 were fixed and re-verified on 2026-11-02. A successful build or OpenAPI document does not substitute for the executed real-dependency checks below.

## Requirement coverage

| Requirement ID | Scenario IDs | Level | Executed test files / evidence | Status |
| --- | --- | --- | --- | --- |
| S3-01 | T-01, T-10, T-11, S3-V01 | API + SQL Server | `AppointmentApiTests`, `AppointmentValidationTests`, `AppointmentDstTests`, `OpenApiTests`, `ProblemDetailsTests` | Passed |
| S3-02 | T-02-T-06, S3-V02, S3-V05 | Unit + SQL Server | `BookingProcedureTests`, `AppointmentEligibilityTests`, `AppointmentIntervalTests`, `AppointmentDurationTests`, `AppointmentDstTests`, scheduling-rule unit tests | Passed |
| S3-03 | T-04-T-09, S3-V03, S3-V06 | SQL Server + API | `BookingProcedureTests`, `BookingConcurrencyTests`, `BookingSetupTests`, `BookingMigrationTests`, `BookingMigrationUpgradeTests`, `AppointmentIntervalTests` alternative pair | Passed |
| S3-04 | S3-V04 | Unit + API + SQL | `BookingAllocatorTests`, `BookingFailureBoundaryTests` | Passed |
| S3-05 | T-01, T-11, T-13, T-14, S3-V01 | API + SQL + Redis | `AppointmentApiTests`, `AppointmentDurationTests`, `BookingOutageTests` | Passed |
| S3-06 | T-08, T-13, T-14, T-16, S3-V06 | SQL + operations + docs | Migration/setup/permission/outage/operational/clean-directory checks | Passed |

## Acceptance mapping

| Acceptance label | Automated/operational evidence | Status |
| --- | --- | --- |
| AC3-01 | Valid POST/GET and restart retrieval in `AppointmentApiTests` | Passed automated/operational; manual pending |
| AC3-02 | Validation, eligibility, direct procedure, and interval checks | Passed automated/operational; manual pending |
| AC3-03 | Adjacent/overlap and resource-only conflict checks | Passed automated/operational; manual pending |
| AC3-04 | 20-way concurrency test: one `201`, nineteen `409`, one row | Passed automated/operational; manual pending |
| AC3-05 | Runtime procedure execution and denied direct DML | Passed automated/operational; manual pending |
| AC3-06 | Empty table, lock wait/fresh check, rollback/released locks, timeouts | Passed automated/operational; manual pending |
| AC3-07 | Redis outage booking/retrieval; SQL outage and readiness | Passed automated/operational; manual pending |
| AC3-08 | Executable smoke/concurrency checks with seeded IDs and generated times; the harnesses were later removed from the implementation | Passed historically; not reproducible from this checkout; manual pending |
| AC3-09 | Documented startup/demo from a clean directory and fresh volume | Passed operational check; separate git clone not used because implementation is uncommitted; manual pending |
| AC3-10 | DTO/Location/UTC/Problem Details contract tests and OpenAPI | Passed automated/operational; manual pending |
| AC3-11 | Stale cache, duration snapshot, and availability visibility checks | Passed automated/operational; manual pending |
| AC3-12 | Alternative pair, cancellation, budget, and unknown-commit no-replay | Passed automated/operational; manual pending |
| AC3-13 | Fresh/repeated/ticket 2-upgrade setup, permissions, down/recovery | Passed automated/operational; manual pending |

## Verification case matrix

| Case | Executed evidence | Status |
| --- | --- | --- |
| T-01 Valid future booking | `AppointmentApiTests` | Passed |
| T-02 Qualification or inactive resource | `BookingProcedureTests`, `AppointmentEligibilityTests`, scheduling unit tests | Passed |
| T-03 Partial shift/opening coverage | `BookingProcedureTests`, `AppointmentEligibilityTests` | Passed |
| T-04 Interval boundaries | `BookingProcedureTests`, `AppointmentIntervalTests` | Passed |
| T-05 Technician conflict only | `BookingProcedureTests` (`51001`), `AppointmentIntervalTests` | Passed |
| T-06 Bay conflict only | `BookingProcedureTests` (`51002`), `AppointmentIntervalTests` | Passed |
| T-07 Concurrent competition | `BookingConcurrencyTests` | Passed |
| T-08 Database write boundary | `RuntimePermissionTests`, `BookingProcedureTests` | Passed |
| T-09 Atomic failure | `BookingProcedureTests`, `BookingFailureBoundaryTests` | Passed |
| T-10 Time handling | `AppointmentValidationTests`, `AppointmentDstTests`, scheduling unit tests | Passed |
| T-11 Relationship and scope | `AppointmentValidationTests`, `AppointmentApiTests`, `BookingProcedureTests` | Passed |
| T-13 Booking during Redis outage | `BookingOutageTests`, Compose smoke | Passed |
| T-14 Restart and database outage | `AppointmentApiTests` restart, `BookingOutageTests`, Compose checks | Passed |
| T-16 Booking clean checkout | Clean-directory copy without build artifacts, fresh Compose project/volume, documented health/operational checks | Passed with limitation: separate git clone not run because the implementation is uncommitted; the smoke/concurrency harnesses used for this run were later removed |
| S3-V01 API contract | `AppointmentApiTests`, `AppointmentValidationTests`, `OpenApiTests`, `ProblemDetailsTests` | Passed |
| S3-V02 Duration and read compatibility | `AppointmentDurationTests` | Passed |
| S3-V03 Alternative candidates | `BookingAllocatorTests`, `AppointmentIntervalTests` | Passed |
| S3-V04 Failure boundaries | `BookingAllocatorTests`, `BookingFailureBoundaryTests` | Passed |
| S3-V05 Direct-call validation parity | `BookingProcedureTests`, `AppointmentDstTests` | Passed |
| S3-V06 Setup and recovery | `BookingMigrationTests`, `BookingSetupTests`, `BookingMigrationUpgradeTests`, `RuntimePermissionTests` | Passed |

## Automated execution record

| Date / time and zone | Revision | Command | Observed result | Status |
| --- | --- | --- | --- | --- |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | `dotnet restore`; `dotnet build --no-restore` | Build succeeded; 0 warnings, 0 errors | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | `dotnet tool restore`; `dotnet ef migrations add AddBookingProcedureAndTimeZoneMappings ...` | Generated `20261101161530_AddBookingProcedureAndTimeZoneMappings` | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | `dotnet ef migrations script ... --no-build` | Order: create `TimeZoneMappings` -> seed rows -> unique index -> dealership FK -> create procedure; no appointment schema change | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `BookingMigration|MigrationAndSeed` | 5 passed, 0 failed, 13 s | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `RuntimePermission` | 8 passed, 0 failed, 7 s | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `BookingProcedure` | 12 passed, 0 failed, 10 s | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `BookingSetup|BookingMigrationUpgrade` | 6 passed, 0 failed, 13 s | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Unit filter `Booking` | 35 passed, 0 failed, 165 ms | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentApi` | 3 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentValidation` | 10 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentEligibility` | 5 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentInterval` | 4 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentDst` | 3 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `AppointmentDuration` | 2 passed, 0 failed | Passed |
| 2026-11-01 23:20 SEAST | `90a6284` + working tree | Integration filter `OpenApi|ProblemDetails` | 8 passed, 0 failed | Passed |
| 2026-11-02 00:00 SEAST | `90a6284` + working tree | Integration filter `BookingConcurrency` | 1 passed, 0 failed, 7 s; one `201`, nineteen `409`, one stored row | Passed |
| 2026-11-02 00:00 SEAST | `90a6284` + working tree | Integration filter `BookingFailureBoundary` | 10 passed, 0 failed, 13 s; lock timeout `1222`, command timeout `-2`, cancellation, deadlock/budget/no-replay mappings | Passed |
| 2026-11-02 00:00 SEAST | `90a6284` + working tree | Integration filter `BookingOutage` | 2 passed, 0 failed, 7 s; Redis outage booking/retrieval and SQL outage/readiness | Passed |
| 2026-11-02 00:10 SEAST | `90a6284` + working tree | `dotnet test tests/ServiceScheduler.UnitTests/... --no-build` | 133 passed, 0 failed | Passed |
| 2026-11-02 00:12 SEAST | `90a6284` + working tree | `dotnet test tests/ServiceScheduler.IntegrationTests/... --no-build` | 154 passed, 0 failed, 1 m 53 s | Passed |
| 2026-11-02 00:40 SEAST | `90a6284` + working tree | `dotnet format --verify-no-changes` | No formatting changes required | Passed |
| 2026-11-02 (re-review) | `ad774fb` + review fixes | `dotnet build --no-restore` | Succeeded; 0 warnings, 0 errors | Passed |
| 2026-11-02 (re-review) | `ad774fb` + review fixes | `dotnet test tests/ServiceScheduler.UnitTests/... --no-build` | 135 passed, 0 failed, 0 skipped | Passed |
| 2026-11-02 (re-review) | `ad774fb` + review fixes | `dotnet test tests/ServiceScheduler.IntegrationTests/... --no-build` | 157 passed, 0 failed, 0 skipped, 1 m 50 s | Passed; real SQL Server 2022 CU23 and Redis Testcontainers |
| 2026-11-02 (re-review) | `ad774fb` + review fixes | `dotnet format --verify-no-changes` | No formatting changes required | Passed |

## Operational execution record

| Environment | Date / time and zone | Actions and observed results | Status |
| --- | --- | --- | --- |
| Local fresh Compose project `scheduler-t3`; SDK `10.0.403`; SQL Server 2022 CU23; Redis `7.4.12-alpine` | 2026-11-02 SEAST | `docker compose -p scheduler-t3 up -d --build --wait` built API/setup images; both migrations applied; `db-setup` logged mapping/procedure validation, runtime provisioning, and shift seeding; containers healthy. `/health/live` and `/health/ready` returned `200` with database/redis Healthy. | Passed |
| Same stack | 2026-11-02 SEAST | The former PowerShell smoke check (removed from the implementation) returned `201`, followed `Location`, and verified GET equality (appointment `e433be6e-2d42-483b-a315-ae02710e9893`). | Passed |
| Same stack | 2026-11-02 SEAST | The former PowerShell concurrency check (removed from the implementation) reported 1 `201`, 19 `409`, 0 unexpected for slot `2026-11-03T09:00:00Z`. | Passed |
| Same stack | 2026-11-02 SEAST | `docker compose -p scheduler-t3 stop redis`; readiness remained `200`; smoke booking/retrieval passed (appointment `e82bf45f-6a86-4f05-a008-61dcce8114bb`); Redis restarted. | Passed |
| Same stack | 2026-11-02 SEAST | `docker compose -p scheduler-t3 restart api`; liveness `200`; the prior appointment still returned `200` through GET. | Passed |
| Same stack | 2026-11-02 SEAST | `docker compose -p scheduler-t3 stop sqlserver`; POST returned `503`, readiness returned `503`; SQL restarted; readiness returned `200` after a short warm-up (first immediate check `503`, after ~8 s `200`). | Passed |
| Clean-directory copy without `.git`/build artifacts; fresh Compose project `scheduler-t3-clean` and fresh named volume | 2026-11-02 SEAST | `docker compose -p scheduler-t3-clean up -d --build --wait` started a fresh stack; liveness `200`; readiness initially `503` during warm-up, then `200` Healthy; the former smoke and concurrency harnesses (since removed) passed; concurrency reported 1 `201`, 19 `409`, 0 unexpected. | Passed with clean-clone limitation; harnesses not reproducible from this checkout |
| Fresh stack SQL inspection | 2026-11-02 SEAST | `sys.database_permissions` for `dbo.CreateAppointment` showed only the explicit `scheduler_app` `EXECUTE` `GRANT`; no public/role-derived procedure execution row was present. | Passed |

## Issues and follow-up

- One ticket 1 availability failure test inserted a dealership with an unmapped IANA ID, which the new dealership FK correctly rejected. The test now inserts a synthetic `TimeZoneMappings` row, asserts the sanitized `500`, and cleans up the mapping. This preserves the original invalid-time-zone intent without weakening the FK.
- SqlClient reports a canceled lock wait as a `SqlException` ("Operation cancelled by user") in some paths. The production procedure boundary and test invoker now convert a SQL exception observed with a canceled request token into `OperationCanceledException`, so request cancellation is not misclassified as an unexpected `500`; the direct cancellation test passes and the later valid call succeeds.
- No required automated or operational failures remain.

## Not run and limitations

- The human manual checklist is generated at [[test/3-manual-test|the ticket 3 manual test checklist]] (2026-11-02; all cases Not run); human execution and ticket acceptance remain pending.
- Interactive Swagger UI/manual browser inspection was not executed; OpenAPI document assertions are automated.
- A separate fresh `git clone` was not used because the ticket 3 implementation is uncommitted. The clean-directory run copied the working tree without `.git` or `bin`/`obj` artifacts and used a fresh Compose project/volume; this is recorded as a limitation on the literal clean-checkout wording.
- A true lost-commit-acknowledgment fault cannot be injected locally. S3-V04 uses the fake-boundary no-replay test plus code inspection; this is a documented limitation.
- Execution-plan capture on representative volume was not run; the existing appointment resource indexes were verified structurally and through the real procedure/concurrency tests.
- The pre-existing `unified-service-scheduler` SQL volume used a different local SA password than the current `.env`; the operational run used a separate Compose project and fresh volume and did not delete that old volume.
