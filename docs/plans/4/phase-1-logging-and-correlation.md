---
title: "4 - Observability: Phase 1 - Logging, Correlation, and Telemetry Options"
tags: [plans, service-scheduler, observability, serilog, logging, correlation]
ticket: 4
status: implemented
updated: 2026-11-02
---

# Phase 1 - Logging, correlation, and telemetry options

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/01-instrumentation-and-correlation|Instrumentation and correlation]].
- Plan overview: [[plans/4/README|Ticket 4 implementation plan]].
- Requirements: S4-01, S4-05; AC4-01, AC4-07; TC4-01, TC4-02, TC4-03, and the unit portion of TC4-08; project P-03.
- Dependencies: research verification Passed 2026-11-02 and the current checkout baseline. No earlier implementation phase is required.

## Scope and intended behavior

This phase establishes the logging and correlation foundation without adding tracing/metrics exporters. Serilog becomes the API logging provider with compact JSON console output, stable application identity, and trace/span enrichment. A custom request-completion middleware emits exactly one sanitized event per non-health request. Problem Details keeps the `traceId` property but emits the 32-lowercase-hex W3C trace ID when an `Activity` exists and a non-empty request-identifier fallback otherwise.

The phase also adds the disabled-by-default `Scheduler:Telemetry` options and validator that later phases consume. OpenTelemetry registration, custom activities/metrics, the telemetry stack, and end-to-end delivery checks remain in later phases.

Exclusions for this phase: no OpenTelemetry provider/exporter, no custom spans or instruments, no Collector/Prometheus/Tempo/Grafana configuration, no database/cache change, and no change to booking/cache/health decisions or budgets.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1.1 | S4-01, S4-05 | `Directory.Packages.props`; `src/ServiceScheduler.Api/ServiceScheduler.Api.csproj` | Pin and reference Serilog.AspNetCore 10.0.0, Serilog.Formatting.Compact 3.0.0, Serilog.Sinks.Console 6.1.1, and the OpenTelemetry 1.20.1 family (Extensions.Hosting, Exporter.OpenTelemetryProtocol, Instrumentation.AspNetCore/Runtime/SqlClient). Keep central package management and existing pins. | `dotnet restore` and `dotnet build --no-restore` succeed with the pinned SDK; no prerelease or vulnerable-package warning is introduced; package graph inspection records the resolved versions. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 1.2 | S4-01, S4-05 | New `Infrastructure/Telemetry/TelemetryOptions.cs`, `TelemetryOptionsValidator.cs`, `ProblemTraceId.cs`; `Program.cs` options registration; `appsettings.json` | Add `Scheduler:Telemetry` with `Enabled=false`, OTLP endpoint `http://127.0.0.1:4317`, sampling ratio `1.0`, service name/version/instance defaults, and bounded export values. Validate only when enabled: absolute HTTP(S) endpoint, ratio 0-1, positive bounded timeouts/queues/batch, batch size <= queue. Add a pure trace-ID resolver using `Activity.Current.TraceId.ToHexString()` with a non-empty fallback. | Options validator tests cover disabled defaults, valid enabled values, invalid endpoint/ratio/timeouts/queue/batch, and batch > queue; trace-ID helper tests cover activity-present 32-hex and fallback; invalid enabled configuration fails startup. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 1.3 | S4-01, S4-05 | New `LoggingSetup.cs`, `ActivityLogEnricher.cs`, `RequestLoggingMiddleware.cs`; `Program.cs` | Replace default providers with Serilog JSON console output (`CompactJsonFormatter`) while keeping Serilog as one `ILoggerFactory` provider so test-added providers still receive events. Add `Application`, `ApplicationVersion`, `Environment`, `TraceId`, `SpanId`, and `TraceFlags` enrichment. Add request middleware before `UseExceptionHandler()` that logs one `RequestCompleted` event with `RequestMethod`, `RouteTemplate` (never raw path), `StatusCode`, `StatusClass`, `Outcome`, `ElapsedMilliseconds`, and level policy (5xx Error, 4xx Warning, else Information); skip `/health/*`; log an escaped exception once and rethrow. | Unit tests assert JSON-serializable log fields, identity, trace/span enrichment, one request event, route-template-only logging, status/outcome mapping, health exclusion, and no raw path/query/body/IDs. Existing `CapturingLoggerProvider` log assertions still pass. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 1.4 | S4-01, S4-02, S4-05 | `ProblemCodes.cs`, `ProblemResponse.cs`, `SqlDependencyExceptionHandler.cs`, `Program.cs` Problem Details callbacks, `ProblemDetailsSchemaTransformer.cs` | Add `ProblemCodes.HttpContextItemKey = "scheduler.problem.code"`. Set the item from `ProblemResponse.Create`, `CustomizeProblemDetails`, `InvalidModelStateResponseFactory`, and `SqlDependencyExceptionHandler`. Replace `Activity.Current?.Id` with the W3C trace ID resolver while preserving the non-empty fallback and the `traceId` property. Log the route template instead of the raw path in the dependency handler. Update the OpenAPI `traceId` description. | Unit helper tests assert 32 lowercase hex when an Activity is active and a non-empty fallback otherwise; integration tests confirm the fallback path, the code item drives request-log `Outcome`, dependency logs contain no raw path, and OpenAPI documents the W3C trace-ID semantics. The telemetry-enabled end-to-end 32-hex assertion is owned by Phase 2. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 1.5 | S4-01, S4-05 | `BoundedCache.cs`, `ServiceCatalogService.cs`, `AppointmentService.cs`, `BookingAllocator.cs`, `SqlAppointmentBookingProcedure.cs`, `appsettings.json`, `appsettings.Development.json` | Sanitize existing events: remove cache key, dealership IDs, and raw path; add `EventName`, bounded operation/result/budget/outcome fields, and no entity IDs. Align messages with the structured event contract. Apply Serilog/framework overrides (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `StackExchange.Redis` to Warning) in both normal and Development configuration. | Existing cache/booking/dependency log tests still pass; log assertions confirm no cache key, dealership ID, or raw path; category overrides do not hide application Information events; request/dependency events remain visible. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 1.6 | S4-06 | `docs/test/4-test.md`; `docs/README.md` and this plan index as needed | Create the ticket 4 executed-evidence record with scope, prerequisites, requirement coverage, planned cases, and Not run rows; record actual Phase 1 commands/results as implementation proceeds. Link the plan and test record from the documentation index. | Test report contains actual revision/environment/commands/results for executed Phase 1 checks and explicit Not run/Blocked rows for later phases; links resolve; no planned result is presented as executed. | Implemented 2026-11-02 (see [[test/4-test]]) |

