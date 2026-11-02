---
title: "2 - Service Discovery and Availability: API Contracts and Validation"
tags: [research, service-scheduler, api, validation, openapi]
ticket: 2
status: verified
updated: 2026-11-01
---

# 2 - API contracts and validation

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research overview: [[research/2/00-overview|Ticket 2 research overview]]
- Requirements: S2-01, S2-02, S2-04, S2-06; AC2-01, AC2-02, AC2-03, AC2-06; T-10, T-11; project requirements D-02 and A-02.
- Evidence baseline: HEAD `3f8ab1e` (2026-11-01), clean working tree.
- Implementation status: the pre-implementation audit below is historical. Ticket 2 implementation and executed test records landed on 2026-11-01; see [[test/2-test|the test report]].

## Existing API foundation

| Fact | Inspected evidence | Consequence for ticket 2 |
| --- | --- | --- |
| Controllers are registered but no business controller exists. | `Program.cs:19`; no `Features/` or controller files in the checkout. | Add feature-local controllers; no second host. |
| Default Problem Details and status-code handling are enabled. | `AddProblemDetails()`, `UseExceptionHandler()`, `UseStatusCodePages()` at `Program.cs:20,47-48`. | Extend customization rather than replace middleware. |
| Unknown routes already return 404 Problem Details with `traceId` and no stack trace. | `ProblemDetailsTests.cs:10-31`. | Preserve this behavior; add a stable `code` extension and explicit cases. |
| No injected application `TimeProvider` exists. | `Program.cs` has no `TimeProvider` registration; setup passes `TimeProvider.System` only at `DatabaseSetupCommand.cs:33`. | Register `TimeProvider.System` in the API and replace it in tests. |
| Business DTOs and OpenAPI operations are absent. | `OpenApiTests.cs:10-42` asserts only document/Swagger availability. | Add DTOs, response attributes, operation/schema transformers, and assertions. |
| Swagger UI reads `/openapi/v1.json` in Development. | `Program.cs:50-57`; `OpenApiTests.cs:10-42`. | New operations appear automatically once documented. |

## Proposed API contract

### Routes and success responses

| Operation | Route | Success | Failure statuses |
| --- | --- | --- | --- |
| Dealership discovery | `GET /api/dealerships` | `200` JSON array of dealerships ordered by name ascending (ID tiebreaker). | `503` classified SQL unavailability; `500` unexpected; `400` only if routing/binding fails. |
| Service catalog | `GET /api/dealerships/{dealershipId}/service-types` | `200` JSON array of service types ordered by name ascending (ID tiebreaker); `[]` for an existing dealership with no services. | `400` malformed dealership UUID; `404` unknown dealership; `503` SQL unavailability on a miss; `500` unexpected. A cache hit may return `200` when SQL is unavailable. |
| Availability | `GET /api/dealerships/{dealershipId}/availability?serviceTypeId=...&startsAt=...` | `200` availability DTO for both `available: true` and `available: false`. | `400` malformed/missing UUID or timestamp, offset-free, past/equal start, computed-end overflow; `404` unknown dealership/service or service from another dealership; `503` classified SQL unavailability; `500` unexpected. |

The availability route intentionally accepts no technician, bay, duration, or end-time inputs. A request with those extra query parameters is not part of the contract; the endpoint ignores unknown query parameters but never uses them. Resource-scope tests seed foreign-dealership candidates because the API exposes no resource parameters. Ticket 2 also accepts no customer or vehicle identifiers, so customer/vehicle ownership validation (BR-01) is not applicable to these reads; dealership consistency is enforced by the scoped service/technician/bay queries and the existing composite foreign keys.

### Success DTOs

```json
GET /api/dealerships
[
  { "id": "a1000000-0000-0000-0000-000000000002", "name": "London Riverside Motors", "timeZoneId": "Europe/London" },
  { "id": "a1000000-0000-0000-0000-000000000001", "name": "Saigon Central Motors", "timeZoneId": "Asia/Ho_Chi_Minh" }
]
```

```json
GET /api/dealerships/{saigonId}/service-types
[
  { "id": "51000000-0000-0000-0000-000000000002", "name": "Brake Service", "durationMinutes": 120 },
  { "id": "51000000-0000-0000-0000-000000000001", "name": "Oil Change", "durationMinutes": 60 }
]
```

