---
title: "3 - Appointment Booking and Retrieval: API Contracts and Retrieval"
tags: [research, service-scheduler, booking, api, validation, openapi]
ticket: 3
status: verified
updated: 2026-11-01
---

# 3 - API contracts and retrieval

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research overview: [[research/3/00-overview|Ticket 3 research overview]]
- Requirements: S3-01, S3-05; AC3-01, AC3-02, AC3-10, AC3-11; T-01, T-10, T-11, T-14; S3-V01, S3-V02; project requirements A-01, A-03, D-02.
- Acceptance labels: `AC3-01`-`AC3-13` are research-local mappings to the ticket's 13 unlabeled acceptance bullets, defined in [[research/3/00-overview|Overview 0.2]]; they are not new ticket IDs.
- Evidence baseline: HEAD `90a6284` plus the staged 2026-11-01 ticket 3 update; API source, Problem Details, OpenAPI, availability parsing/rules, and tests inspected on 2026-11-01.

> [!IMPORTANT]
> The POST/GET operations do not exist. The contract below is proposed; every contract check is Not run. Ticket 2's implemented read behavior and error pipeline are the baseline, not booking evidence.

## Existing API foundation

| Fact | Inspected evidence | Consequence for ticket 3 |
| --- | --- | --- |
| Controllers are registered; feature-local controllers exist for discovery, catalog, and availability. | `Program.cs` `AddControllers()`; `Features/{Dealerships,ServiceCatalog,Availability}/*Controller.cs`. | Add `Features/Appointments/AppointmentsController.cs`; no second host. |
| Problem Details customization adds `code` from `ProblemCodes.ForStatus` and `traceId` if absent; framework model-state failures use `InvalidModelStateResponseFactory`. | `Program.cs` lines 29-65; `Infrastructure/Errors/ProblemCodes.cs`, `ProblemResponse.cs`. | Reuse; add `409 capacity_unavailable` and booking-specific manual responses. |
| `ProblemCodes.ForStatus` maps 400/404/500/503 only; no 409. | `ProblemCodes.cs`. | Must Change for `409`. |
| Existing error codes are `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`. | `ProblemCodes.cs`; `ProblemDetailsSchemaTransformer.cs` description. | Add `capacity_unavailable`; update OpenAPI schema text. |
| `StartsAtParser` requires a trailing `Z`/`z` or explicit numeric offset, rejects offset-free/date-only input, and parses invariantly. | `Features/Availability/StartsAtParser.cs`. | Reuse for POST `startsAt`; do not bind `DateTimeOffset` directly. |
| `AvailabilityInterval.TryCreate` rejects non-positive duration, non-future start, and checked-addition overflow. | `Features/Availability/AvailabilityInterval.cs`. | Reuse for booking precheck and per-attempt validation where appropriate. |
| Availability returns `200` with `available:false` for a valid request with no pair; it never writes and never reads Redis. | `AvailabilityService.cs`, `AvailabilityController.cs`. | Booking must return `409`, not `200 available:false`, when it cannot reserve. Availability remains advisory. |
| Existing DTOs use camelCase records; timestamp fields are `DateTime` with `Kind=Utc`, serialized with `Z`. | `Contracts/DealershipResponse.cs`, `ServiceTypeResponse.cs`, `AvailabilityResponse.cs`. | Use the same serialization pattern for appointment timestamps. |
| OpenAPI is mapped in Development; operation/schema transformers add descriptions and `code`/`traceId`. | `Program.cs` lines 67-79; `Infrastructure/OpenApi/*`. | Extend for POST/GET, request/response schemas, statuses, and Location. |
| Existing tests assert ticket 2 operations, Problem Details, and OpenAPI. | `Startup/ProblemDetailsTests.cs`, `OpenApi/OpenApiTests.cs`. | Extend with booking contract cases. |
| No appointment controller, service, DTO, or route exists. | `Glob`/inspection of `Features/`. | Proposed addition. |

## Proposed routes and success responses