## Verification

| Case | Command / scenario | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| TC4-01 Telemetry options | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~TelemetryOptions"` | Disabled defaults pass; invalid enabled endpoint/ratio/timeouts/queue/batch fail with the configuration key; no network is required. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-02 Log contract | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~ActivityLogEnricher|FullyQualifiedName~RequestLoggingMiddleware"` | JSON events include application identity and `TraceId`/`SpanId`/`TraceFlags` when an activity exists; the request event contains route template/method/status class/outcome/elapsed and no raw path/query/body/IDs. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-03 Request middleware and fallback trace ID | Unit middleware tests plus `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ProblemDetails"` | Exactly one completion event per request; documented outcomes map correctly; fallback trace ID is non-empty when no activity exists. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-08 Sensitive-data unit portion | Unit log/request tests with synthetic IDs, VIN-like values, query strings, and route/body values | None of the synthetic values appear in captured log messages/properties; route template and bounded outcome remain. | Not run (plan-time record; see [[test/4-test]]) |
| Existing regression | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build`; `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | Existing cache/booking/Problem Details/OpenAPI tests pass; logger-capture tests still observe application events through the additional provider. | Not run (plan-time record; see [[test/4-test]]) |
| Build/format | `dotnet restore`; `dotnet build --no-restore`; `dotnet format --verify-no-changes` | Build succeeds with no warnings/errors; formatting is unchanged. | Not run (plan-time record; see [[test/4-test]]) |

## Exit criteria

- Serilog JSON console logging is active with stable application identity and trace/span enrichment; telemetry options are disabled by default and validated when enabled.
- One sanitized request-completion event is emitted per non-health request, and existing log call sites contain no cache keys, dealership IDs, or raw paths.
- Problem Details `traceId` uses the W3C 32-hex value when an `Activity` exists and keeps a non-empty fallback; the code item is set by all Problem Details producers.
- Phase 1 unit/integration checks and the existing affected suites pass; `docs/test/4-test.md` records actual commands and results.
- No OpenTelemetry exporter, telemetry container, or custom span/metric is claimed from this phase.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: key/payload/TTL/budgets unchanged; only log fields are sanitized in this phase.
- Booking: behavior/retries/locking unchanged; only log fields are sanitized in this phase.
- Configuration: new `Scheduler:Telemetry` defaults and log-level overrides; no secret.
- Open decision: the exact Serilog test-sink helper remains a unit-test implementation detail; it must not introduce a production-only sink or change provider behavior.
