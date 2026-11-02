---
title: "4 - Observability: Phase 2 - Tracing and Metrics Instrumentation"
tags: [plans, service-scheduler, observability, opentelemetry, tracing, metrics]
ticket: 4
status: implemented
updated: 2026-11-02
---

# Phase 2 - Tracing and metrics instrumentation

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/01-instrumentation-and-correlation|Instrumentation and correlation]].
- Plan overview: [[plans/4/README|Ticket 4 implementation plan]].
- Requirements: S4-02, S4-03, S4-05; AC4-01, AC4-02, AC4-05, AC4-07; TC4-03 trace-ID integration, TC4-04, and the unit portion of TC4-07; project P-01/P-02 constraints.
- Dependencies: Phase 1 logging/correlation foundation and pinned OpenTelemetry packages.

## Scope and intended behavior

This phase adds conditional OpenTelemetry tracing and metrics with OTLP/gRPC export. The provider is created only when `Scheduler:Telemetry:Enabled` is true. Resource attributes, parent-based sampling, ASP.NET Core/Runtime/SqlClient instrumentation, a custom `ServiceScheduler` activity source/meter, bounded Views, span sanitization, and the custom availability/allocation/cache spans and instruments are implemented.

The phase does not add Collector/Prometheus/Tempo/Grafana containers or end-to-end delivery checks; it verifies the local contracts with `ActivityListener`/`MeterListener` and a telemetry-enabled API host whose exporter endpoint may be unreachable. Phase 3 wires the backend, and Phase 4 proves delivery.

Exclusions: no EF Core instrumentation, no StackExchange.Redis instrumentation package, no custom propagator, no telemetry health check, no change to booking/cache decisions or budgets.

## Instrumentation contract

### Provider and resources

| Concern | Planned behavior |
| --- | --- |
| Registration | `AddOpenTelemetry()` only when `Scheduler:Telemetry:Enabled` is true; otherwise no provider/exporter is created. |
| Resource | `service.name` from `ServiceName`, `service.version` from `ServiceVersion`, `service.instance.id` from `ServiceInstanceId`, `deployment.environment.name` from `IHostEnvironment.EnvironmentName`. |
| Sampler | `ParentBasedSampler(new TraceIdRatioBasedSampler(SamplingRatio))`; Compose/tests use `1.0`; incoming `traceparent` flags `01` are required to keep a parent-sampled trace. |
| Propagator | OpenTelemetry SDK default W3C TraceContext + Baggage. |
| Trace exporter | OTLP/gRPC to `Scheduler:Telemetry:OtlpEndpoint`; batch delay 1,000 ms, export timeout 5,000 ms, queue 2,048, batch 512, OTLP timeout 3,000 ms. |
| Metrics exporter | OTLP/gRPC to the same endpoint; periodic export interval 5,000 ms, timeout 3,000 ms. |
| Instrumentation | `AddAspNetCoreInstrumentation`, `AddSqlClientInstrumentation`, `AddRuntimeInstrumentation`, `AddSource("ServiceScheduler")`, `AddMeter("ServiceScheduler")`. |
| Views | `http.server.request.duration` with HTTP-recommended buckets and bounded tag keys; `scheduler.booking.allocation.duration` with the allocation buckets and `outcome`; defensive counter Views restricted to declared labels. |
| Health filtering | ASP.NET Core instrumentation filters `/health/live` and `/health/ready`; the request middleware already skips them in Phase 1. |
| Span sanitization | Remove `url.path`, `url.query`, `user_agent.original`, `server.address`, `server.port`, and `http.request.method_original` from server spans; keep `http.route`; never enable SQL parameter or header capture. |
| SQL capture | SqlClient covers EF-generated commands and the direct `dbo.CreateAppointment` command; no EF Core package is added. |

### Custom spans

| Span | Source / parent | Attributes and status |
| --- | --- | --- |
| `availability.evaluate` | `AvailabilityService.EvaluateAsync`; parent is the HTTP server span or current activity. | `availability.result` = `available`, `unavailable`, `not_found`, `invalid_interval`, or `error`; status Error only for an unexpected exception. No IDs. |
| `booking.allocate` | `BookingAllocator.AllocateAsync`; parent is the HTTP server span or current activity. | `booking.outcome` = `succeeded`, `capacity_unavailable`, `dependency_unavailable`, `validation_error`, or `internal_error`; `booking.attempted_pairs`, `booking.deadlock_retries`; status Error for dependency/internal only. |
| `cache.get` / `cache.set` / `cache.remove` | `BoundedCache` per request operation; parent is the current activity. | `cache.name=service_catalog`, `cache.operation`, `cache.result` (`hit`, `miss`, `success`, `failure`), `cache.budget_ms`; no key/ID; status Error for failure; cancellation is not failure. |