```json
GET /api/dealerships/{saigonId}/availability?serviceTypeId={oilChangeId}&startsAt=2026-11-03T03:00:00Z
{
  "dealershipId": "a1000000-0000-0000-0000-000000000001",
  "serviceTypeId": "51000000-0000-0000-0000-000000000001",
  "available": true,
  "startsAt": "2026-11-03T03:00:00Z",
  "endsAt": "2026-11-03T04:00:00Z",
  "evaluatedAt": "2026-11-02T00:00:00Z"
}
```

The same six fields are returned when `available` is `false`; `startsAt` is the normalized requested instant, `endsAt` is the SQL-derived end, and `evaluatedAt` is the single captured clock value. Technician and bay identifiers are deliberately absent.

### DTO field sources

| Field | Source / derivation | Type and nullability | Validation / response behavior |
| --- | --- | --- | --- |
| Dealership `id` | `Dealerships.Id` | `Guid`, required | Ordered list; no cache. |
| Dealership `name` | `Dealerships.Name` | `string`, required | Name ascending, ID tiebreaker. |
| Dealership `timeZoneId` | `Dealerships.TimeZoneId` (IANA) | `string`, required | Returned as stored; availability resolves it server-side. |
| Service `id` | `ServiceTypes.Id` | `Guid`, required | Dealership-scoped catalog. |
| Service `name` | `ServiceTypes.Name` | `string`, required | Name ascending, ID tiebreaker. |
| Service `durationMinutes` | `ServiceTypes.DurationMinutes` | `int`, required, positive by `CK_ServiceTypes_DurationMinutes` | Authoritative duration for availability; never cached into availability. |
| Availability `dealershipId` | Route | `Guid` | Echoed after validation. |
| Availability `serviceTypeId` | Query | `Guid` | Echoed after dealership-scoped lookup. |
| Availability `available` | Computed | `bool` | `true` only when an eligible technician and bay both exist; otherwise `false` with `200`. |
| Availability `startsAt` | Parsed request instant normalized to UTC | `DateTime` (`Kind=Utc`) | Serialized with `Z`; explicit offset/Z required. |
| Availability `endsAt` | `startsAt + SQL DurationMinutes` | `DateTime` (`Kind=Utc`) | Overflow rejected as `400`; serialized with `Z`. |
| Availability `evaluatedAt` | `TimeProvider.GetUtcNow()` captured once | `DateTime` (`Kind=Utc`) | Returned unchanged for the request; serialized with `Z`. |
| Error `code` | `ProblemCodes` mapping | `string` | Stable values in the table below. |
| Error `traceId` | `Activity.Current?.Id ?? HttpContext.TraceIdentifier` | `string` | Present for validation, not-found, dependency, and unexpected failures. |

### Explicit-offset and future-start validation

1. Bind `dealershipId` and `serviceTypeId` as `Guid` without a `:guid` route constraint, so a malformed route value reaches model binding and produces a `400` rather than a route-selected `404`.
2. Bind `startsAt` as a raw `string`/`string?`; do not bind `DateTimeOffset` directly, because model binding can silently interpret offset-free input as the host-local time.
3. Trim the raw value and require a trailing `Z`/`z` or explicit numeric offset (`±HH:MM` or `±HHMM`). Reject offset-free/date-only values before parsing.
4. Parse with `DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)`. Unparseable or invalid offset values return `400`.
5. Normalize with `value.ToUniversalTime()` and use the resulting instant for the interval.
6. Capture `evaluatedAt = timeProvider.GetUtcNow()` exactly once at the start of the request.
7. Require `startsAt > evaluatedAt` (strictly future; equality returns `400`).
8. Compute `endsAt` from the SQL duration using checked tick arithmetic; if it exceeds the supported timestamp range, return `400`.
9. Convert `startsAt`/`endsAt` to the dealership zone only for opening-hour coverage; elapsed comparisons and shift/conflict checks use UTC.
10. Serialize response timestamps as `DateTime` values with `Kind=Utc` (`DateTimeOffset.UtcDateTime`) so System.Text.Json emits a trailing `Z`.