| Operation | Route | Success | Failure statuses |
| --- | --- | --- | --- |
| Create appointment | `POST /api/dealerships/{dealershipId}/appointments` | `201 Created` with `Location` and an `AppointmentResponse`; all associations stored. | `400` invalid input/ownership/time/overflow; `404` missing entity or cross-dealership service; `409` no eligible pair under the bounded policy; `503` classified dependency/lock failure; `500` unexpected. |
| Retrieve appointment | `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}` | `200` with the same `AppointmentResponse`. | `400` malformed route GUID; `404` missing or other-dealership appointment; `503` classified dependency failure; `500` unexpected. Get has no `409`. |

The POST body accepts only the customer, vehicle, service type, and desired start. The server chooses technician, bay, end time, and duration snapshot; clients cannot supply resource IDs, duration, or an end time.

### POST request

```json
{
  "customerId": "c1000000-0000-0000-0000-000000000001",
  "vehicleId": "b1000000-0000-0000-0000-000000000001",
  "serviceTypeId": "51000000-0000-0000-0000-000000000001",
  "startsAt": "2026-11-03T03:00:00Z"
}
```

| Property | Type | Required | Contract |
| --- | --- | --- | --- |
| `customerId` | UUID string | Yes | Must be an existing customer. |
| `vehicleId` | UUID string | Yes | Must be an existing vehicle owned by `customerId`. |
| `serviceTypeId` | UUID string | Yes | Must be an existing service type belonging to the route dealership. |
| `startsAt` | string | Yes | ISO-8601 timestamp with `Z`/`z` or an explicit numeric offset; normalized to UTC. Offset-free/date-only input is rejected before conversion. |

Use nullable `Guid?` properties (or equivalent explicit null/`Guid.Empty` checks) so missing JSON properties are rejected rather than silently binding to `Guid.Empty`. Mark the request properties required (for example `[Required]`/required members) so OpenAPI reflects the contract while explicit checks still reject missing/empty values. Bind `startsAt` as `string?`; do not use `DateTimeOffset` binding. Malformed UUIDs and malformed JSON are framework model-state failures and must use the shared `400 validation_error` contract.

### POST/GET success response

```json
{
  "id": "5a000000-0000-0000-0000-000000000001",
  "dealershipId": "a1000000-0000-0000-0000-000000000001",
  "customerId": "c1000000-0000-0000-0000-000000000001",
  "vehicleId": "b1000000-0000-0000-0000-000000000001",
  "serviceTypeId": "51000000-0000-0000-0000-000000000001",
  "technicianId": "71000000-0000-0000-0000-000000000001",
  "serviceBayId": "81000000-0000-0000-0000-000000000001",
  "startsAt": "2026-11-03T03:00:00Z",
  "endsAt": "2026-11-03T04:00:00Z",
  "durationMinutes": 60,
  "status": "Confirmed",
  "createdAt": "2026-11-01T12:00:00Z"
}
```

The appointment ID and `createdAt` are illustrative; the values are generated by SQL Server. Both POST and GET return the same property names and semantics. `status` is the stored value, initially `"Confirmed"` (case-sensitive, matching `AppointmentStatuses.Confirmed`). All timestamps are UTC and serialized with a trailing `Z`. `durationMinutes` and `endsAt` are the persisted snapshot/end; GET must not recompute them from the current catalog.

### Location

Use `CreatedAtAction(nameof(GetById), new { dealershipId, appointmentId = result.AppointmentId }, response)` so the `Location` header points to `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}`. A client must be able to follow the Location and receive the persisted appointment. The absolute/relative form of the URL is not contractual; the route target is.

## Field sources and mapping