### Custom instruments

| Instrument | Type / unit | Labels | Recording point |
| --- | --- | --- | --- |
| `scheduler.http.requests` | Counter&lt;long&gt;, `1` | `route`, `method`, `status_class`, `outcome` | Request-completion middleware, non-health requests; `route` is the route template or `unmatched`; `outcome` is the Problem code or `none`. |
| `scheduler.booking.outcomes` | Counter&lt;long&gt;, `1` | `outcome` | `AppointmentsController.CreateAsync` after the documented outcome is known, including request-shape/`startsAt` validation failures; `created`, `capacity_unavailable`, `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`. |
| `scheduler.booking.procedure.attempts` | Counter&lt;long&gt;, `1` | `result` | `SqlAppointmentBookingProcedure.ExecuteAsync` per call; `succeeded`, `technician_conflict`, `bay_conflict`, `invalid_request`, `resource_not_eligible`, `coverage_unavailable`, `timezone_mapping_invalid`, `deadlock`, `lock_timeout`, `command_timeout`, `dependency_unavailable`, `unexpected`; cancellation is not counted. |
| `scheduler.cache.operations` | Counter&lt;long&gt;, `1` | `operation`, `result` | `BoundedCache` request operations; `get` results `hit`/`miss`/`failure`; `set`/`remove` results `success`/`failure`; cancellation is not counted; health-check reads are excluded. |
| `scheduler.booking.allocation.duration` | Histogram&lt;double&gt;, `s` | `outcome` | `BookingAllocator.AllocateAsync` once per allocation; outcome vocabulary as the allocation result. |
| `http.server.request.duration` | Histogram&lt;double&gt;, `s` | Bounded View: `http.request.method`, `http.route`, `http.response.status_code` | ASP.NET Core instrumentation; health requests filtered. |

### Bounded vocabularies

| Label | Allowed values |
| --- | --- |
| `route` / `http_route` | Controller route templates and `unmatched`; never a raw path. |
| `method` / `http_request_method` | HTTP method, normalized by the instrumentation. |
| `status_class` | `1xx`, `2xx`, `3xx`, `4xx`, `5xx`. |
| `http_response_status_code` | Numeric status code from the documented API contract. |
| `outcome` (request) | Problem code or `none`. |
| `outcome` (booking/allocation) | `created` or `succeeded`, `capacity_unavailable`, `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`. |
| `result` (procedure) | The twelve-value vocabulary above. |
| `operation` | `get`, `set`, `remove`. |
| `result` (cache) | `hit`, `miss`, `success`, `failure`. |

No entity ID, VIN, email, raw URL/query, trace/span ID, SQL parameter value, or traceparent value is a metric label or span attribute.

### Recording decisions