URL examples must encode a positive offset's `+` as `%2B`:

```text
...&startsAt=2026-11-03T10:00:00%2B07:00
...&startsAt=2026-11-03T03:00:00Z
```

Both examples above identify the same instant and must produce the same `startsAt`/`endsAt`. The input offset need not match the dealership offset; it identifies the instant. The opening-hour and DST policy is owned by [[research/2/02-availability-and-scheduling|Part 2]].

### Validation decision table

| Input / condition | Result | Error code |
| --- | --- | --- |
| Malformed `dealershipId`/`serviceTypeId` UUID | `400` | `validation_error` |
| Missing `serviceTypeId` or `startsAt` | `400` | `validation_error` |
| `startsAt` offset-free, date-only, or unparseable | `400` | `validation_error` |
| `startsAt <= evaluatedAt` | `400` | `validation_error` |
| Computed end outside supported range | `400` | `validation_error` |
| Valid GUIDs/timestamp but unknown dealership or service, or service belongs to another dealership | `404` | `not_found` |
| Valid request with no eligible technician/bay/opening/shift | `200`, `available:false` | none |
| Classified SQL outage on a SQL-dependent read | `503` | `dependency_unavailable` |
| Unexpected exception | `500` (sanitized) | `internal_error` |

## Problem Details contract

### Stable error codes and extensions

| Status | `code` value | Use |
| --- | --- | --- |
| `400` | `validation_error` | Malformed/missing UUIDs, unparseable or offset-free timestamps, non-future starts, duration-end overflow, and framework-generated model-state failures. |
| `404` | `not_found` | Unknown dealership/service, service from another dealership, and unknown route. |
| `503` | `dependency_unavailable` | Classified SQL Server unavailability on discovery/catalog miss/availability. |
| `500` | `internal_error` | Unexpected failures; no exception message, stack trace, or connection details. |

