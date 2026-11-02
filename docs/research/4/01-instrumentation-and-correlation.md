---
title: "4 - Observability: Instrumentation and Correlation"
tags: [research, service-scheduler, observability, serilog, opentelemetry, tracing, metrics]
ticket: 4
status: verified
updated: 2026-11-02
---

# 4 - Instrumentation and correlation

- Ticket: [[4 - Observability|Ticket 4]]
- Research overview: [[research/4/00-overview|Ticket 4 research overview]]
- Requirements: S4-01, S4-02, S4-03, S4-05; AC4-01, AC4-02, AC4-05, AC4-06, AC4-07; T-13, T-15, S4-V01, S4-V02.
- Evidence baseline: HEAD `92a81efde5dac1070669fff13193e452dc9b6529` with a clean working tree; inspected application, cache, booking, error, health, test, package, and configuration files. Research-phase package/image metadata and configuration probes are recorded where used; they are not ticket tests.

> [!IMPORTANT]
> No Serilog, OpenTelemetry, telemetry options, request middleware, custom activities, or metric instruments exist. The contracts below are proposed. Every verification item in this part is **Not run**.

## Existing evidence

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| The API has no Serilog or OpenTelemetry package reference. | `Directory.Packages.props` and `src/ServiceScheduler.Api/ServiceScheduler.Api.csproj`; no Serilog/OpenTelemetry entries. | New package pins are required. |
| The composition root has no telemetry registration or request middleware. | `Program.cs`: controllers, Problem Details, OpenAPI, options, DbContext, cache, services, health, endpoints; no `AddSerilog`, `AddOpenTelemetry`, or custom middleware. | `Program.cs` is a Must Change entry point. |
| Problem Details `traceId` uses `Activity.Current?.Id` or `HttpContext.TraceIdentifier`. | `Program.cs` `CustomizeProblemDetails` and `InvalidModelStateResponseFactory`; `ProblemResponse.Create`; `SqlDependencyExceptionHandler.TryHandleAsync`. | Correlation exists but `Activity.Id` is a traceparent string, not a bare trace ID; normalize to the W3C trace ID. |
| The request pipeline currently produces no single structured request event. | `Program.cs` endpoint mapping only; default framework logging. | Add a custom request-completion middleware; do not rely on Serilog's default request middleware because it captures `RequestPath`. |
| Existing cache/dependency events use `ILogger` and include some IDs or raw paths. | `BoundedCache.cs:123` (cache key); `ServiceCatalogService.cs:55,61` (dealership ID); `AppointmentService.cs:89,97` (dealership ID); `SqlDependencyExceptionHandler.cs:31` (method/path/trace ID); `BookingAllocator.cs`/`SqlAppointmentBookingProcedure.cs` (bounded counts/status). | Keep useful events, remove IDs/raw path, add bounded fields and trace enrichment. |
| The cache wrapper enforces a 250 ms caller budget and falls back to SQL. | `BoundedCache.TryGetAsync`/`CompleteAsync`; `CacheOptions`; `CacheHealthCheck`. | Instrumentation must not add waits or change fallback; activity/metric calls are synchronous and local. |
| EF reads and the direct booking command both use `Microsoft.Data.SqlClient` 6.1.7. | `Program.cs` `UseSqlServer`; `SqlAppointmentBookingProcedure` direct `SqlCommand`; transitive package listing. | SqlClient instrumentation covers both paths; no EF Core instrumentation is needed. |
| Booking has bounded candidate/deadlock/retry/timeout behavior. | `BookingAllocator.cs`, `BookingOptions`, `SqlAppointmentBookingProcedure.cs`. | Telemetry records outcomes and attempts without changing decisions or retry counts. |
| Existing tests use a capturing `ILoggerProvider` and real SQL/Redis containers. | `CapturingLoggerProvider.cs`, `SchedulerApiFactory.cs`, `SqlServerTestBase.cs`, `CacheSqlServerTestBase.cs`. | Serilog must coexist with test-added providers; listeners can test instruments without new exporters. |

## Package decisions