- The request middleware skips `/health/*` for both the log event and `scheduler.http.requests`; the ASP.NET Core instrumentation filter excludes the same paths from server spans and `http.server.request.duration`.
- `BoundedCache` keeps its existing 250 ms/1 s budgets and fallback. Request operations record `cache.get/set/remove` activities and counters; `CacheHealthCheck` calls a non-recording path so health pings do not enter the catalog hit-ratio denominator.
- `scheduler.booking.outcomes` is recorded at the controller so request-shape/`startsAt` validation failures are counted. If an exception escapes the controller, the request metric carries `outcome=internal_error` or `dependency_unavailable`; the booking counter does not double count it.
- `scheduler.booking.procedure.attempts` records `dependency_unavailable` for a classified connection-open failure and `unexpected` for an unmapped exception before rethrowing; cancellation is never counted as a failure.
- The transport-level cache hit ratio treats a corrupt cached payload as a `get` hit before the corrupt-payload event triggers SQL fallback; dashboards and documentation state this meaning.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 2.1 | S4-02, S4-03, S4-05 | New `Infrastructure/Telemetry/ServiceSchedulerTelemetry.cs` | Add the `ServiceScheduler` activity source and meter, instrument instances, span names, attribute keys, and outcome vocabularies as constants/helpers. | The source/meter names and instrument names match the contract; custom calls are safe no-ops when no listener is registered. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.2 | S4-02, S4-03, S4-05 | New `Infrastructure/Telemetry/TelemetryRegistration.cs`; `Program.cs` | Register resource, sampler, OTLP trace/metric exporters with bounded options, ASP.NET Core/SqlClient/Runtime instrumentation, custom source/meter, Views, health filter, and server-span sanitization only when enabled. | Telemetry-disabled hosts create no provider/exporter; enabled hosts build successfully; unit/integration tests observe the expected resource/sampler/export settings through code inspection and listener behavior. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.3 | S4-02 | `AvailabilityService.cs` | Wrap `EvaluateAsync` in an `availability.evaluate` activity with a bounded result tag for evaluated/not-found/invalid-interval/error paths. | Unit listener tests cover the telemetry helper/allocation/cache paths; an integration listener test using the existing SQL fixture observes one `availability.evaluate` activity per evaluation, the correct result value, and no entity IDs; decisions are unchanged. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.4 | S4-02, S4-03, S4-05 | `BoundedCache.cs`, `CacheHealthCheck.cs` | Add `cache.get/set/remove` activities and `scheduler.cache.operations` counters with bounded results/budgets; keep the existing log event and budgets; expose a non-recording read path for the health check. | Fake-cache unit tests cover hit/miss/set success/set failure/remove success/remove failure/timeout/Redis failure/cancellation; health reads do not increment the catalog counter; fallback and budgets are unchanged. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.5 | S4-02, S4-03, S4-05 | `BookingAllocator.cs`, `SqlAppointmentBookingProcedure.cs`, `AppointmentsController.cs` | Add the `booking.allocate` activity, allocation duration histogram, procedure-attempt counter, and controller booking-outcome counter with the bounded vocabularies. Keep retry/locking/decision logic unchanged; wrap connection-open classification for the attempt metric. | Listener tests cover allocation success/capacity/dependency/internal, procedure mapped/unmapped/cancellation cases, early controller validation, and final outcomes; no IDs/keys/parameters are recorded; budgets/retries are unchanged. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.6 | S4-02, S4-05 | `ProblemDetailsSchemaTransformer.cs` description; telemetry-enabled API integration test (new test under `Startup/` or `Telemetry/`) | Verify the Phase 1 trace-ID resolver end-to-end with OpenTelemetry enabled and an unreachable exporter endpoint. | A 404/400/409/503 response carries a 32-lowercase-hex `traceId` when an activity exists; the API still starts and responds with the exporter unreachable; no raw path is logged. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 2.7 | S4-06 | `docs/test/4-test.md` | Record Phase 2 unit/integration commands, observed results, and any Blocked checks. | Executed evidence is recorded with revision/environment; unexecuted checks remain explicit. | Implemented 2026-11-02 (see [[test/4-test]]) |

## Verification

| Case | Command / scenario | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| TC4-04 Custom spans/metrics | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Telemetry"`; integration listener test for availability | `ActivityListener`/`MeterListener` observe allocation/cache spans and instruments in unit tests and the availability span against real SQL; declared names, labels, and outcomes match; no keys/IDs; cancellation is not a failure. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-07 Unit outcomes/labels | Same unit filter, including booking and cache tests | Booking success/capacity/dependency/validation and cache hit/miss/success/failure are distinguishable; only bounded label vocabularies are present. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-03 Telemetry-enabled trace ID | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TelemetryTrace"` (planned class name) | Problem Details `traceId` is 32 lowercase hex and matches the server activity; fallback remains non-empty when telemetry is disabled. | Not run (plan-time record; see [[test/4-test]]) |
| Telemetry-disabled regression | Full existing unit/integration suites with `Scheduler:Telemetry:Enabled` unset | No provider/exporter is created; booking/cache/readiness behavior and existing tests are unchanged. | Not run (plan-time record; see [[test/4-test]]) |
| Build/format | `dotnet restore`; `dotnet build --no-restore`; `dotnet format --verify-no-changes` | Build succeeds with no warnings/errors; formatting is unchanged. | Not run (plan-time record; see [[test/4-test]]) |

## Exit criteria

- OpenTelemetry registers only when enabled and contains the planned resource, sampler, bounded OTLP exporters, ASP.NET Core/Runtime/SqlClient instrumentation, custom source/meter, Views, health filter, and span sanitization.
- Availability, allocation, procedure-attempt, cache, and request/booking metrics match the bounded contract; health traffic and cache-health probes are excluded.
- Telemetry-disabled behavior and existing booking/cache/readiness tests remain unchanged.
- Phase 2 checks pass and `docs/test/4-test.md` records actual commands/results.
- No Collector/Prometheus/Tempo/Grafana delivery or dashboard result is claimed from this phase.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: instrumentation only; key/payload/TTL/budgets/fallback unchanged; health reads excluded from the operation counter.
- Booking: instrumentation only; procedure/locking/retry/budget unchanged; `unexpected` is a metric bucket, not a new `BookingProcedureStatus`.
- Configuration: telemetry export/sampling/resource options are consumed; no secret.
- Remaining decision: the exact OpenTelemetry API overloads for batch/metric-reader options must be verified against the pinned packages during implementation; the plan fixes the intended values, not unverified call signatures.
- Remaining decision: metric translation names are asserted in Phase 4; dashboards may need adjustment if the pinned Prometheus translation differs.