| Field | Source / derivation | Type / nullability | Validation / response behavior |
| --- | --- | --- | --- |
| `id` | Procedure output `@AppointmentId` (`NEWID`) or persisted `Appointments.Id` | UUID, required | POST uses the procedure output; GET reads the row. |
| `dealershipId` | Route and procedure input; persisted `Appointments.DealershipId` | UUID, required | Route dealership must exist; service/technician/bay are dealership-scoped. |
| `customerId` | Request; procedure input; persisted `Appointments.CustomerId` | UUID, required | Customer must exist. |
| `vehicleId` | Request; procedure input; persisted `Appointments.VehicleId` | UUID, required | Vehicle must exist and belong to `customerId`. |
| `serviceTypeId` | Request; procedure input; persisted `Appointments.ServiceTypeId` | UUID, required | Service must exist and belong to the route dealership. |
| `technicianId` | Procedure-selected candidate; persisted `Appointments.TechnicianId` | UUID, required | Active, qualified, dealership-scoped, shift-covered, conflict-free. |
| `serviceBayId` | Procedure-selected candidate; persisted `Appointments.ServiceBayId` | UUID, required | Active, dealership-scoped, conflict-free. |
| `startsAt` | Parsed request instant normalized to UTC; persisted `Appointments.StartUtc` | `DateTime` (`Kind=Utc`) | Explicit offset required; strictly future; serialized with `Z`. |
| `endsAt` | Procedure `DATEADD(minute, DurationMinutes, StartUtc)`; persisted `Appointments.EndUtc` | `DateTime` (`Kind=Utc`) | Server-derived; never client-supplied; GET uses stored value. |
| `durationMinutes` | SQL `ServiceTypes.DurationMinutes` read inside the procedure; persisted `Appointments.DurationMinutes` | `int`, positive | Server-derived snapshot; later catalog changes do not alter it. |
| `status` | Procedure/persisted `Appointments.Status` | `string`, `"Confirmed"` | Schema check allows only `Confirmed`; GET returns stored value. |
| `createdAt` | Procedure `SYSUTCDATETIME` cast to `datetimeoffset(7)`; persisted `Appointments.CreatedUtc` | `DateTime` (`Kind=Utc`) | Server-generated; serialized with `Z`; not client-supplied. |

Persisted `DateTimeOffset` values are projected with `.UtcDateTime` into the response `DateTime` fields (the existing availability controller uses this pattern), so `System.Text.Json` emits a trailing `Z` rather than `+00:00`.

## Validation and time contract

1. Bind `dealershipId`/`appointmentId` as `Guid` without a `:guid` route constraint so malformed values reach model binding and return `400 validation_error`, not a route-selected `404`.
2. Bind POST IDs as nullable UUID properties and reject missing/`Guid.Empty` values. Malformed UUIDs are model-state `400`.
3. Bind `startsAt` as a raw string, trim it, require a trailing `Z`/`z` or explicit numeric offset (`±HH:MM`/`±HHMM`), and parse with `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, DateTimeStyles.None, ...)`; reuse `StartsAtParser` or an equivalent shared parser.
4. Normalize to UTC with `ToUniversalTime()`; equivalent explicit offsets identify the same instant. The input offset need not match the dealership zone.
5. Capture `evaluatedAt` from the injected `TimeProvider` once for the request validation; require `startsAt > evaluatedAt` (equal is rejected).
6. Read the authoritative SQL `ServiceTypes.DurationMinutes` for the route dealership; compute `endsAt` with checked addition and reject overflow as `400`. The procedure repeats duration/end derivation and returns the persisted values.
7. Convert start/end to the dealership's local wall clock only for opening-coverage evaluation; use the established wall-clock/DST policy. Shift and conflict checks remain UTC and half-open.
8. The complete interval must fit one opening row and one technician shift; adjacent rows are not combined. An end exactly at closing/shift end is allowed.
9. The client cannot choose duration, end, technician, or bay. Extra body/query fields are not part of the contract; do not use them.

### Validation decision table

