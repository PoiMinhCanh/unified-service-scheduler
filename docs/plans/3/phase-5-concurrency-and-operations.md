---
title: "3 - Phase 5: Concurrency, Failure Boundaries, Resilience, and Operational Checks"
tags: [plans, service-scheduler, booking, concurrency, resilience]
ticket: 3
phase: 5
status: planned
---

# Phase 5: Concurrency, failure boundaries, resilience, and operational checks

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-03, S3-04, S3-05, S3-06; AC3-04, AC3-06, AC3-07, AC3-08, AC3-12; T-07, T-13, the outage portion of T-14, and the clean-checkout portion of T-16; S3-V04; project P-01, P-04, D-03, D-04.
- Intended outcome: prove the atomic allocation under real concurrent API load, verify the bounded lock/timeout/cancellation/unknown-commit behavior with independent connections, keep booking and retrieval correct during Redis and SQL outages, verify the Compose stack through documented manual checks.
- Exclusions: no new production behavior beyond the Phase 3-4 booking path; no appointment schema/permission change; no cancellation/rescheduling, idempotency, authentication, vehicle-overlap protection, telemetry stack, or CI workflow.

## Dependencies

- Phases 1-4 complete: procedure, protected write boundary, booking service, HTTP contract, and API tests pass.
- Real SQL Server 2022 CU23 and Redis fixtures/Compose services are available.
- The fixed clock and seeded future shifts remain valid; generated slot discovery must handle an expired shift horizon by instructing the operator to rerun setup.
- Research verification passed on 2026-11-01.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-03; T-07, AC3-04, AC3-06 | New `tests/ServiceScheduler.IntegrationTests/Booking/BookingConcurrencyTests.cs` | Use a dedicated SQL database with exactly one active qualified technician, one active bay, the seeded service duration, opening coverage, and an initially empty `Appointments` table. Create two `SchedulerApiFactory` hosts sharing the same runtime/setup database and fixed clock. Release 20 independent HTTP clients from a `TaskCompletionSource`/barrier against the same valid interval. Assert exactly one `201`, nineteen `409`, no `503`/`500`, and exactly one stored appointment with the correct duration/end/status. Assert final state through the setup connection and clean up only the class's rows. | T-07/AC3-04 pass on real SQL Server; the test uses independent factories/connections and a coordination barrier, not arbitrary sleeps; a repeated run remains stable without weakening assertions. | Planned |
| 2 | S3-03, S3-04; T-09, S3-V04, AC3-06, AC3-12 | New `tests/ServiceScheduler.IntegrationTests/Booking/BookingFailureBoundaryTests.cs` and `Fixtures/FakeAppointmentBookingProcedure.cs` | Hold the seeded technician row with `UPDLOCK, HOLDLOCK` in a setup connection and call the procedure directly through `scheduler_app`: assert `1222` after the 5 s lock timeout and no row. With the lock held, use a test `SqlCommand.CommandTimeout = 1 s` and assert `-2` and no row. Cancel a direct call while the lock is held, assert cancellation, release the lock in `finally`, and assert a later valid call succeeds. Separately replace `IAppointmentBookingProcedure` with an integration fake to assert API `503` mapping for `1222`/`-2`/classified dependency/exhausted `1205`, sanitized `500` for `51006` and unexpected exceptions (not `409`), budget expiry -> `503`, and exactly one call for a non-`1205` dependency/unknown-commit result. | Lock timeout, command timeout, cancellation, lock release, dependency mapping, budget, unexpected-error classification, and no-replay policy pass; no required test depends on a shared container being stopped. | Planned |
| 3 | S3-05, S3-06; T-13, T-14, AC3-07 | New `tests/ServiceScheduler.IntegrationTests/Booking/BookingOutageTests.cs` using `CacheSqlServerTestBase`/real Redis | Pause Redis and perform a valid POST plus GET; assert success and SQL-only booking/retrieval; unpause and clean up. For SQL unavailability, configure the factory with an unreachable runtime connection or an isolated stopped-dependency pattern; assert POST `503 dependency_unavailable`, readiness `503`, and no stored appointment counted through the setup connection. Do not stop the shared SQL/Redis container during other tests. | T-13/T-14 outage and AC3-07 pass; Redis has no effect on booking/retrieval; SQL outage prevents confirmation and fails readiness; recovery leaves no partial row. | Not run |
| 6 | S3-04, S3-05; S3-V04 | `tests/ServiceScheduler.IntegrationTests/Startup/ProblemDetailsTests.cs` | Extend booking Problem Details assertions for `503 dependency_unavailable` and sanitized `500 internal_error` (using the real or fake boundary as appropriate), including non-empty `traceId` and absence of stack/credentials/connection details. | Booking failure responses carry the documented code/traceId and expose no sensitive content; unexpected errors are not reported as capacity conflicts. | Planned |
| 7 | S3-06; P-04 | `docs/test/3-test.md`, `docs/deploy-notes/3-deploy-notes.md` | Record Phase 5 commands, results, environment, script outputs, Compose checks, and recovery observations; update the deploy notes with operational execution evidence. | Actual evidence is recorded; failed/blocked checks remain visible; no acceptance criterion is marked Passed from a checklist alone. | Planned |

## Concurrency and failure test design