| Package | Proposed version | Target / dependency evidence | Purpose |
| --- | --- | --- | --- |
| `Serilog.AspNetCore` | `10.0.0` | NuGet metadata declares `net10.0`; depends on Serilog 4.3.0, Serilog.Formatting.Compact 3.0.0, Serilog.Settings.Configuration 10.0.0, Serilog.Sinks.Console 6.1.1. | Host/DI integration, console bootstrap, request logging support (request middleware is custom). |
| `Serilog.Formatting.Compact` | `3.0.0` | NuGet metadata; latest stable. | `CompactJsonFormatter` JSON console output. |
| `Serilog.Sinks.Console` | `6.1.1` | NuGet metadata; latest stable. | Console sink (also transitive). |
| `OpenTelemetry` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable (1.21.0-rc.1 is prerelease). | SDK, resources, samplers. |
| `OpenTelemetry.Extensions.Hosting` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable. | `AddOpenTelemetry()` hosting integration. |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable. | OTLP trace/metric export. |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable. | HTTP server spans and `http.server.request.duration` metric. |
| `OpenTelemetry.Instrumentation.Runtime` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable. | Runtime/process metrics. |
| `OpenTelemetry.Instrumentation.SqlClient` | `1.20.1` | NuGet metadata declares `net10.0`; latest stable; README states Microsoft.Data.SqlClient 4.0+ works (v3.x incompatible). | SQL spans for EF queries and the direct procedure command. |
| `Testcontainers` (test project) | `4.14.0` | Existing module version; base package for non-module containers. | Telemetry stack fixtures. |

Rejected or deferred packages:

| Package / option | Reason |
| --- | --- |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` `1.20.1-beta.1` | Prerelease; SqlClient already emits spans for EF-generated commands. Adding both creates nested/duplicate query spans without adding required evidence. |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` `1.20.1-beta.1` | Prerelease; explicit cache activities/metrics in `BoundedCache` provide bounded, controlled cache evidence. |
| `OpenTelemetry.Instrumentation.Http` | The API makes no outbound HTTP calls in the initial release. |
| `OpenTelemetry.Exporter.InMemory` | Not needed; unit tests can use `ActivityListener`/`MeterListener`, and end-to-end tests query real backends. |
| Serilog `10.0.1-dev-*` / OpenTelemetry `1.21.0-rc.1` | Prerelease; the project pins stable versions. |
| `Serilog.Enrichers.Span` | A small custom `ActivityLogEnricher` gives exact field names and avoids an extra dependency; behavior is covered by unit tests. |

> [!NOTE]
> Package metadata was inspected from NuGet and confirms `net10.0` assets. `dotnet restore`/`dotnet build` are implementation checks; research does not claim a successful build with the proposed package graph.

## Logging contract

### Configuration and provider

- Use Serilog as the API logging provider: `builder.Logging.ClearProviders()` followed by `builder.Services.AddSerilog((services, configuration) => ...)`.
- The `AddSerilog` provider remains one provider in the `ILoggerFactory`; test-added `ILoggerProvider` instances (for example `CapturingLoggerProvider`) can still receive events, preserving existing cache/dependency log assertions.
- Configure a JSON console sink with `Serilog.Formatting.Compact.CompactJsonFormatter` in all API environments. Container stdout remains the primary log access path; no file sink or centralized log storage is added.
- Apply log-level overrides so the single request event and domain/dependency events are visible without EF/ASP.NET noise: `Microsoft.AspNetCore` Warning, `Microsoft.EntityFrameworkCore` Warning, `StackExchange.Redis` Warning; default Information. Keep the existing `appsettings.json` `Logging` section as the `ILoggerFactory` filter and document that Serilog is the API sink. `appsettings.Development.json` currently sets `Microsoft.AspNetCore` to Information, so apply the override there as well or use Serilog `MinimumLevel.Override` so Development does not reintroduce framework noise.
- Do not call `UseSerilogRequestLogging`; use the custom request-completion middleware below so `RequestPath` is never captured.
- Setup mode (`--db-setup`) runs before web-host construction and keeps its separate simple console logger; it is outside the request telemetry pipeline. Document this as a scope boundary, not a missing implementation.

### Stable application identity