| Input / condition | HTTP result | Problem Details code |
| --- | --- | --- |
| Missing/malformed body, missing required property, malformed UUID, unparseable/offset-free/date-only `startsAt` | `400` | `validation_error` |
| `startsAt <= evaluatedAt` or per-attempt past start | `400` | `validation_error` |
| Computed end overflow or non-positive/invalid service duration | `400` | `validation_error` |
| Existing customer and vehicle but vehicle belongs to another customer | `400` | `validation_error` |
| Malformed `dealershipId`/`appointmentId` route UUID | `400` | `validation_error` |
| Unknown dealership, customer, vehicle, or service; service belongs to another dealership | `404` | `not_found` |
| Valid request, no eligible technician/bay pair under the bounded allocation policy, including insufficient opening/shift coverage | `409` | `capacity_unavailable` |
| Classified SQL connectivity/timeout/deadlock/lock failure (including exhausted deadlock retries) | `503` | `dependency_unavailable` |
| Unexpected failure (including invalid SQL time-zone mapping at runtime) | `500` (sanitized) | `internal_error` |
| GET appointment missing or belonging to another dealership | `404` | `not_found` |

A valid request that cannot reserve capacity is `409`, not `200 available:false` and not a false success. Availability remains a separate advisory read.

## Problem Details contract

Add a stable `capacity_unavailable` code for `409` and map it in `ProblemCodes.ForStatus(StatusCodes.Status409Conflict)`. The response body uses the shared `ProblemResponse.Create`/Problem Details pipeline so `code` and `traceId` are present. Suggested copy:

```json
{
  "title": "The requested interval is unavailable.",
  "status": 409,
  "detail": "No eligible technician and service bay pair could be reserved for the requested interval within the configured allocation limit.",
  "code": "capacity_unavailable",
  "traceId": "00-..."
}
```

| Status | `code` | Use |
| --- | --- | --- |
| `400` | `validation_error` | Malformed/missing input, malformed UUIDs, offset-free/unparseable/past starts, overflow, ownership mismatch; framework model-state failures. |
| `404` | `not_found` | Unknown dealership/customer/vehicle/service, cross-dealership service, missing/other-dealership appointment, unknown route. |
| `409` | `capacity_unavailable` | Valid request but no eligible pair within the bounded allocation policy, including opening/shift coverage and conflicts. |
| `503` | `dependency_unavailable` | Classified SQL connectivity/timeout/deadlock/lock failures. |
| `500` | `internal_error` | Unexpected failures; no stack trace, credentials, or connection details. |

The mandatory documented booking outcomes all carry `code`/`traceId`, including framework model-state `400` responses. `405`/`415` remain framework routing/media errors outside the ticket's documented outcome matrix; add codes only if planning decides the literal "all Problem Details" wording requires them.

## Procedure error mapping at the API boundary

The procedure contract and numbers are in [[research/3/02-booking-procedure-and-concurrency|Part 2]]. The controller/service maps them as follows; known procedure errors must be handled before the global SQL exception handler.

| Procedure result / SQL error | API behavior |
| --- | --- |
| `51001` technician conflict | Try the next bounded candidate pair; `409 capacity_unavailable` if attempts are exhausted. |
| `51002` bay conflict | Try the next bounded candidate pair; `409 capacity_unavailable` if attempts are exhausted. |
| `51003` invalid request | `400 validation_error`; no candidate retry. |
| `51004` resource not eligible | Try the next bounded pair; `409 capacity_unavailable` if exhausted. |
| `51005` coverage unavailable | Try the next bounded pair; `409 capacity_unavailable` if exhausted. |
| `51006` time-zone mapping unavailable/invalid | `500 internal_error`; sanitized. |
| `1205` deadlock victim | Bounded whole-attempt retry; `503 dependency_unavailable` after exhaustion. |
| `1222` lock timeout, `-2` command timeout, classified connectivity | `503 dependency_unavailable`; no write replay. |
| Unexpected SQL/other exception | `500 internal_error`; preserve the exception for controlled handling/logging. |

## Retrieval

- `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}` reads `SchedulerDbContext.Appointments` with `AsNoTracking()` and predicates on both `Id` and `DealershipId`; an appointment from another dealership produces no row and `404`.
- Return the stored `StartUtc`, `EndUtc`, `DurationMinutes`, `Status`, `CreatedUtc`, and all association IDs. Do not re-read the catalog or recompute duration/end.
- Retrieval makes no Redis call and no writes. The SQL named volume preserves appointments across ordinary API/container restarts.
- GET has no `409` outcome. SQL dependency failure maps to `503`; unexpected failure maps to sanitized `500`.