Extensions:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "The request is invalid.",
  "status": 400,
  "detail": "startsAt must include Z or an explicit offset.",
  "code": "validation_error",
  "traceId": "00-...-..."
}
```

`title`/`detail` remain short, stable, and sanitized. Do not include request bodies, credentials, connection strings, stack traces, or internal exception types.

### Implementation approach

- Keep `AddProblemDetails()` but configure `ProblemDetailsOptions.CustomizeProblemDetails` to add `code` from the status and `traceId` if absent.
- Use a `ProblemCodes` constant class so controllers, exception handlers, and tests share values.
- Use a `SqlFailureClassifier` that traverses the exception and inner exceptions and recognizes a narrow set of SQL connectivity/timeout/deadlock numbers. Suggested initial set: `-2`, `2`, `53`, `121`, `233`, `1205`, `1222`, `4060`, `40197`, `40501`, `40613`, `49918`, `49919`, `49920`, `10053`, `10054`, `10060`, `10061`, `10928`, `10929`, `11001`. Authentication/permission errors such as `18456`/`229` indicate configuration defects rather than transient dependency unavailability; leave them unclassified so they surface as sanitized `500` unless a later requirement explicitly classifies them.
- Register an `IExceptionHandler` (for example `SqlDependencyExceptionHandler`) with `AddExceptionHandler<T>()`. It logs a warning with the status/code/trace ID and SQL error number (not the full connection string), writes a `503` Problem Details through `IProblemDetailsService`, and returns `true`. It must not convert every SQL/persistence exception into a dependency failure; unlisted exceptions remain sanitized `500` responses.
- Do not classify `OperationCanceledException` caused by the caller as a dependency failure; let request cancellation propagate.
- For manual controller `400` responses, create `ProblemDetails` through the same factory/customization path or set `code`/`traceId` explicitly; do not return a bare `BadRequest("...")`.
- Preserve the existing unknown-route behavior and its trace ID. The new `code` extension must appear on framework-generated validation responses as well as custom ones.
- Keep the default exception handler's sanitized `500` behavior. A controlled unexpected-failure test can use a temporary dealership with an invalid IANA `TimeZoneId` inserted through the setup identity; the resulting time-zone exception is not a classified SQL outage and must become `500` with no sensitive detail.

### Error-path test coverage

| Case | Expected |
| --- | --- |
| Missing `serviceTypeId`/`startsAt` | `400`, `code=validation_error`, non-empty `traceId`, Problem Details content type |
| Malformed route/query UUID | `400`, `code=validation_error` (route must not be treated as unknown `404`) |
| Offset-free, unparseable, date-only timestamp | `400`, `code=validation_error` |
| Past timestamp and timestamp equal to `evaluatedAt` | `400`, `code=validation_error` |
| Duration overflow | `400`, `code=validation_error` |
| Unknown dealership/service and cross-dealership service | `404`, `code=not_found` |
| SQL unreachable on discovery/availability/catalog miss | `503`, `code=dependency_unavailable`, no false availability |
| Unexpected invalid-time-zone failure | `500`, `code=internal_error`, no stack/password/connection string |
| Existing unknown-route test | `404`, `code=not_found`, `traceId`, no stack/password |

## OpenAPI documentation

- Add `[ProducesResponseType]` attributes for every success status and error status on each action, using the concrete DTO and `ProblemDetails` types.
- Use built-in OpenAPI operation/schema transformers (registered through `AddOpenApi(options => ...)` in `Program.cs`) to add:
  - summaries/descriptions for the three operations;
  - ordering statements: dealerships by `name` ascending; service types by `name` ascending, with ID tiebreakers;
  - parameter descriptions for `dealershipId`, `serviceTypeId`, and `startsAt`, including the explicit-offset requirement and the `%2B` example;
  - availability outcome descriptions for `available:true`/`false`;
  - `ProblemDetails` extension properties `code`/`traceId` in the schema and error examples;
  - stable error-code/status tables in operation descriptions.
- Do not embed expiring dates or unverified seeded IDs as the only examples. OpenAPI descriptions may use placeholders; automated API integration tests discover actual seeded IDs and compute future starts from the fixed clock and seeded horizon, and Swagger UI supports supplementary manual requests.
- Keep Swagger UI at `/swagger/index.html`; it already points at `/openapi/v1.json` (`Program.cs:50-57`).
- Extend `OpenApiTests` to assert the three paths, response schemas/status codes, ordering text, and `ProblemDetails` extension documentation, in addition to the existing document/Swagger checks.

## Swagger and automated API request coverage

The 2026-11-01 scope amendment retired the standalone `scripts/ticket2-demo.ps1`; no script deliverable is required. AC2-01 is satisfied by automated API integration tests using synthetic data, with Swagger UI as the supplementary manual client:

1. The tests discover seeded dealership and service IDs through the implemented API or the SQL fixture and use the fixed clock `2026-11-02T00:00:00Z` with seeded future shifts rather than embedding expiring dates.
2. Availability tests cover equivalent `Z` and `+07:00` (`%2B`) instants, the full DTO fields, UTC `Z` timestamps, and both `available:true`/`available:false` outcomes.
3. Contract tests cover unknown dealership/service (`404`), missing/malformed parameters (`400`), and offset-free `startsAt` (`400`), asserting `code` and `traceId`.
4. Swagger UI at `/swagger/index.html` reads `/openapi/v1.json` and supports human trial requests against the isolated Compose stack; manual execution is recorded separately as Not run or Blocked when not performed.

The test report must record actual commands and observed results; an automated test count or a Swagger page is not itself acceptance evidence.

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | Default Problem Details only; no controllers, `TimeProvider`, OpenAPI transformers, or DB exception handler. | Add registrations/middleware/transformers described above. | S2-01, S2-02, S2-04, S2-06 |
| Must Change | `ProblemDetailsTests.cs` | Asserts only 404/traceId/no stack. | Add `code` and 400/503/500 cases. | S2-04 |
| Must Change | `OpenApiTests.cs` | Asserts only document/Swagger existence. | Assert three operations, DTOs, status codes, ordering, error schema. | S2-06, AC2-01 |
| Must Change | `SchedulerApiFactory.cs` | No controllable `TimeProvider`. | Add optional `TimeProvider` replacement for deterministic API tests. | S2-02 |
| Proposed Addition | `Features/Dealerships/DealershipsController.cs`, contracts/query | No discovery endpoint. | Add ordered discovery read/DTO. | S2-01 |
| Proposed Addition | `Features/ServiceCatalog/ServiceCatalogController.cs`, contracts/service | No catalog endpoint. | Add catalog read with cache-aside (Part 3). | S2-01, S2-05 |
| Proposed Addition | `Features/Availability/AvailabilityController.cs`, contracts/service/parser | No availability endpoint. | Add request parsing, rules, SQL reads, DTO mapping. | S2-02-S2-04 |
| Proposed Addition | `Infrastructure/Errors/ProblemCodes.cs`, `SqlFailureClassifier.cs`, `SqlDependencyExceptionHandler.cs` | Default Problem Details only. | Add stable codes/trace ID and narrow SQL classification. | S2-04 |
| Retired | `scripts/ticket2-demo.ps1` | No executable examples. | Retired by the 2026-11-01 scope amendment; automated API integration tests and Swagger UI cover S2-06/AC2-01. | S2-06, AC2-01 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| A-01 | Decision | How to reject offset-free input without model binding localizing it? | Bind raw string, require trailing `Z`/offset, then `DateTimeOffset.TryParse` with invariant culture. | Resolved; parser implementation tests deferred to TC2-12/TC2-13. |
| A-02 | Decision | How to serialize UTC with `Z`? | DTO `DateTime` values with `Kind=Utc` from `DateTimeOffset.UtcDateTime`. | Resolved and verified 2026-11-01: `System.Text.Json` emitted `"2026-11-03T03:00:00Z"` for both `DateTime` with `Kind=Utc` and `DateTimeOffset.UtcDateTime`; API serialization test deferred. |
| A-03 | Decision | Which SQL errors map to `503`? | Narrow classifier list in this part; unlisted remain `500`. | Resolved; classifier and API error-path tests deferred to TC2-26/TC2-34. |
| A-04 | Risk | Framework-generated validation responses must carry `code`/`traceId`. | `CustomizeProblemDetails` applies to `IProblemDetailsService`/factory outputs; test missing/malformed parameters. | Deferred: requires the implemented Problem Details customization; covered by TC2-34. |
| A-05 | Decision | How to document OpenAPI without expiring examples? | Attributes plus transformers for descriptions/order/error schema; automated API integration tests use seeded IDs and computed future starts, and Swagger UI supports manual requests. | Resolved and capability verified 2026-11-01: `IOpenApiOperationTransformer`, `IOpenApiSchemaTransformer`, and the type-based/delegate transformer types exist in `Microsoft.AspNetCore.OpenApi` 10.0.13. Document assertions remain covered by TC2-33; the standalone script and TC2-35 were retired by the 2026-11-01 scope amendment. |
| A-06 | Risk | A malformed route GUID could produce `404` instead of `400` if a `:guid` constraint is added. | Do not add a route constraint; rely on model binding failure and test it. | Deferred: API binding test covered by TC2-04/TC2-34. |
| A-07 | Risk | Missing `startsAt` bound as a non-nullable string may produce a model-state error with an unpredictable message. | Keep the contract status/code stable; assert status/`code` rather than exact framework wording. | Deferred: API binding test covered by TC2-34. |

Research-phase probe on 2026-11-01 (temporary project outside the repository) confirmed the UTC `Z` serialization and the presence of the built-in OpenAPI transformer APIs. It did not implement or exercise an endpoint; every ticket-level API test remains Not run.

## Verification mapping for this concern

| Requirement | Proposed check | Level |
| --- | --- | --- |
| S2-01 | Discovery fields/order; catalog fields/order, empty catalog, unknown dealership; OpenAPI operations/DTOs. | API + SQL + OpenAPI |
| S2-02 | Offset-free/past/equal/unparseable rejection; equivalent `+07:00`/`Z` instants; UTC `Z` response; computed end/overflow. | Unit + API |
| S2-04 | `code`/`traceId` on 400/404/503/500; SQL outage never returns false availability. | Unit + API + SQL |
| S2-06 | OpenAPI document inspection; automated API integration tests; Swagger/manual instructions. | API |

Research-phase probes executed on 2026-11-01 covered only UTC serialization, OpenAPI transformer availability, and generated SQL; all endpoint contract checks are **Not run**. The detailed scenario matrix is in [[research/2/04-verification-and-documentation|Part 4]].