| Field | Source | Example |
| --- | --- | --- |
| `Application` / `service.name` | `Scheduler:Telemetry:ServiceName` default `unified-service-scheduler-api` | `unified-service-scheduler-api` |
| `ApplicationVersion` / `service.version` | `Scheduler:Telemetry:ServiceVersion` or assembly informational version | build version |
| `Environment` / `deployment.environment.name` | `IHostEnvironment.EnvironmentName` | `Development` |
| `service.instance.id` | `Scheduler:Telemetry:ServiceInstanceId` default `scheduler-api` | `scheduler-api` |

Serilog adds `Application`, `Environment`, and `ApplicationVersion` to every event. OpenTelemetry adds `service.name`, `service.version`, `service.instance.id`, and `deployment.environment.name` to the resource. No `service.namespace` is proposed, so Prometheus maps `service.name` directly to `job` and `service.instance.id` to `instance`.

### Trace/span enrichment

`ActivityLogEnricher` reads `Activity.Current` and adds these properties when an activity exists:

| Log property | Source | Notes |
| --- | --- | --- |
| `TraceId` | `Activity.Current.TraceId.ToHexString()` | 32 lowercase hex; matches Tempo trace ID and Problem Details `traceId`. |
| `SpanId` | `Activity.Current.SpanId.ToHexString()` | 16 lowercase hex for the current span; may be a custom child span. |
| `TraceFlags` | `Activity.Current.ActivityTraceFlags` | Useful for sampled/unsampled diagnosis. |

The enricher does not add the trace ID if no activity exists; the request-completion middleware then has no trace fields, and Problem Details falls back to `HttpContext.TraceIdentifier`. This fallback is only for telemetry-disabled/startup cases and is not presented as a W3C trace ID.

### Request-completion middleware

Place `RequestLoggingMiddleware` before `UseExceptionHandler()` so it observes the final response status after Problem Details handling. It logs exactly one completion event per request:

| Property | Value / rule |
| --- | --- |
| `EventName` | `RequestCompleted` |
| `RequestMethod` | HTTP method (bounded). |
| `RouteTemplate` | `HttpContext.GetEndpoint()?.Metadata.GetMetadata<RouteEndpoint>()?.RoutePattern.RawText`; `unmatched` when no endpoint. Never the raw path. |
| `StatusCode` | Final status code. |
| `StatusClass` | `1xx` through `5xx` derived from status. |
| `Outcome` | Problem Details `code` from `HttpContext.Items` when present; otherwise `ProblemCodes.ForStatus(StatusCode)` when the status is in the contract; otherwise `none`. Bounded application vocabulary. |
| `ElapsedMilliseconds` | Elapsed time. |
| `TraceId`, `SpanId`, `TraceFlags` | Added by the enricher. |
| `Application`, `Environment`, `ApplicationVersion` | Added by Serilog enrichment. |

Level policy: 5xx -> `Error`, 4xx -> `Warning`, 2xx/3xx -> `Information`. Do not include raw path, query string, headers, request body, customer/vehicle IDs, VINs, appointment IDs, or trace IDs in the message template/properties (trace/span fields are correlation properties, not metric labels). The request-completion event and the domain/dependency events are complementary: the completion event records route/status/outcome, while the dependency event records the cause; do not add a second identical failure message at controller/service/exception-handler layers.

Unexpected exceptions that escape the exception handler are caught by the middleware, logged once at Error with the exception object and `Outcome=internal_error`, and rethrown. Classified SQL failures are logged by `SqlDependencyExceptionHandler` (not duplicated by the request middleware).

### Problem Details trace ID and outcome item

- Add `ProblemCodes.HttpContextItemKey = "scheduler.problem.code"`.
- `ProblemResponse.Create`, the `CustomizeProblemDetails` callback, the `InvalidModelStateResponseFactory`, and `SqlDependencyExceptionHandler` set the item to the bounded Problem code.
- The request-completion middleware reads this item for `Outcome`; this avoids parsing response bodies and keeps the outcome vocabulary aligned with the API contract.
- Change `traceId` values to `Activity.Current?.TraceId.ToHexString() ?? httpContext.TraceIdentifier` in all four producers. Keep the `traceId` property name and non-empty fallback.
- Update `ProblemDetailsSchemaTransformer` description to say the value is a W3C trace ID (32 lowercase hex) when telemetry is active and is used to find the request in Tempo; logs carry the same `TraceId` and a `SpanId`.
- No new Problem Details property (for example `spanId`) is required by the ticket; the span ID remains a log/trace field.