## Data flow

### POST

```text
HTTP POST
  -> controller binds raw startsAt/IDs; malformed input -> 400
  -> AppointmentService/allocator captures evaluatedAt from TimeProvider
  -> EF dealership+service lookup (404 if missing/cross-dealership)
  -> EF customer lookup (404), vehicle lookup (404), ownership check (400)
  -> SQL duration + checked end/future validation (400)
  -> EF opening coverage (409 if no single row)
  -> EF ordered technician and bay candidate reads, bounded (409 if none)
  -> bounded pair loop -> IAppointmentBookingProcedure -> dbo.CreateAppointment
  -> procedure revalidates and inserts/commits
  -> 201 + Location + AppointmentResponse
```

### GET

```text
HTTP GET
  -> controller binds dealershipId/appointmentId (malformed -> 400)
  -> EF AsNoTracking appointment query filtered by Id AND DealershipId
  -> found: 200 AppointmentResponse (stored values)
  -> missing/other dealership: 404 not_found
```

## Cache and availability compatibility

- Booking reads `ServiceTypes.DurationMinutes` directly from SQL Server for its precheck and the procedure reads it again inside the transaction; it never calls `ServiceCatalogService` or Redis. A stale catalog cache therefore cannot change a new booking's duration.
- GET reads the persisted appointment; later catalog duration changes cannot alter it.
- Availability is uncached and queries confirmed appointments directly; a committed booking is visible to subsequent availability reads. No booking-driven cache invalidation is required.
- Ticket 3 introduces no catalog key, payload, TTL, or cache infrastructure change.

## OpenAPI documentation

- Add `[ProducesResponseType]` attributes: POST `201` (`AppointmentResponse`), `400`, `404`, `409`, `503`, `500`; GET `200` (`AppointmentResponse`), `400`, `404`, `503`, `500`.
- Add `[ProducesResponseType]`/request-body schema for `CreateAppointmentRequest` (IDs and raw `startsAt` string) and `AppointmentResponse`.
- Extend `ApiOperationTransformer` with:
  - summaries/descriptions for create and retrieve;
  - route parameter descriptions (`dealershipId`, `appointmentId`);
  - success descriptions including Location and stored snapshot semantics;
  - `409 capacity_unavailable` and bounded-allocation wording for POST;
  - `404 not_found` for missing/other-dealership retrieval and missing entities/cross-dealership service on POST;
  - `503`/`500` descriptions consistent with the existing error contract.
