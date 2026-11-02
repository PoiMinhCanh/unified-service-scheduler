---
title: "3 - Phase 3: Booking Orchestration, Options, and Procedure Boundary"
tags: [plans, service-scheduler, booking, api, options, retry]
ticket: 3
phase: 3
status: planned
---

# Phase 3: Booking orchestration, options, and procedure boundary

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/01-api-contracts-and-retrieval|API contracts and retrieval]], [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-01, S3-02, S3-03, S3-04; AC3-02, AC3-03, AC3-12; the unit portions of T-02/T-03; S3-V03, S3-V04; project A-01, A-02, A-03, P-01.
- Intended outcome: implement the booking configuration, the candidate planner, the bounded allocator with deadlock retries and failure classification, the SQL procedure boundary, and the service that performs authoritative prechecks, selects bounded candidate pairs, and returns a typed outcome for the future controller.
- Exclusions: no controller, route, HTTP DTO, `Location`, or OpenAPI work (Phase 4); no API integration tests (Phase 4); no concurrency/outage checks (Phase 5); no procedure, migration, or permission change (Phases 1-2); no cache or telemetry change.

## Dependencies

- Phase 1: mapping table/FK and `dbo.CreateAppointment` exist.
- Phase 2: the runtime write boundary, setup validation, and direct procedure invariants are verified with `scheduler_app`.
- Research verification passed on 2026-11-01.
- Existing reusable components: `TimeProvider` registration, `AvailabilityQueries`, `AvailabilityInterval`, `OpeningCoverage`, `StartsAtParser`, `ProblemCodes`/`ProblemResponse` (the 409 code is added in Phase 4), `DatabaseOptions`, and the SQL Server fixture.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-03, S3-04 | `src/ServiceScheduler.Api/Features/Appointments/BookingOptions.cs`, `BookingOptionsValidator.cs`, `src/ServiceScheduler.Api/Program.cs`, `src/ServiceScheduler.Api/appsettings.json` | Bind `Scheduler:Booking` with defaults `MaxCandidateAttempts=5`, `MaxDeadlockRetries=2`, `RetryDelayMilliseconds=50`, `RequestBudgetSeconds=20`, `CommandTimeoutSeconds=10`. Validate integer ranges (`MaxCandidateAttempts` 1-20, `MaxDeadlockRetries` 0-5, `RetryDelayMilliseconds` 0-1000, `RequestBudgetSeconds` 1-120, `CommandTimeoutSeconds` 1-60) and require `CommandTimeoutSeconds < RequestBudgetSeconds`; register `ValidateOnStart` and the validator; do not change ticket 2 cache/database options. | Invalid values fail startup with key-naming, non-secret messages; defaults are valid; unit tests cover defaults, boundaries, and invalid combinations. | Planned |
| 2 | S3-03, S3-04 | New `src/ServiceScheduler.Api/Infrastructure/Persistence/IAppointmentBookingProcedure.cs` and `SqlAppointmentBookingProcedure.cs` | Define the procedure boundary with a typed result/outcome and the four outputs. Implement `dbo.CreateAppointment` through a parameterized `SqlCommand` on a dedicated runtime connection per attempt, with `CommandTimeout` from options, cancellation propagation, and output parameters. Map `SqlException.Number`: `51001`/`51002` conflict, `51003` invalid request, `51004` ineligible resource, `51005` coverage unavailable, `51006` time-zone mapping invalid, `1205` deadlock, `1222` lock timeout, `-2` command timeout, other `SqlFailureClassifier` numbers dependency unavailable. Rethrow unexpected exceptions. Do not open an EF transaction, do not retry inside the boundary, and do not reuse a connection/session across attempts after a failure. | Unit-testable boundary with the documented result mapping; a valid call returns the procedure outputs; known error numbers map without exposing SQL messages; unexpected errors propagate for controlled `500` handling; no blind retry occurs in the boundary. | Planned |
| 3 | S3-03, S3-04; S3-V03 | New `src/ServiceScheduler.Api/Features/Appointments/BookingCandidatePlanner.cs` | Implement the pure planner: technicians in the outer loop and bays in the inner loop, preserving the SQL name/ID order; deduplicate candidate IDs; enumerate distinct pairs; skip a technician after `51001` and a bay after `51002`; skip only the failed pair after `51004`/`51005`; count each distinct pair once toward `MaxCandidateAttempts`; stop on success or exhaustion; return a deterministic result for empty lists and exhausted pairs. | Unit tests prove stable technician-then-bay order, no duplicate pairs, resource skip, pair-only skip, the attempt cap, and exhaustion; the planner has no SQL, cache, or clock dependency. | Planned |
| 4 | S3-03, S3-04; S3-V03, S3-V04 | New `src/ServiceScheduler.Api/Features/Appointments/BookingAllocator.cs` and feature-local `AppointmentBookingOutcome.cs` | Drive planner pairs through `IAppointmentBookingProcedure`. Accept the budget start/deadline captured by the service and, before each procedure attempt/retry, return `DependencyUnavailable` if the total budget is exhausted. Capture a fresh per-attempt `EvaluatedAtUtc` immediately before each procedure call. Retry only `1205`, at most `MaxDeadlockRetries` times globally, with `RetryDelayMilliseconds × (retryIndex + 1)` using `Task.Delay(TimeSpan, TimeProvider, cancellationToken)`; re-execute the same pair. Do not retry `1222`, `-2`, classified connectivity, `51003`, `51006`, or an unknown commit. Map `51003` to validation, `51006` to internal, exhausted conflicts/ineligibility/coverage to capacity, and dependency failures to dependency. Propagate request cancellation; log outcome/attempt context without request bodies, VINs, or customer contact details. | Allocator unit tests with a fake procedure cover first-pair success, `51001`/`51002` resource skips, `51004`/`51005` pair retries, `51003`, `51006`, `1205` retry then success, `1205` exhaustion, `1222`/`-2`/connectivity with exactly one call, budget expiry, request cancellation, unexpected exceptions, and no replay after an unknown commit. | Planned |
| 5 | S3-01, S3-02; AC3-02 | New `src/ServiceScheduler.Api/Features/Appointments/AppointmentService.cs` | Capture the total budget start from the injected `TimeProvider` at service entry and check it before EF reads and before invoking the allocator. Implement the precheck in deterministic order: resolve the dealership + service with `AvailabilityQueries.ServiceLookups` (`404` if absent/cross-dealership); read customer and vehicle (`404` if absent) and reject vehicle/customer mismatch (`400`); validate the start/duration/end with `AvailabilityInterval` and the captured `TimeProvider` instant (`400` for non-future, non-positive duration, or overflow); evaluate one opening row with `OpeningCoverage` and the dealership IANA zone, mapping an invalid zone to internal failure (`409` for no coverage); read ordered technician and bay candidates through `AvailabilityQueries` with `Take(MaxCandidateAttempts)`, deduplicated (`409` when either list is empty); invoke the allocator; return the typed created/failure result with the known IDs and procedure outputs. Never read `ServiceCatalogService`/Redis, never treat availability as a reservation, and never open an EF transaction. | The service compiles and is registered; its validation order and fixed outcomes are testable; no cache call or availability service call exists; invalid or no-capacity paths return without a procedure call where determined by the precheck. | Planned |
| 6 | S3-01-S3-04 | `src/ServiceScheduler.Api/Program.cs` | Register `BookingOptions` binding/validation, `IAppointmentBookingProcedure`/`SqlAppointmentBookingProcedure`, `BookingCandidatePlanner`, `BookingAllocator`, and `AppointmentService` using constructor injection; keep the existing controllers, Problem Details, DbContext, cache, health, and `TimeProvider` registrations unchanged. | Host builds and starts; the booking service graph resolves; existing endpoints and startup tests are unaffected. | Planned |
| 7 | S3-03, S3-04; T-02/T-03/T-10 unit portions; S3-V03, S3-V04 | New `tests/ServiceScheduler.UnitTests/Booking/BookingOptionsValidatorTests.cs`, `BookingCandidatePlannerTests.cs`, `BookingAllocatorTests.cs`, `FakeAppointmentBookingProcedure.cs`; reuse existing `Availability/StartsAtParserTests.cs`, `AvailabilityIntervalTests.cs`, `OpeningCoverageTests.cs`, `ShiftCoverageTests.cs`, `HalfOpenOverlapTests.cs` | Add the unit coverage described in Steps 1, 3, and 4. The fake implements `IAppointmentBookingProcedure`, records the requested pair/evaluation instant, and returns scripted outcomes, errors, delays, or cancellation. Use `FakeTimeProvider` for budget/backoff where deterministic. Reuse the existing scheduling-rule unit tests for the T-10 time semantics and the unit-level T-02/T-03 coverage/overlap predicates; the existing `AvailabilitySqlTests` generated-SQL predicate checks remain integration-level evidence in Phase 4. Do not duplicate these tests, and do not treat them as SQL Server booking evidence. | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Booking"` passes; the existing unit suite remains green. | Planned |
| 8 | S3-06; P-04 | `docs/test/3-test.md` | Extend the test report with Phase 3 commands, results, and unexecuted checks. | Actual commands/results and remaining API/concurrency checks are recorded; no HTTP behavior is claimed. | Planned |

## Booking option contract

| Key | Default | Validation | Purpose |
| --- | --- | --- | --- |
| `Scheduler:Booking:MaxCandidateAttempts` | `5` | Integer 1-20 | Maximum distinct candidate pairs invoked per request. |
| `Scheduler:Booking:MaxDeadlockRetries` | `2` | Integer 0-5 | Global whole-attempt replays after a `1205` deadlock. |
| `Scheduler:Booking:RetryDelayMilliseconds` | `50` | Integer 0-1000 | Backoff per deadlock retry (50 ms, 100 ms). |
| `Scheduler:Booking:RequestBudgetSeconds` | `20` | Integer 1-120 | Total budget across reads and procedure attempts. |
| `Scheduler:Booking:CommandTimeoutSeconds` | `10` | Integer 1-60 and less than the budget | `SqlCommand.CommandTimeout` for each procedure call. |

The procedure's `SET LOCK_TIMEOUT 5000` remains a SQL-side constant below the command timeout; the architecture/setup documentation must keep it synchronized with the configured defaults.

## Validation and candidate order

| Precheck | Source | Failure outcome |
| --- | --- | --- |
| Dealership/service scope | `AvailabilityQueries.ServiceLookups` | `404` for missing/unknown/cross-dealership service. |
| Customer exists | `SchedulerDbContext.Customers` `AsNoTracking` | `404`. |
| Vehicle exists | `SchedulerDbContext.Vehicles` `AsNoTracking` | `404`. |
| Vehicle ownership | Persisted `Vehicle.CustomerId` | `400` validation when it differs from the request customer. |
| Positive SQL duration, future start, end overflow | `AvailabilityInterval` using SQL `DurationMinutes` and `TimeProvider` | `400`. |
| One local opening row covering the full interval | `OpeningCoverage` + `AvailabilityQueries.OpeningStarts` | `409` capacity when no row; internal failure for an invalid persisted IANA zone. |
| Technician candidates | `AvailabilityQueries.TechnicianCandidates`, bounded and deduplicated | `409` when empty. |
| Bay candidates | `AvailabilityQueries.BayCandidates`, bounded and deduplicated | `409` when empty. |
| Pair allocation | Planner + allocator + procedure | `409` when exhausted; `400`/`500`/`503` per the procedure mapping. |

The API precheck is not a reservation. The procedure revalidates every predicate inside its transaction; a precheck success that loses a race becomes `51001`/`51002` and the allocator tries the next bounded pair.

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet build --no-restore` and `dotnet format --verify-no-changes`. | Build succeeds; formatting is clean. | Not run |
| Options validation (S3-03, S3-04) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~BookingOptions"` | Defaults valid; each range boundary and invalid combination fails clearly; command timeout must be below the budget. | Not run |
| Planner (S3-03, S3-04, S3-V03) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~BookingCandidatePlanner"` | Stable order, deduplication, skip behavior, attempt cap, and exhaustion pass. | Not run |
| Existing scheduling-rule unit tests (T-02/T-03/T-10 unit portions) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Availability"` | Existing parser/interval/opening/shift/overlap tests pass; they provide unit-level coverage but do not replace Phase 2/4 real SQL checks. | Not run |
| Allocator (S3-03, S3-04, S3-V03, S3-V04) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~BookingAllocator"` | Success/alternative-pair/deadlock/timeout/budget/cancellation/unknown-commit policy passes with the fake boundary; no blind replay. | Not run |
| Composition root (S3-01-S3-04) | Start the test host with the existing fixtures; resolve `AppointmentService`; verify existing startup/health/OpenAPI tests. | Host builds; booking service resolves; existing endpoints unaffected. | Not run |
| Regression (S3-06) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | All unit tests, including existing availability/error/cache tests, pass. | Not run |

## Exit criteria

- Booking options bind, validate, and fail startup on invalid values.
- The procedure boundary exposes typed outcomes and maps `51001`-`51006`, `1205`, `1222`, `-2`, and classified dependency failures as documented; it never retries internally or reuses a failed session.
- The candidate planner is deterministic, bounded, and free of duplicate pairs.
- The allocator proves alternative-pair, deadlock retry, budget, cancellation, and unknown-commit no-replay behavior with unit tests.
- `AppointmentService` performs the documented prechecks, uses SQL duration, never reads Redis, and is registered with the procedure/allocator/planner.
- `docs/test/3-test.md` records Phase 3 commands/results; no HTTP booking success is claimed yet.

## Impacts and remaining decisions

- Database/migrations: none beyond Phases 1-2.
- Cache: none; the service never calls `ServiceCatalogService` or the cache.
- Configuration: new `Scheduler:Booking` defaults and startup validation; no secret.
- Permissions: unchanged from Phase 2; the API will use `scheduler_app` for procedure execution.
- Deployment: booking options and their relationship to the SQL `LOCK_TIMEOUT` must be documented in Phase 6.
- Remaining decisions: no blocking decision. The service may return typed results rather than throw for expected outcomes; unexpected exceptions must remain available to the global sanitized `500` handler. Exact internal record names may adjust while preserving the fixed configuration keys, outcomes, and procedure contract.