### Existing log call-site sanitization

| File / symbol | Current issue | Proposed event |
| --- | --- | --- |
| `BoundedCache.LogDegradation` | Message logs cache key (dealership GUID). | `EventName=CacheOperation`, `CacheName=service_catalog`, `CacheOperation`, `CacheResult=failure`, `BudgetMilliseconds`; keep exception; no key/ID. |
| `ServiceCatalogService` corrupt/null payload logs | Messages log dealership ID. | `EventName=CacheCorruptPayload` / `CachePayloadInvalid`, `CacheName=service_catalog`; no key/ID; remove key as today. |
| `AppointmentService` unknown/invalid IANA zone logs | Messages log dealership ID. | `EventName=BookingConfigurationError`, `ConfigurationFailure=unknown_time_zone|invalid_time_zone`; route/trace identifies request; no ID. |
| `BookingAllocator` events | Counts/status are bounded but unstructured. | `EventName=BookingAllocation`, `BookingOutcome`, `AttemptedPairs`, `DeadlockRetries`; no IDs. |
| `SqlAppointmentBookingProcedure` mapped error | SQL number/status only. | `EventName=BookingProcedureAttempt`, `SqlErrorNumber`, `ProcedureResult`; no IDs/parameters. |
| `SqlDependencyExceptionHandler` | Logs raw method/path and trace ID. | `EventName=DependencyUnavailable`, `RequestMethod`, `RouteTemplate` (never path), `StatusCode=503`; trace ID via enricher. |
| `DatabaseSetupRunner` | Simple console setup logs. | No change; setup is outside the API telemetry pipeline. |

No log event includes request bodies, response bodies, connection strings, passwords, VINs, customer contact data, appointment IDs, or raw URLs. Exception objects may contain dependency error messages; they must not contain credentials or customer payloads.

## Tracing contract

### Registration and resources

- Register OpenTelemetry only when `Scheduler:Telemetry:Enabled` is true. When false, no provider/exporter is created; custom `ActivitySource`/`Meter` calls remain no-ops for listeners.
- Resource: `service.name`, `service.version`, `service.instance.id`, `deployment.environment.name` as above.
- Sampler: `ParentBasedSampler(new TraceIdRatioBasedSampler(SamplingRatio))`. Compose uses `1.0`; lower ratios intentionally sample. Incoming `traceparent` sampled flag `01` is required for parent-based sampling to keep the trace.
- Propagator: the OpenTelemetry SDK default W3C TraceContext + Baggage; no custom propagator is required.
- Trace exporter: OTLP/gRPC to the Collector; batch processor with bounded options (see outage section).
- Metrics exporter: OTLP/gRPC to the Collector; periodic reader with bounded interval/timeout.
- Instrumentation: `AddAspNetCoreInstrumentation`, `AddSqlClientInstrumentation`, `AddRuntimeInstrumentation`, custom `AddSource("ServiceScheduler")`, `AddMeter("ServiceScheduler")`.
- No EF Core and no StackExchange.Redis instrumentation packages.

### Span boundaries and attributes

| Span | Source | Parent | Attributes / status |
| --- | --- | --- | --- |
| HTTP server span | ASP.NET Core instrumentation | Incoming `traceparent` or root | `http.request.method`, `http.route`, `http.response.status_code`, `url.scheme`, `network.protocol.version`, `error.type`; remove `url.path`, `url.query`, `user_agent.original`, `server.address`, `server.port`, `http.request.method_original` in `EnrichWithHttpResponse`. |
| SQL query / procedure span | SqlClient instrumentation | Current activity (HTTP or booking) | `db.system.name`, `db.namespace`, `db.operation.name`, `db.query.text`/`db.stored_procedure.name`, `db.response.status_code`, `error.type`; no parameters unless the experimental environment variable is set (it must remain unset). |
| `availability.evaluate` | Custom `AvailabilityService` activity | HTTP server span | `availability.result` = `available`, `unavailable`, `not_found`, `invalid_interval`, or `error`; status Error only for unexpected exception. |
| `booking.allocate` | Custom `BookingAllocator` activity | HTTP server span | `booking.outcome` = `succeeded`, `capacity_unavailable`, `dependency_unavailable`, `validation_error`, `internal_error`; `booking.attempted_pairs`, `booking.deadlock_retries`; status Error for dependency/internal, not expected capacity/validation. |
| `cache.get` / `cache.set` / `cache.remove` | Custom `BoundedCache` activities | HTTP server span or custom service span | `cache.name=service_catalog`, `cache.operation`, `cache.result` (`hit`, `miss`, `success`, `failure`), `cache.budget_ms`; no key/ID; status Error for failure; cancellation does not become a failure. |