- Update `ProblemDetailsSchemaTransformer` so the `code` description lists `validation_error`, `not_found`, `capacity_unavailable`, `dependency_unavailable`, and `internal_error`.
- Extend `OpenApiTests` to assert both paths, the request/response schemas, the status sets, and the `capacity_unavailable` documentation. Do not embed expiring dates as the only examples.

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | No appointment controller/service registration. | Register `AppointmentService`, `BookingAllocator`/options, and the procedure boundary. | S3-01-S3-05 |
| Must Change | `ProblemCodes.cs`, `ProblemResponse.cs`, `ProblemDetailsSchemaTransformer.cs` | No `409` code. | Add `capacity_unavailable` and schema text. | S3-01, S3-04, S3-05 |
| Must Change | `ApiOperationTransformer.cs`, `OpenApiTests.cs` | Only ticket 2 operations documented. | Add POST/GET operation/response documentation and assertions. | S3-01, S3-06 |
| Must Change | `ProblemDetailsTests.cs` | No booking 400/404/409/503/500 cases. | Add booking error-path assertions. | S3-01, S3-04, S3-05 |
| Need Review | `StartsAtParser.cs` | Located under `Features/Availability`; booking reuse would couple features. | Reuse in place or extract to a shared scheduling/time namespace without behavior change; decide in plan. | S3-01 |
| Need Review | `AvailabilityInterval.cs`, `OpeningCoverage.cs` | Existing pure rules; booking precheck needs the same semantics but the procedure is authoritative. | Reuse the .NET precheck only; never substitute for the procedure. | S3-02 |
| No Change | `DealershipsController`, `ServiceCatalogController`, `AvailabilityController` | Existing read endpoints. | Keep; booking adds its own routes. | S3-01, S3-05 |
| No Change | `ServiceCatalogService`, `BoundedCache` | Catalog cache-aside. | Booking must not read it; no cache change. | S3-02 |
| Proposed Addition | `Features/Appointments/AppointmentsController.cs`, `Contracts/CreateAppointmentRequest.cs`, `Contracts/AppointmentResponse.cs` | No booking HTTP/DTOs. | Add routes, validation mapping, Location, DTOs. | S3-01, S3-05 |
| Proposed Addition | Booking contract tests | No booking API tests. | Add valid/invalid/retrieval/OpenAPI/error cases. | S3-01, S3-05 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| A3-01 | Decision | Exact response property names? | Use `id`, association IDs, `startsAt`, `endsAt`, `durationMinutes`, `status`, `createdAt`; camelCase web defaults; document in OpenAPI. | Resolved. |
| A3-02 | Decision | `createdAt` versus `createdUtc`? | `createdAt` matches existing `*At` timestamp naming; description states UTC. | Resolved. |
| A3-03 | Decision | How to bind IDs without treating missing properties as `Guid.Empty`? | Nullable GUID properties plus explicit null/empty validation; malformed UUIDs stay framework `400`. | Resolved. |
| A3-04 | Decision | How to reject offset-free timestamps? | Raw string + shared `StartsAtParser`; no direct `DateTimeOffset` body binding. | Resolved; parser implementation tested in ticket 2 unit tests. |
| A3-05 | Risk | Framework model-state responses must carry `code`/`traceId`. | Existing `InvalidModelStateResponseFactory`; extend tests to booking body/route failures. | Deferred to implementation tests. |
| A3-06 | Decision | `409` code? | `capacity_unavailable`, added to `ProblemCodes` and OpenAPI. | Resolved. |
| A3-07 | Decision | Location absolute or relative? | Use `CreatedAtAction`; the followable route is contractual, not the URL form. | Resolved. |
| A3-08 | Risk | POST success built from inputs could diverge from GET stored values. | Use procedure output for ID/end/duration/createdAt and test POST-then-GET equality. | Deferred to implementation tests. |
| A3-09 | Decision | How is a stale catalog prevented from affecting booking? | Never call the catalog cache; read SQL duration in the API precheck and procedure. | Resolved; S3-V02 test planned. |
| A3-10 | Decision | GET other-dealership behavior? | Filter by `Id` and `DealershipId`; return `404`. | Resolved; T-11 test planned. |
| A3-11 | Risk | 405/415 have no application code under the current customizer. | Documented mandatory outcomes all have codes; 405/415 are outside the booking matrix. Revisit only if planning interprets "all Problem Details" literally. | Resolved with documented interpretation. |
| A3-12 | Risk | Extra client fields could be silently ignored. | Accept only the documented body fields; do not use resource/duration/end inputs even if supplied. | Resolved. |

## Verification mapping for this concern

| Requirement | Proposed check | Level |
| --- | --- | --- |
| S3-01, AC3-10 | Valid POST `201`, exact fields, Location, POST-then-GET equality, UTC `Z`, malformed/missing input, ownership mismatch, past/offset-free starts, overflow. | Unit + API |
| S3-05, AC3-01, AC3-10 | GET stored associations/end/duration/status/createdAt; missing/other-dealership `404`; restart retrieval. | API + SQL |
| S3-04 | `409 capacity_unavailable` and `code`/`traceId` for no-pair/conflict; 400/404/503/500 matrix. | API + SQL |
| S3-02, AC3-11 | Booking uses SQL duration despite stale catalog; GET preserves snapshot after a service change; availability sees the committed row. | API + SQL + Redis |
| S3-06 | OpenAPI paths/schemas/status descriptions; automated contract tests; Swagger/manual supplement. | API + OpenAPI |

All contract checks are **Not run**. Detailed test files and commands are in [[research/3/04-verification-and-documentation|Part 4]].
