---
title: "3 - Phase 4: Appointment HTTP Contract, Retrieval, and API Verification"
tags: [plans, service-scheduler, booking, api, openapi, retrieval]
ticket: 3
phase: 4
status: planned
---

# Phase 4: Appointment HTTP contract, retrieval, and API verification

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/01-api-contracts-and-retrieval|API contracts and retrieval]], [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-01, S3-02, S3-05; AC3-01, AC3-02, AC3-03, AC3-10, AC3-11; T-01 through T-06, T-10, T-11, the restart portion of T-14; S3-V01, S3-V02, S3-V03, and the API portions of S3-V05; project A-01, A-02, A-03, D-01, D-02.
- Intended outcome: expose `POST /api/dealerships/{dealershipId}/appointments` and `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}`, map the documented outcomes to Problem Details with `capacity_unavailable`, return the complete persisted response and `Location`, document both operations in OpenAPI, and verify the contract against real SQL Server and Redis.
- Exclusions: no concurrency, held-lock, command-timeout, cancellation, budget, outage, or script work (Phase 5); no procedure/migration/permission changes (Phases 1-2); no cache or telemetry change; no appointment cancellation/rescheduling, idempotency, authentication, or vehicle-overlap protection.

## Dependencies

- Phase 3: options, planner, allocator, procedure boundary, and `AppointmentService` exist and pass unit tests.
- Phase 2: the procedure is executable by `scheduler_app` and direct DML is denied.
- Research verification passed on 2026-11-01.
- Existing test infrastructure: `SqlServerTestBase` for real SQL classes, `CacheSqlServerTestBase` for Redis-dependent classes, fixed clock `2026-11-02T00:00:00Z`, seeded Saigon/London IDs, and setup/runtime connection strings.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-01, S3-05 | New `src/ServiceScheduler.Api/Features/Appointments/Contracts/CreateAppointmentRequest.cs` and `AppointmentResponse.cs` | Define the request with nullable `Guid?` customer/vehicle/service IDs and `string? startsAt`; mark required properties for OpenAPI while retaining explicit null/empty checks. Define one response record used by POST and GET with `id`, dealership/customer/vehicle/service/technician/bay IDs, `startsAt`, `endsAt`, `durationMinutes`, `status`, and `createdAt`; use UTC `DateTime` fields projected from `DateTimeOffset.UtcDateTime` so JSON emits `Z`. | The DTOs serialize with camelCase and the documented property names; missing IDs cannot silently bind to `Guid.Empty`; no raw `DateTimeOffset` body binding is used for `startsAt`. | Planned |
| 2 | S3-01, S3-02, S3-05 | New `src/ServiceScheduler.Api/Features/Appointments/AppointmentsController.cs` and the `AppointmentService` result mapping | Add POST and GET actions on `api/dealerships/{dealershipId}/appointments`. Bind route GUIDs without a `:guid` route constraint so malformed values reach model binding and return `400 validation_error`. POST validates required IDs and parses `startsAt` through `StartsAtParser`, calls `AppointmentService`, maps created/capacity/validation/not-found/dependency/internal outcomes through `ProblemResponse.Create`, and returns `201 Created` with `CreatedAtAction(nameof(GetById), new { dealershipId, appointmentId }, response)`. GET queries `Appointments` with `AsNoTracking()` filtered by both `Id` and `DealershipId`, projects stored `StartUtc`/`EndUtc`/`DurationMinutes`/`Status`/`CreatedUtc` with `.UtcDateTime`, and returns `404 not_found` when no row matches. | Valid POST returns `201`, `Location`, and the complete response; GET follows `Location` and returns the same persisted values; missing/other-dealership appointments return `404`; malformed body/route values return `400`; `409 capacity_unavailable` is returned for valid no-capacity requests; no Redis call or write occurs on GET. | Planned |
| 3 | S3-01, S3-04, S3-05 | `src/ServiceScheduler.Api/Infrastructure/Errors/ProblemCodes.cs`, `src/ServiceScheduler.Api/Infrastructure/OpenApi/ProblemDetailsSchemaTransformer.cs`, `src/ServiceScheduler.Api/Infrastructure/OpenApi/ApiOperationTransformer.cs` | Add `capacity_unavailable` and map `StatusCodes.Status409Conflict` in `ProblemCodes.ForStatus`; update the schema code description to list `validation_error`, `not_found`, `capacity_unavailable`, `dependency_unavailable`, and `internal_error`. Add POST/GET summaries, parameter descriptions, success descriptions (including `Location` and stored-snapshot semantics), status sets (`201`/`400`/`404`/`409`/`503`/`500` for POST; `200`/`400`/`404`/`503`/`500` for GET), and bounded-allocation wording. Add `[ProducesResponseType]` attributes where useful for schemas. | `409` responses carry `code=capacity_unavailable` and `traceId`; OpenAPI documents both paths, request/response schemas, statuses, `Location`, and the conflict description; existing ticket 2 operation documentation remains correct. | Planned |
| 4 | S3-01, S3-05; T-01, T-11, T-14 restart; S3-V01 | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentApiTests.cs` | Use a real SQL database and `SchedulerApiFactory` with the fixed clock. POST the seeded Saigon customer/vehicle/oil-change service at `2026-11-03T03:00:00Z`; assert `201`, a followable `Location`, all response fields, `Z` timestamps, duration 60, end 04:00Z, status `Confirmed`, and a persisted row. Follow `Location` and assert GET equality. POST the same interval again and assert it is a new attempt (one pair, second request `409`, still one row). GET from the other dealership and an unknown ID; assert `404`. Dispose the factory, create a new factory against the same database, and assert the appointment remains retrievable. | T-01/T-11/T-14 restart and S3-V01 pass; POST and GET return identical persisted associations; no duplicate row from the repeated request; restart retrieval works without Redis. | Planned |
| 5 | S3-01, S3-02; T-10, T-11, S3-V01 | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentValidationTests.cs` | Cover missing/malformed body, missing/empty/malformed IDs, missing/unparseable/offset-free/date-only `startsAt`, past and equal-to-clock starts, checked-end overflow, unknown dealership/customer/vehicle/service, cross-dealership service, vehicle/customer mismatch, and malformed route GUIDs. Assert the documented `400 validation_error`/`404 not_found` status and `code`/`traceId`, and assert appointment count is unchanged after every failure. | T-10/T-11 and the validation portion of S3-V01 pass with no unintended writes; each failure has a non-empty trace ID and no stack/credentials. | Planned |
| 6 | S3-02; T-02, T-03 | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentEligibilityTests.cs` | Use setup-identity probes to deactivate or unqualify the technician, deactivate the bay, restrict the shift, and restrict the opening row; restore each probe in `finally`. Assert a valid request returns `409 capacity_unavailable` with no row when no eligible pair remains. | T-02/T-03 pass; inactive/unqualified resources and partial shift/opening coverage never produce a confirmation; no probe data leaks across tests. | Planned |
| 7 | S3-02, S3-03; T-04-T-06, AC3-03; S3-V03 | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentIntervalTests.cs` | Insert a synthetic confirmed appointment for the seeded technician/bay, then exercise adjacent-before, adjacent-after, partial overlap, containment, and identical intervals through the API. Add a second bay for a technician-only scenario and a second qualified technician with a shift for a bay-only scenario. Add an alternative-pair case with a second active qualified technician and second active bay ordered after the seeded pair; block the first ordered pair with a conflicting appointment and assert the API selects the second pair and returns `201`. Assert `201` for adjacent and `409 capacity_unavailable` for every overlap, with the stored row count enforced. | T-04-T-06 and S3-V03 pass through the API; the precheck excludes obvious conflicts and the procedure remains authoritative; alternative-pair selection succeeds when the first pair is blocked; adjacent intervals succeed; no partial rows. | Planned |
| 8 | S3-02; T-10, S3-V05 API portion | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentDstTests.cs` | Reproduce the London spring-forward/fall-back API fixtures and the Saigon equivalent-offset request (`2026-11-03T10:00:00+07:00` versus `03:00:00Z`): add Sunday opening/shift probes, assert the documented local coverage/end-boundary outcome, and compare API results with direct procedure results from Phase 2. | T-10 and the API portion of S3-V05 pass; equivalent explicit offsets identify the same instant; local opening boundaries and DST policy agree between .NET and SQL. | Planned |
| 9 | S3-02, S3-05; AC3-11, S3-V02 | New `tests/ServiceScheduler.IntegrationTests/Booking/AppointmentDurationTests.cs` using `CacheSqlServerTestBase`/real Redis | Prime the catalog cache with one duration, change `ServiceTypes.DurationMinutes` through the setup identity, and POST: assert the SQL duration/end snapshot is used. After booking, change the service duration again and assert GET preserves the stored snapshot while a new uncached availability read uses the authoritative SQL duration. Assert the committed appointment is visible to availability. Restore the probe data in `finally`. | S3-V02 and AC3-11 pass; a stale catalog cannot change booking duration; later catalog changes do not alter GET; availability sees the committed row; Redis is never consulted by booking. | Planned |
| 10 | S3-01, S3-06; S3-V01 | `tests/ServiceScheduler.IntegrationTests/OpenApi/OpenApiTests.cs`, `Startup/ProblemDetailsTests.cs` | Extend OpenAPI assertions for both appointment paths, request/response schemas, status sets, `capacity_unavailable`, and `Location`; add booking `400`/`404`/`409` Problem Details assertions with `code`/`traceId` and no sensitive content. Leave booking `503`/`500` failure-path assertions to Phase 5. | OpenAPI and Problem Details tests pass; the documented contract is stable and observable. | Planned |
| 11 | S3-06; P-04 | `docs/test/3-test.md` | Record Phase 4 commands, results, environment, and remaining concurrency/outage/script checks. | Actual evidence is recorded; acceptance criteria that depend on Phases 5-6 remain Not run. | Planned |

## API contract summary

| Operation | Route | Success | Failure statuses |
| --- | --- | --- | --- |
| Create appointment | `POST /api/dealerships/{dealershipId}/appointments` | `201 Created` with `Location` and `AppointmentResponse`. | `400 validation_error`; `404 not_found`; `409 capacity_unavailable`; `503 dependency_unavailable`; `500 internal_error`. |
| Retrieve appointment | `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}` | `200` with `AppointmentResponse` using stored values. | `400 validation_error` for malformed route GUIDs; `404 not_found`; `503 dependency_unavailable`; `500 internal_error`; no `409`. |

| Validation condition | HTTP result | Code |
| --- | --- | --- |
| Missing/malformed body, missing required property, malformed UUID, unparseable/offset-free/date-only `startsAt` | `400` | `validation_error` |
| `startsAt <= evaluatedAt`, past/equal start, non-positive/invalid duration, end overflow | `400` | `validation_error` |
| Customer/vehicle exists but ownership mismatches | `400` | `validation_error` |
| Malformed `dealershipId`/`appointmentId` route GUID | `400` | `validation_error` |
| Unknown dealership/customer/vehicle/service or cross-dealership service | `404` | `not_found` |
| Valid request with no eligible pair under the bounded policy, including opening/shift coverage | `409` | `capacity_unavailable` |
| Classified SQL dependency/timeout/deadlock/lock failure (including exhausted retries) | `503` | `dependency_unavailable` |
| Unexpected failure, including an invalid runtime time-zone mapping | `500` | `internal_error` |
| GET appointment missing or from another dealership | `404` | `not_found` |

The POST and GET responses use the same property names; `endsAt` and `durationMinutes` are the procedure/row snapshot, never recomputed from the current catalog. All response timestamps serialize with `Z`. `Location` must be followable to the GET route; the absolute/relative URL form is not contractual.

## Test data and identity design

| Item | Value |
| --- | --- |
| Fixed clock | `2026-11-02T00:00:00Z` via `SchedulerApiFactory`/`FakeTimeProvider`. |
| Saigon dealership | `a1000000-0000-0000-0000-000000000001` |
| London dealership | `a1000000-0000-0000-0000-000000000002` |
| Customer An Nguyen / vehicle | `c1000000-0000-0000-0000-000000000001` / `b1000000-0000-0000-0000-000000000001` |
| Customer Oliver Smith / vehicle | `c1000000-0000-0000-0000-000000000002` / `b1000000-0000-0000-0000-000000000002` |
| Saigon oil change (60 min) | `51000000-0000-0000-0000-000000000001` |
| Saigon brake service (120 min) | `51000000-0000-0000-0000-000000000002` |
| London annual service (90 min) | `51000000-0000-0000-0000-000000000003` |
| Saigon active technician / bay | `71000000-0000-0000-0000-000000000001` / `81000000-0000-0000-0000-000000000001` |
| London active technician / bay | `71000000-0000-0000-0000-000000000003` / `81000000-0000-0000-0000-000000000003` |
| Normal Saigon slot | `2026-11-03T03:00:00Z` (Tuesday 10:00-11:00 local) inside weekday opening/shift coverage. |
| Equivalent-offset slot | `2026-11-03T10:00:00+07:00` (same instant as `03:00:00Z`). |

- Each test class uses its own database from the fixture; setup-identity probes insert/update/delete only the class's own rows. Concurrency and direct-call classes get dedicated databases in Phase 5.
- API tests use `scheduler_app` through the factory runtime connection; migration/seed/probe setup and cleanup use the privileged setup connection.
- Mutual-exclusion and conflict tests may need second technicians/bays/qualifications/shifts/opening rows; use `SchedulingTestData` helpers with `Guid.NewGuid()` IDs and clean up in `finally`.
- The `AppointmentDurationTests` class uses real Redis; stale-cache cases must not call `ServiceCatalogService` from the booking path.

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet build --no-restore` and `dotnet format --verify-no-changes`. | Build succeeds; formatting is clean. | Not run |
| T-01 / S3-V01 | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentApi"` | Valid POST `201`, followable Location, complete DTO, POST-GET equality, UTC `Z`, restart retrieval, repeated POST is a new attempt; other-dealership/unknown GET `404`. | Not run |
| T-10 / T-11 / S3-V01 | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentValidation"` | Documented `400`/`404` matrix with `code`/`traceId`; equivalent explicit offsets; no writes on failure. | Not run |
| T-02 / T-03 | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentEligibility"` | Inactive/unqualified/bad coverage -> `409 capacity_unavailable`; no row. | Not run |
| Existing query-shape checks (T-02/T-03) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AvailabilitySql"` | Existing generated-SQL predicate assertions for active/qualified/shift/opening/half-open conflict pass; they supplement the real SQL eligibility tests. | Not run |
| T-04-T-06 | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentInterval"` | Adjacent succeeds; partial/contained/identical and resource-only overlaps conflict; final row counts correct. | Not run |
| S3-V03 alternative pair | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentInterval"` | A blocked first ordered pair permits the second eligible pair; exhaustion follows the documented bound; final stored pair is the successful one. | Not run |
| T-10 / S3-V05 API | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentDst"` | London DST fixtures and equivalent offsets match direct procedure and .NET policy. | Not run |
| S3-V02 / AC3-11 | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AppointmentDuration"` | SQL duration controls booking; GET snapshot survives catalog changes; committed row visible to availability; Redis stale value cannot affect booking. | Not run |
| OpenAPI / Problem Details | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~OpenApi"` and `~ProblemDetails` | Both paths, schemas, statuses, `capacity_unavailable`, Location, and `code`/`traceId` are documented/emitted; no sensitive content. | Not run |
| Regression | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | All prior and new integration tests pass. | Not run |