Span names use dotted lowercase business names; the ActivitySource is `ServiceScheduler`. Custom error status descriptions use bounded outcome strings, not exception messages. `Activity.Current` is allowed to be a custom child span when a service log is emitted; the trace ID stays the same.

### Sensitive-data controls in spans

- ASP.NET Core instrumentation emits `url.path` (raw path) and `url.query` (redacted by default). Both are removed in `EnrichWithHttpResponse` using `activity.SetTag(name, null)`; `user_agent.original`, `server.address`, and `server.port` are also removed to keep the span bounded. `http.route` is the route template, not the raw path.
- SQL parameters are not captured by default. Do not set `OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_ENABLE_TRACE_DB_QUERY_PARAMETERS`, do not enable returned-row capture, and do not enable HTTP header capture. `db.query.text` contains parameterized SQL text, not parameter values; it is retained for diagnosis.
- Custom cache spans never record the cache key (which contains a dealership GUID).
- Custom availability/allocation spans record counts/outcomes, not dealership/customer/vehicle/service/appointment IDs.
- No trace ID is used as a metric label or span attribute; trace IDs are correlation identifiers in logs and Tempo.

### Correlation mapping

| Signal | Value | Where |
| --- | --- | --- |
| W3C trace ID | 32 lowercase hex | `Activity.Current.TraceId`; Tempo trace ID; Problem Details `traceId`; Serilog `TraceId`. |
| Span ID | 16 lowercase hex | `Activity.Current.SpanId`; Serilog `SpanId`; Tempo span ID. |
| Traceparent | `00-<traceId>-<spanId>-<flags>` | Incoming/outgoing HTTP header; not exposed as a metric label. |
| Problem code | `validation_error`, `not_found`, `capacity_unavailable`, `dependency_unavailable`, `internal_error` | Problem Details `code`; request log `Outcome`; booking metric outcome where applicable. |
| Application identity | `service.name`, version, environment, instance | OTel resource; Serilog `Application`, `ApplicationVersion`, `Environment`. |

A generated request can be correlated by taking the log `TraceId` (or Problem Details `traceId`) and querying Tempo `GET /api/v2/traces/{traceId}` in Grafana Explore. The log request event uses the route template, not the raw path; the Tempo trace contains the HTTP/SQL/custom spans.

### Sampling limitation

With `SamplingRatio=1.0`, every trace with a sampled parent is recorded. With a lower ratio, ordinary request traces may be absent even though metrics/logs exist; a smoke test must use ratio `1.0` and a `traceparent` sampled flag of `01`. Parent-based sampling can also drop a trace whose incoming parent is unsampled. Document this in the setup/deploy notes rather than promising every request trace at lower ratios.

## Metrics contract

### Meter and instruments

All custom instruments use the `ServiceScheduler` meter. Units use `1` for counters (dimensionless) and `s` for histograms, so the Prometheus translation appends `_total`/`_seconds` predictably.