| Case | Method | Expected |
| --- | --- | --- |
| 20 POSTs, one eligible pair, empty table | Two factories, 20 clients, shared `TaskCompletionSource` barrier, same interval | Exactly one `201`, nineteen `409`, one stored row; no `503`/`500`. |
| Lock wait then fresh overlap check | 20-way race plus direct-call conflict after lock release | Waiting request rechecks and returns `409`, not a duplicate. |
| Technician-only conflict | Direct procedure with a second bay and the same technician | `51001`, no row; API equivalent returns `409`. |
| Bay-only conflict | Direct procedure with a second technician and the same bay | `51002`, no row; API equivalent returns `409`. |
| Adjacent / partial / contained / identical | Direct procedure plus API interval tests | Adjacent succeeds; every overlap conflicts for the relevant resource. |
| Atomic failure / released locks | Count rows before/after a conflict or validation failure; then issue a valid call | Row count unchanged; the later call succeeds. |
| Lock timeout | Hold the technician row; direct runtime call with the procedure's 5 s lock timeout | `1222`; no row; no retry. |
| Command timeout | Hold the lock; direct call with `CommandTimeout = 1 s` | `-2`; no row. |
| Cancellation | Cancel a direct call while the lock is held; release; issue a valid call | Canceled attempt leaves no row; later call succeeds; API cancellation propagates. |
| Deadlock | Fake allocator returns `1205` twice then success; exhausted `1205` -> `503` | Retry the same rolled-back attempt within the global bound; no blind replay beyond it. |
| Unknown commit | Fake boundary returns a non-`1205` dependency failure; assert call count | Exactly one call; `503`; no replay. |
| Budget expiry | Fake boundary advances the fake clock or uses a short test budget; expect `503` and no further calls | No write starts after the request budget is exhausted. |
| Redis outage | Pause Redis; POST/GET; unpause | Booking/retrieval unaffected; SQL authoritative. |
| SQL outage | Unreachable runtime connection or isolated stopped dependency; POST/readiness | POST `503`; readiness `503`; no confirmation row. |
| Restart | Already covered by Phase 4 `AppointmentApiTests`; re-run in the full regression | Appointment retrievable after a new host against the same database. |

Do not use arbitrary sleeps for coordination; use barriers, `TaskCompletionSource`, bounded polling, and cleanup in `finally`. A failed or blocked required check prevents implementation completion; record it instead of weakening the assertion.

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet build --no-restore` and `dotnet format --verify-no-changes`. | Build succeeds; formatting is clean. | Not run |
| T-07 concurrency | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingConcurrency"` | Exactly one `201`, nineteen `409`, one stored row, no `503`/`500`; stable final state. | Not run |
| S3-V04 failure boundaries | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingFailureBoundary"` | Lock/command timeout, cancellation, budget, dependency mapping, no-replay, and unexpected `500` behavior pass. | Not run |
| T-13/T-14 outage | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BookingOutage"` | Redis outage leaves booking/retrieval correct; SQL outage returns `503` and fails readiness with no row. | Not run |
| Booking Problem Details | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ProblemDetails"` | `503`/`500` codes/traceIds and sanitized bodies; no `409` for unexpected errors. | Not run |
| Compose startup | `docker compose config`, `docker compose up -d --wait`, `docker compose logs db-setup`, `docker compose logs api`, health endpoints. | Stack starts from the local configuration; setup applies; liveness/readiness are correct. | Not run |
| Redis outage operation | `docker compose stop redis`, run a valid booking/retrieval, then `docker compose start redis`. | Booking/retrieval succeed during the outage; recovery leaves no corruption. | Not run |
| Full regression | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` and the full integration suite. | All existing and new tests pass; no flaky required check is retried into a pass without evidence. | Not run |

## Exit criteria

- The 20-way concurrency test produces exactly one confirmation, nineteen conflicts, and one stored appointment across two API factories.
- Held-lock, command-timeout, cancellation, budget, unknown-commit, and unexpected-error boundaries are verified; no unsafe write replay and no false `409`.
- Booking/retrieval remain correct during Redis outage; SQL outage prevents confirmation and fails readiness.
- Verify booking and retrieval manually in Swagger UI against the documented Compose environment; concurrency is covered by the existing integration tests.
- `docs/test/3-test.md` and `docs/deploy-notes/3-deploy-notes.md` record actual commands/results and any limitations.
- Phase 6 documentation/clean-checkout work remains; no ticket acceptance is claimed from this phase alone.

## Impacts and remaining decisions

- Database/migrations: none; the procedure and indexes from Phases 1-2 are exercised under concurrency.
- Permissions: unchanged; direct DML remains denied while procedure execution succeeds.
- Cache: none for booking correctness; Redis outage checks prove booking/retrieval independence.
- Configuration: uses the Phase 3 option values; lock/command/budget constants must match the documented architecture.
- Deployment: Compose checks, and outage recovery belong in the deploy notes and setup guide (Phase 6).
- Remaining decisions: no blocking decision. A true lost-commit-acknowledgment fault cannot be injected locally; the no-retry policy is covered by the fake-boundary test plus code inspection, and that limitation must be documented rather than claimed as a real network fault test. The PowerShell version actually used must be recorded.