## Exit criteria

- POST returns `201` with a followable `Location` and complete DTO; GET returns identical persisted values and is dealership scoped.
- The documented `400`/`404`/`409` matrix is implemented with `code`/`traceId`; `409 capacity_unavailable` is distinct from dependency and validation failures.
- Adjacent appointments succeed and every overlap variant conflicts for either resource; qualification/inactive/coverage failures return `409` without writes.
- DST/offset handling agrees between .NET precheck and the SQL procedure.
- A stale catalog cannot change a new booking's duration; GET preserves the stored snapshot; availability sees the committed appointment.
- OpenAPI documents both operations and the `409` code; `docs/test/3-test.md` records Phase 4 evidence.
- Concurrency, held-lock, command-timeout, cancellation, outage, and script checks remain explicitly unexecuted until Phase 5.

## Impacts and remaining decisions

- API: two new routes and one shared response contract; `capacity_unavailable` added to the shared Problem Details vocabulary.
- Database/migrations: none beyond Phases 1-2.
- Cache: none; booking never reads Redis, and retrieval is SQL-only.
- Configuration: uses the Phase 3 booking options; no new key.
- Documentation: Phase 4 OpenAPI assertions feed the Phase 6 architecture/setup/API records; the manual Swagger check remains supplementary and Not run until human execution.
- Remaining decisions: no blocking decision. The exact controller action names may adjust while preserving the route, `Location` target, property names, status matrix, and UTC formatting. `51003` is treated as a client validation failure on the API path only because the API never owns a transaction; if a future path introduces one, it must be handled as an internal failure instead.