| OTel instrument | Type | Unit | Labels | Recording point |
| --- | --- | --- | --- | --- |
| `scheduler.http.requests` | Counter&lt;long&gt; | `1` | `route`, `method`, `status_class`, `outcome` | Request-completion middleware (all requests, including `unmatched`). |
| `scheduler.booking.outcomes` | Counter&lt;long&gt; | `1` | `outcome` | `AppointmentsController.CreateAsync` after `CreateAsync` returns; `created`, `capacity_unavailable`, `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`. |
| `scheduler.booking.procedure.attempts` | Counter&lt;long&gt; | `1` | `result` | `SqlAppointmentBookingProcedure.ExecuteAsync` per procedure call; `succeeded`, `technician_conflict`, `bay_conflict`, `invalid_request`, `resource_not_eligible`, `coverage_unavailable`, `timezone_mapping_invalid`, `deadlock`, `lock_timeout`, `command_timeout`, `dependency_unavailable`, `unexpected`. Cancellation is not counted as a failure. |
| `scheduler.cache.operations` | Counter&lt;long&gt; | `1` | `operation`, `result` | `BoundedCache`; `get` results `hit`/`miss`/`failure`; `set`/`remove` results `success`/`failure`; cancellation is not counted as a failure. |
| `scheduler.booking.allocation.duration` | Histogram&lt;double&gt; | `s` | `outcome` | `BookingAllocator.AllocateAsync` once per allocation; same outcome vocabulary as the final booking result. |
| `http.server.request.duration` (built-in) | Histogram&lt;double&gt; | `s` | Bounded View to `http.request.method`, `http.route`, `http.response.status_code` | ASP.NET Core instrumentation. |

### Histogram buckets and Views

- `http.server.request.duration` uses the stable HTTP recommended buckets: `0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10` seconds. Add an `AddView` with those boundaries and `TagKeys = ["http.request.method", "http.route", "http.response.status_code"]` to drop `url.scheme`, server address/port, protocol version, and other attributes.
- `scheduler.booking.allocation.duration` uses `0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20` seconds and `TagKeys = ["outcome"]`.
- Add defensive Views for the custom counters with only their declared label keys, so a future tag cannot silently add cardinality.
- All histograms are explicit-bucket; no native histogram or exemplar requirement. Aggregation in Prometheus is cumulative; dashboards use `rate(...)` over `$__rate_interval` and `histogram_quantile`.

### Bounded label vocabularies

| Label | Allowed values |
| --- | --- |
| `route` / `http_route` | Controller route templates and `unmatched`; never raw paths. |
| `method` / `http_request_method` | HTTP methods; unknown methods normalized by the instrumentation. |
| `status_class` | `1xx`, `2xx`, `3xx`, `4xx`, `5xx`. |
| `http_response_status_code` | Numeric status code (bounded set). |
| `outcome` (request) | Problem code or `none`. |
| `outcome` (booking/allocation) | `created` or `succeeded` as applicable; `capacity_unavailable`, `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`. |
| `result` (procedure) | The procedure-result vocabulary above, capped at 12 values. |
| `operation` | `get`, `set`, `remove`. |
| `result` (cache) | `hit`, `miss`, `success`, `failure`. |

No `appointment_id`, `customer_id`, `vehicle_id`, `dealership_id`, `service_type_id`, `technician_id`, `service_bay_id`, `trace_id`, `span_id`, raw URL, query string, VIN, email, or traceparent value is a metric attribute.

### Expected Prometheus names and PromQL

With default `UnderscoreEscapingWithSuffixes` translation:

| OTel name | Expected Prometheus series |
| --- | --- |
| `scheduler.http.requests` | `scheduler_http_requests_total` |
| `scheduler.booking.outcomes` | `scheduler_booking_outcomes_total` |
| `scheduler.booking.procedure.attempts` | `scheduler_booking_procedure_attempts_total` |
| `scheduler.cache.operations` | `scheduler_cache_operations_total` |
| `scheduler.booking.allocation.duration` | `scheduler_booking_allocation_duration_seconds_bucket`, `_count`, `_sum` |
| `http.server.request.duration` | `http_server_request_duration_seconds_bucket`, `_count`, `_sum` |

Proposed PromQL:

| Panel | PromQL |
| --- | --- |
| Request rate | `sum by (route, method) (rate(scheduler_http_requests_total[$__rate_interval]))` |
| 5xx error ratio | `sum(rate(scheduler_http_requests_total{status_class="5xx"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_http_requests_total[$__rate_interval])), 1e-9)` |
| HTTP p95 latency | `histogram_quantile(0.95, sum by (le, http_route, http_request_method) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))` |
| Booking outcomes | `sum by (outcome) (rate(scheduler_booking_outcomes_total[$__rate_interval]))` |
| Procedure attempts | `sum by (result) (rate(scheduler_booking_procedure_attempts_total[$__rate_interval]))` |
| Allocation p95 | `histogram_quantile(0.95, sum by (le, outcome) (rate(scheduler_booking_allocation_duration_seconds_bucket[$__rate_interval])))` |
| Cache hit ratio | `sum(rate(scheduler_cache_operations_total{operation="get", result="hit"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_cache_operations_total{operation="get", result=~"hit\|miss"}[$__rate_interval])), 1e-9)` |
| Cache get failures | `sum(rate(scheduler_cache_operations_total{operation="get", result="failure"}[$__rate_interval]))` |

The hit ratio denominator excludes failures; failure rate is a separate panel. This distinguishes "no traffic" from "cache outage": both the operations panel and the failure panel are inspected. Zero-traffic display rules are in Part 2.

## Bounded processing and outage behavior

| Concern | Proposed bound |
| --- | --- |
| OTLP exporter timeout | 3,000 ms (`OtlpExporterOptions.TimeoutMilliseconds`). |
| Trace batch scheduled delay | 1,000 ms. |
| Trace batch exporter timeout | 5,000 ms. |
| Trace batch queue / batch size | 2,048 / 512. |
| Metric export interval / timeout | 5,000 ms / 3,000 ms. |
| Collector batch | 2 s, 512 spans/metrics, max 1,024. |
| Collector memory limiter | 128 MiB limit, 32 MiB spike. |
| Collector sending queue / retry window | 1,000 items, 2 consumers, 30 s maximum retry window. |
| Sampling | Parent-based ratio; Compose `1.0`; lower values documented as trace-visibility limits. |

Behavior:

- With `Scheduler:Telemetry:Enabled=false`, no OpenTelemetry provider or exporter is registered; Serilog logs still work; custom ActivitySource/Meter calls are cheap no-ops for recording.
- With telemetry enabled and the OTLP endpoint unreachable, the SDK starts normally; exporters run asynchronously, use bounded queues/timeouts, and drop data when the queue is full. Request threads do not wait for network export.
- Telemetry is not part of `/health/ready`; SQL/Redis health behavior is unchanged. Missing Collector/Prometheus/Tempo/Grafana does not change readiness or booking correctness.
- Collector queues/retries are bounded; if Tempo/Prometheus are unavailable, Collector drops/retries within its configured window. Telemetry lost during an outage is not backfilled; new telemetry resumes after recovery.
- Host shutdown flushes providers within the configured timeouts; if the backend is unavailable, shutdown completes without guaranteeing delivery.
- Cache instrumentation is synchronous local activity/counter work before/after the existing bounded cache operation; it does not add I/O or extend the 250 ms operation budget. Booking instrumentation records tags/counters; it does not open connections or alter procedure retries/timeouts.
- Start-up configuration validation applies only when enabled: endpoint must be an absolute HTTP(S) URI; sampling ratio 0-1; positive bounded timeouts and queue/batch values; batch size <= queue. An unreachable backend is not a configuration failure.

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | No telemetry registration, request middleware, or trace-ID normalization. | Register conditional Serilog/OTel; add request middleware before exception handling; normalize Problem Details trace ID and code item. | S4-01-S4-03, S4-05 |
| Must Change | New `Infrastructure/Telemetry/` options/validator | Missing. | Add disabled-by-default options and validation for endpoint/sampler/timeouts/queue/batch. | S4-01-S4-05 |
| Must Change | New `ActivityLogEnricher`, `RequestLoggingMiddleware`, `LoggingSetup` | Missing. | Add JSON logging, trace/span enrichment, route-template request event, bounded outcome fields, and exception handling. | S4-01, S4-05 |
| Must Change | `ProblemResponse`, `ProblemCodes`, Program Problem Details callbacks, `SqlDependencyExceptionHandler` | `traceId` is traceparent/request ID; raw path logged; no outcome item. | Emit W3C trace ID; set/read Problem code item; log route template; update OpenAPI description. | S4-01, S4-02, S4-05 |
| Must Change | `BoundedCache` | No cache activities/metrics; key logged. | Add cache activities/counter and sanitized event; preserve budgets/fallback. | S4-02, S4-03, S4-05 |
| Must Change | `ServiceCatalogService` | No cache-result telemetry; IDs logged. | Add bounded cache events; remove IDs. | S4-01-S4-03 |
| Must Change | `AvailabilityService` | No custom availability span. | Add `availability.evaluate` and result tag. | S4-02 |
| Must Change | `BookingAllocator`, `SqlAppointmentBookingProcedure`, `AppointmentsController`, `AppointmentService` | No allocation/procedure spans or outcome metrics; IDs in some logs. | Add activities/counters, bounded outcome tags, sanitized logs; no decision/retry changes. | S4-01-S4-03 |
| Must Change | `appsettings.json` | No telemetry section; EF/Redis logging at Information. | Add disabled telemetry defaults and category overrides. | S4-01-S4-05 |
| Need Review | `traceId` contract | Existing tests/OpenAPI only require non-empty. | Add 32-hex trace-ID assertions and fallback test; update description. | S4-01, S4-02 |
| Need Review | Request logging vs Serilog middleware | Serilog request logging captures `RequestPath`. | Use custom middleware; do not use `UseSerilogRequestLogging`. | S4-01, S4-05 |
| Need Review | ASP.NET span attributes | `url.path`/`url.query` emitted. | Remove raw path/query/user-agent/server address/port in enrichment. | S4-02, S4-05 |
| No Change | Cache key/payload/TTL/budgets; booking procedure/locks/retries/budget; health checks | Correctness constraints. | Preserve unchanged. | P-01, P-02, S4-05 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| I4-01 | Decision | Which package versions? | Serilog.AspNetCore 10.0.0; OpenTelemetry 1.20.1 family; no prerelease. | Resolved; NuGet metadata. |
| I4-02 | Decision | How are logs correlated? | Custom request middleware + Activity enricher + W3C trace ID in Problem Details. | Resolved. |
| I4-03 | Decision | How to avoid raw path/query in logs/spans? | Route-template request event; remove ASP.NET `url.path`/`url.query`/user-agent/server attrs. | Resolved. |
| I4-04 | Decision | EF vs SQL instrumentation? | SqlClient only; no EF package; verify procedure span. | Resolved. |
| I4-05 | Decision | Redis instrumentation? | Custom cache activities/metrics; no beta package. | Resolved. |
| I4-06 | Decision | Metric names/labels/buckets? | Contract above; Views bound attributes. | Resolved; verify translation in smoke test. |
| I4-07 | Decision | Sampling/export bounds? | Parent-based ratio; 1.0 in Compose; bounded batch/metric reader options. | Resolved. |
| I4-08 | Risk | Existing tests capture `ILogger`; Serilog could replace providers. | `AddSerilog` as one provider preserves other providers; verify existing logger-capture test passes. | Resolved; implementation check. |
| I4-09 | Risk | Problem Details trace-ID change could break tests. | Keep property name/fallback; update non-empty assertions to 32-hex where an Activity exists; add fallback test. | Resolved; implementation check. |
| I4-10 | Risk | Telemetry could add latency to cache/booking. | Local activity/counter only; no I/O in request path; exporters async. | Resolved by design; no performance guarantee. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S4-01, AC4-01 | Serilog JSON test sink: fields, trace/span, app identity, no raw path/query/body/IDs; end-to-end log-to-Tempo trace ID match. | Unit + operational |
| S4-02, AC4-02 | Generated traces contain HTTP/SQL spans for EF reads and `dbo.CreateAppointment`, `availability.evaluate`, and `booking.allocate`; catalog traces contain `cache.get/set`; no `url.path`/`url.query`; no SQL parameters. | Automated smoke |
| S4-03, AC4-05 | Prometheus names/labels/values; booking success vs capacity conflict vs dependency error; procedure attempts; cache hit/miss/failure; no forbidden labels. | Automated smoke |
| S4-05, AC4-06, AC4-07 | Telemetry-disabled/unreachable behavior; readiness/booking; recovery; sensitive-data contract; bounded queue/timeout inspection. | Unit + API + operational |

All checks are **Not run**. Detailed test design is in [[research/4/03-verification-workload-and-operations|Part 3]].
