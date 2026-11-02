---
title: "4 - Observability: Research Overview"
tags: [research, service-scheduler, observability, telemetry, prometheus, grafana, tracing]
ticket: 4
status: verified
updated: 2026-11-02
---

# 4 - Observability: Research Overview

- Ticket: [[4 - Observability|Ticket 4]]
- Goal and scope: make catalog, availability, and booking requests diagnosable through correlated Serilog JSON logs, bounded OpenTelemetry traces and metrics, a pinned Collector/Prometheus/Tempo/Grafana stack, automated telemetry smoke checks, without making telemetry a booking or readiness dependency.
- Requirement IDs: S4-01 through S4-06; project requirement P-03; constraints A-01 through A-03, P-01, P-02, D-01 through D-04, and P-04.
- Verification cases: ticket-defined T-13 and T-15; S4-V01 through S4-V03. The ticket's eight unlabeled acceptance bullets are mapped to research-local labels AC4-01 through AC4-08 in this overview; they are not new ticket IDs.
- Evidence baseline: checkout HEAD `92a81efde5dac1070669fff13193e452dc9b6529` on `main` with a clean working tree at research start. Inspected files are listed in [[#Files referenced]]. Research-phase container probes on 2026-11-01/2026-11-02 validated proposed Collector, Prometheus, Tempo, and Grafana configuration shapes against the pinned images; probes are configuration evidence, not ticket tests.
- Environment observed: Windows 11; .NET SDK `10.0.403`; Docker server `25.0.3`; Docker Compose `v2.24.6-desktop.1`; existing SQL Server 2022 CU23 and Redis `7.4.12-alpine` pins unchanged.
- Related plan: not created; this research precedes planning.

> [!IMPORTANT]
> This document records requirements, inspected evidence, proposed contracts, decisions, and planned verification. Serilog, OpenTelemetry, Collector, Prometheus, Tempo, Grafana, telemetry tests do not exist in the checkout. Every ticket test is **Not run**; research-phase container probes validate configuration syntax and startup only and do not establish T-13, T-15, S4-V01-S4-V03, or any acceptance criterion.

## Decomposition decision

Use multipart research under `docs/research/4/` because ticket 4 combines application logging/tracing/metrics, a multi-container telemetry backend with provisioning, sensitive-data controls, outage behavior, end-to-end delivery checks,. The concerns need separate evidence and have cross-part dependencies.

| Part | File | Responsibility |
| --- | --- | --- |
| Overview | [[research/4/00-overview\|00-overview]] | Scope lock, part index, current architecture, consolidated coverage/audit, gap decisions, dependency-ordered change plan, recovery, handoffs, traceability, decisions, and out-of-scope items. |
| Part 1 | [[research/4/01-instrumentation-and-correlation\|01-instrumentation-and-correlation]] | Package pins, Serilog logging and request events, OpenTelemetry SDK/instrumentation, span boundaries, metric names/units/buckets, W3C correlation, Problem Details trace IDs, sensitive-data controls, and bounded outage behavior. |
| Part 2 | [[research/4/02-telemetry-stack-and-dashboards\|02-telemetry-stack-and-dashboards]] | Pinned Collector/Prometheus/Tempo/Grafana images and configuration, Compose integration, OTLP paths, retention/resource bounds, provisioned data sources and dashboards, PromQL, zero-traffic behavior, access, and troubleshooting. |
| Part 3 | [[research/4/03-verification-workload-and-operations\|03-verification-workload-and-operations]] | Automated telemetry contract and end-to-end smoke checks, outage/recovery checks, Redis degradation signals, evidence rules, and documentation/deploy-note plan. |

The overview owns shared scope, consolidated decisions, and Phase 7 traceability. Later phases must use this overview as the primary research entry point.

## Phase 0: Scope lock

### 0.1 Ticket requirements

| ID | Requirement text (condensed) | Acceptance evidence | Project requirement / constraint |
| --- | --- | --- | --- |
| S4-01 | Structured request/domain/dependency logs with usable trace correlation and stable application identity. | JSON logs contain stable application identity and matching trace/span fields; a request can be followed from its log event to its trace. | P-03; D-01, D-03 |
| S4-02 | HTTP, SQL, availability/allocation, and cache tracing through OTLP and Tempo. | Tempo contains server, SQL, custom availability/allocation, and cache spans for generated requests, including booking-conflict traces. | P-03; P-01, P-02 constraints |
| S4-03 | Bounded request, booking, and cache metrics exported through the Collector to Prometheus. | Prometheus contains request rate/latency/error, booking-outcome, procedure-attempt, and cache hit/miss/failure series with bounded labels. | P-03; P-02 constraint |
| S4-04 | Pinned local telemetry stack, provisioned Grafana data sources/dashboards, and operational instructions. | Fresh Compose startup provisions Prometheus/Tempo data sources and a working dashboard; access, retention, and troubleshooting are documented. | P-03; D-01, D-03, P-04 |
| S4-05 | Sensitive-data protection, bounded telemetry processing, and booking/readiness resilience during telemetry outages. | Logs/spans/metric attributes exclude forbidden data; API starts and books with telemetry backends unavailable; readiness stays correct; export resumes after recovery. | P-03; P-01, P-02, A-03 constraints |
| S4-06 | Automated telemetry smoke checks and recorded inspection. | Tests query real Prometheus/Tempo/Grafana APIs with bounded polling. | P-03; D-04, P-04 |

### 0.2 Acceptance criteria

The ticket lists its acceptance criteria as eight unlabeled bullets. `AC4-01` through `AC4-08` are research-local traceability labels applied in ticket order; they are not new ticket requirements and do not change the criteria text.

| Research label | Acceptance criterion | Research owner |
| --- | --- | --- |
| AC4-01 | A generated booking request can be correlated between structured logs and its Tempo trace through Grafana. | Parts 1 and 3; T-15. |
| AC4-02 | Availability and booking-conflict traces include the relevant SQL/custom spans; cache activity is visible. | Parts 1 and 3; S4-02. |
| AC4-03 | Requests reach Prometheus and Tempo through the Collector, verified by automated smoke checks and recorded manual inspection. | Parts 1-3; T-15, S4-V03. |
| AC4-04 | Provisioned Grafana dashboards work without manual data-source or dashboard creation and handle zero traffic predictably. | Part 2 and Part 3; S4-V03. |
| AC4-05 | Metrics distinguish successful bookings, capacity conflicts, dependency errors, and cache hits/misses/failures using bounded labels. | Part 1 and Part 3; S4-V02. |
| AC4-06 | Redis outage shows degradation signals while catalog fallback and booking stay correct; telemetry backend outages do not prevent booking or fail readiness solely due to telemetry. | Parts 1-3; T-13, S4-V01. |
| AC4-07 | Tests/checks confirm sensitive data and unbounded identifiers are excluded from metric labels and logged payloads. | Parts 1 and 3; S4-V02. |
| AC4-08 | Documented startup instructions, architecture and README reflect actual observations. | Parts 2 and 3. |

### 0.3 Verification cases

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-13 Redis degradation signals | Automated smoke + inspection | Controlled Redis outage produces correlated cache-failure logs/metrics while catalog fallback and booking remain correct. | P-03; S4-05 |
| T-15 Observability wiring | Automated smoke + inspection | Requests reach provisioned metric and trace backends, and logs contain matching trace identifiers. | P-03; S4-01, S4-02, S4-03 |
| S4-V01 Telemetry outage and recovery | Automated operational | API startup, booking, and readiness remain correct with telemetry backends unavailable; bounded export resumes after recovery. | S4-05 |
| S4-V02 Telemetry data contract | Automated | Captured logs, spans, and exported metric attributes exclude sensitive payloads, raw request paths/query strings, and unbounded identifiers; outcome counters and correlation fields match generated scenarios. | S4-01, S4-02, S4-03, S4-05 |
| S4-V03 Provisioning and dashboard behavior | Automated smoke + inspection | Fresh local startup provisions data sources/dashboards; panels show expected workload signals and documented zero-traffic behavior. | S4-03, S4-04 |

### 0.4 Exclusions and necessary dependencies

| Item / ticket source | Classification | Reason and requirement link |
| --- | --- | --- |
| Serilog JSON console logging with request/domain/dependency events | Required | S4-01, AC4-01, AC4-07. |
| OpenTelemetry SDK, OTLP exporter, ASP.NET Core/runtime/SqlClient instrumentation | Required | S4-02, S4-03; SQL spans must cover both EF queries and the direct stored-procedure `SqlCommand`. |
| Custom availability/allocation and cache activities | Required | S4-02, AC4-02; the cache package has no suitable stable instrumentation. |
| Collector, Prometheus, Tempo, Grafana containers, configuration, and provisioned dashboard/data sources | Required | S4-03, S4-04, AC4-03, AC4-04. |
| Bounded metrics with defined names, units, buckets, labels, and PromQL | Required | S4-03, AC4-05; dashboards and smoke checks depend on the contract. |
| Sensitive-data exclusion and bounded export behavior | Required | S4-05, AC4-06, AC4-07; booking/readiness must not depend on telemetry. |
| Automated telemetry smoke/contract/outage checks | Required | S4-06, S4-V01-S4-V03. |
| Existing API host, Problem Details pipeline, health checks, SQL Server, Redis, Compose, and test fixtures | Necessary dependency | Instrumentation extends them; existing booking/cache behavior must be preserved. |
| Existing `ILogger` dependency/cache events | Necessary dependency | They become Serilog events; they must be sanitized and correlated rather than replaced wholesale. |
| Loki or centralized log storage | Excluded | Ticket says console/container output remains the core log access path; Loki is optional and outside this ticket. |
| Alerting, production hosting, centralized operations | Excluded | Explicit ticket exclusion; local demo only. |
| Database schema, migrations, booking procedure, runtime permissions, cache key/payload/TTL/invalidation changes | Excluded | Ticket states no such change is intended; research must record no impact. |
| Authentication/authorization of API callers | Excluded | Local unauthenticated demo; telemetry UI access is local-only operational access, not application authentication. |
| Custom application UI | Excluded | Grafana is an operational visualization layer, not an application UI; generated Swagger UI remains the API client. |
| Performance targets or high-availability claims | Excluded | No performance target or high-availability guarantee is claimed. |
| Exemplars | Optional | Ticket says exemplars are optional subject to research and backend support; not required for acceptance. |

### 0.5 Attachments

Ticket 4 references no images, PDFs, or other supporting assets. No asset inspection applies.

## Current architecture and entry points

> [!NOTE]
> Tickets 1-3 are implemented and recorded in [[test/1-test|the ticket 1 test report]], [[test/2-test|the ticket 2 test report]], and [[test/3-test|the ticket 3 test report]]. Ticket 4 telemetry code and configuration are absent at this baseline. Preparing ticket 4 does not rerun or establish those prior results.

| Component | Inspected evidence | Status |
| --- | --- | --- |
| Composition root | `src/ServiceScheduler.Api/Program.cs`: controller/Problem Details registration and `code`/`traceId` customization, OpenAPI/Swagger, options validation, `DbContext`/`UseSqlServer`, Redis cache, feature services, booking procedure, health checks, and endpoint mapping. There is no Serilog, OpenTelemetry, telemetry exporter, request-logging middleware, or telemetry options. | Existing; Must Change. |
| Error contract | `Infrastructure/Errors/ProblemCodes.cs`, `ProblemResponse.cs`, `SqlDependencyExceptionHandler.cs`; `traceId` currently uses `Activity.Current?.Id` (traceparent) or `HttpContext.TraceIdentifier`. | Existing; Must Change for trace-ID semantics and sanitized logging. |
| Feature services | `Features/Dealerships`, `ServiceCatalog`, `Availability`, `Appointments`; controllers are thin, services own queries/rules, booking allocation has a bounded procedure loop. | Existing; Must Change for custom spans/metrics and log sanitization. |
| Catalog cache | `Infrastructure/Caching/BoundedCache.cs`, `CacheOptions.cs`, `CacheHealthCheck.cs`; 250 ms per-operation budget, Redis failure fallback, key logged on degradation. | Existing; Must Change for cache activities/metrics and key removal from logs; budgets/key/payload/TTL unchanged. |
| SQL boundary | `Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs` uses a direct `SqlCommand`; EF Core queries cover reads and booking prechecks. Both flow through `Microsoft.Data.SqlClient` 6.1.7. | Existing; instrumentation only; procedure/locking unchanged. |
| Health | `/health/live` has no checks; `/health/ready` runs `database` and `redis` checks; JSON writer exposes names/statuses/durations only. | Existing; No Change; telemetry must not become a readiness check. |
| Configuration/packaging | `appsettings.json`, `.env.example`, `compose.yaml`, Dockerfile; SQL Server 2022 CU23, Redis `7.4.12-alpine`, one-shot setup, API. No telemetry services, volumes, or configuration. | Existing; Must Change. |
| Packages | `Directory.Packages.props` has EF Core/Microsoft.Extensions 10.0.13, `Microsoft.Data.SqlClient` 6.1.7 transitive, `Testcontainers.MsSql`/`Testcontainers.Redis` 4.14.0, xUnit v3. No Serilog or OpenTelemetry package. | Existing; Must Change. |
| Tests/fixtures | Unit and integration projects; `SchedulerApiFactory`, real SQL Server/Redis Testcontainers, fixed `FakeTimeProvider`, `CapturingLoggerProvider`, database-per-class and Redis DB-per-class isolation. | Existing; Need Review/extend; telemetry tests absent. |
| Documentation | README, architecture, setup guide, database/cache docs, test/deploy records; telemetry marked planned for ticket 4. | Existing; Must Change during implementation. |

### Current request and telemetry flow

```text
HTTP request
  -> ASP.NET Core request pipeline (no request-completion event beyond framework logging)
  -> controller -> feature service
       catalog: BoundedCache -> Redis hit/miss/failure -> SQL fallback
       availability: EF Core SQL reads
       booking: EF Core prechecks -> bounded allocator -> direct SqlCommand -> dbo.CreateAppointment
  -> Problem Details response with code + traceId
  -> response
```

Proposed ticket 4 flow:

```text
HTTP request
  -> OpenTelemetry ASP.NET Core server span (W3C traceparent propagation)
  -> Serilog request-completion middleware (route template, method, status class, outcome, elapsed, TraceId, SpanId)
  -> controller/service custom activity (availability.evaluate, booking.allocate, cache.get/set/remove)
       SQL spans from SqlClient instrumentation for EF queries and the direct procedure command
  -> Serilog JSON console log
  -> OTLP gRPC traces --\
  -> OTLP gRPC metrics ----> Collector -> Prometheus (metrics) and Tempo (traces) -> Grafana
```

### Current log and trace inventory

| Location | Current event / behavior | Ticket 4 disposition |
| --- | --- | --- |
| `Infrastructure/Caching/BoundedCache.cs:123` | Warning includes Redis operation, cache key (dealership GUID), and budget. | Keep degradation event; remove key/ID; add cache operation/result and budget; record cache metric/activity. |
| `Features/ServiceCatalog/ServiceCatalogService.cs:55,61` | Error includes dealership ID; corrupt JSON removes key. | Keep corrupt-payload event; remove ID; add bounded cache event/metric; no payload. |
| `Features/Appointments/AppointmentService.cs:89,97` | Error includes dealership ID for unknown/invalid IANA zone. | Keep configuration-error event; remove ID; trace/route context identifies request. |
| `Features/Appointments/BookingAllocator.cs:62,79,102,130,137,176` | Information/warning includes attempt counts and outcome context; no IDs. | Keep and add bounded `EventName`/outcome/count fields; record final booking outcome and allocation metrics/span. |
| `Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs:80` | Warning includes SQL error number and mapped status; no IDs. | Keep; add `EventName`, procedure result; record attempt metric. |
| `Infrastructure/Errors/SqlDependencyExceptionHandler.cs:31` | Warning includes HTTP method, raw path, and trace ID. | Replace raw path with route template; keep trace ID via enricher; record request status metric via middleware. |
| `Infrastructure/Setup/DatabaseSetupRunner.cs:20-40` | Setup-mode simple console logs; separate `LoggerFactory`. | No change required; setup mode is not part of the request telemetry pipeline. |
| Problem Details customization (`Program.cs`, `ProblemResponse`, handler) | `traceId` is `Activity.Current?.Id` (traceparent) or request identifier. | Change to W3C trace ID (`Activity.Current.TraceId`, 32 lowercase hex) with request-identifier fallback; document OpenAPI. |

## Phase 1: Consolidated layer coverage

| Component | Coverage and evidence | Research part |
| --- | --- | --- |
| API contract and validation | Existing controllers/DTOs/Problem Details; ticket 4 changes only `traceId` semantics and adds no route or request field. | Parts 1 and 3 |
| Scheduling rules | Existing availability/booking rules unchanged; custom spans record outcomes without changing decisions. | Part 1 |
| Resource selection and availability | Existing SQL reads and procedure unchanged; SQL/custom spans observe them. | Parts 1 and 3 |
| Domain model and EF mappings | No change; EF reads emit SQL spans through SqlClient. | Part 1 |
| EF Core migrations and seeds | No change; no migration or seed is planned. | Overview; Part 2 |
| Booking procedure and transactions | No change; telemetry must not add network waits or locks inside the procedure path. | Parts 1 and 3 |
| Runtime permissions | No change; telemetry has no SQL write/read privilege requirement beyond existing runtime reads. | Overview; Part 2 |
| Failure and retry handling | Existing classification unchanged; telemetry exporter failures are isolated. | Parts 1 and 3 |
| Redis catalog cache | No key/payload/TTL/invalidation change; instrumentation adds activities/metrics and removes key from logs; budgets unchanged. | Parts 1 and 3 |
| Configuration and packaging | New `Scheduler:Telemetry` options, Compose telemetry services/volumes, config files, package/image pins. | Parts 1-3 |
| Telemetry | Missing implementation; proposed Serilog/OpenTelemetry/Collector/Prometheus/Tempo/Grafana. | Parts 1-3 |
| Unit tests | Missing telemetry unit tests; existing tests remain; add options/enricher/middleware/listener tests. | Part 3 |
| API and dependency integration tests | Missing telemetry end-to-end/outage tests; existing SQL/Redis fixtures reusable. | Part 3 |
| Operational verification | Missing; add Compose smoke, log capture, dashboard inspection, recovery. | Part 3 |
| Documentation and delivery | Must Change: README, architecture, setup, docs index, test report, deploy notes; cache/database no-impact notes. | Parts 2 and 3 |
| UI or new integrations | N/A — backend-only; Grafana is an operational dashboard, not an application UI. S4-V03 covers provisioning behavior. | Overview |

### 1.2 Workflow change checklist

| Check | Evidence to collect | Status |
| --- | --- | --- |
| All callers and entry points | Controller routes, feature services, cache wrapper, SQL procedure boundary, exception handler, health endpoints, composition root. | Inspected; instrument at these boundaries. |
| Rules enforced across boundaries | Booking/cache/availability rules remain in existing code; telemetry records outcomes and must not alter decisions or budgets. | Proposed; Parts 1 and 3. |
| Read and write consistency | Logs/spans/metrics derive from actual results; no independent state; no request body/ID capture. | Proposed; Part 1. |
| Failure paths | Redis fallback, SQL dependency failure, booking conflicts, cancellation, telemetry exporter outage, Collector/Prometheus/Tempo outage. | Proposed; Parts 1 and 3. |
| Verification coverage | Unit listener tests, end-to-end backend smoke, contract checks, outage/recovery, dashboard. | Proposed; Part 3. |

## Phase 2: Consolidated data-flow trace

### 2.1 READ paths

| Path | Flow to instrument | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Dealership discovery | HTTP span -> EF SQL span -> response. | No cache; route template/status in request event. | Existing code; instrumentation missing. |
| Service catalog | HTTP span -> `cache.get` activity/metric -> Redis hit/miss/failure -> SQL fallback/read (`cache.set` on miss) -> response. | Cache budgets unchanged; no key in telemetry; cache failure degrades to SQL. | Existing code; instrumentation missing. |
| Availability | HTTP span -> `availability.evaluate` activity -> EF SQL spans -> `availability.result`. | Advisory read; no reservation; no cache; outcome vocabulary bounded. | Existing code; instrumentation missing. |
| Appointment retrieval | HTTP span -> EF SQL span -> response. | Dealership scope unchanged; no cache. | Existing code; instrumentation missing. |
| Health | HTTP span if enabled -> SQL/Redis health checks. | Liveness dependency-free; readiness must not add telemetry checks. | Existing code; keep semantics. |

### 2.2 WRITE path

| Path | Flow to instrument | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Booking confirmation | HTTP span -> `booking.allocate` activity -> SQL spans for prechecks -> per-attempt `dbo.CreateAppointment` SQL span -> final `booking.outcome` metric. | Procedure transaction/locking/retry budget unchanged; telemetry is synchronous local instrumentation only; exporters are asynchronous. | Existing code; instrumentation missing. |
| Telemetry export | Batch trace processor and periodic metric reader -> OTLP gRPC -> Collector -> Tempo/Prometheus. | Bounded queue/timeouts; no request-thread network wait; Collector queues/retries. | Missing; proposed. |
| Redis outage | `cache.get` failure -> metrics/activity/log -> SQL fallback; request succeeds. | Bounded 250 ms budget; no telemetry wait; readiness Degraded/200. | Existing behavior; telemetry signals missing. |
| Telemetry outage | Exporter failure/drop -> request unaffected -> readiness unaffected -> new export after recovery. | No health check; no blocking; no backfill of dropped telemetry. | Missing; proposed. |

### 2.3 Path coverage verification

| Affected path / requirement | Value source and rule enforcement | Failure / cleanup behavior | Verification scenario | Observed evidence |
| --- | --- | --- | --- | --- |
| Catalog/cache S4-02/S4-03 | Cache result from `BoundedCache`; SQL fallback from existing service. | Cache failure logs/metrics; SQL response still returned. | T-13, AC4-02, AC4-05, AC4-07. | Not run. |
| Availability S4-02 | Existing advisory evaluation result. | No writes; outcome tag. | AC4-02. | Not run. |
| Booking S4-02/S4-03 | Existing final status and procedure results. | Counters for final outcome and internal attempts; no retry changes. | AC4-02, AC4-05. | Not run. |
| Correlation S4-01 | W3C trace ID from ASP.NET Core/OTel; log enricher; Problem Details. | Fallback request identifier when no Activity. | T-15, AC4-01, S4-V02. | Not run. |
| Telemetry outage S4-05 | Asynchronous exporters; no readiness check. | Requests/readiness unaffected; export resumes. | S4-V01, AC4-06. | Not run. |
| Dashboard S4-04 | Prometheus series/Queries and Grafana provisioning. | Zero traffic shows documented zero/no data. | S4-V03, AC4-04. | Not run. |

## Phase 3: UX state matrix

N/A — ticket 4 is backend-only and does not introduce or change an application UI. Grafana is an operational dashboard whose provisioning and zero-traffic behavior are covered by S4-V03 and Part 2; it is not an end-user state machine. Swagger UI remains a supplementary API client.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict / evidence |
| --- | --- | --- | --- | --- |
| `src/ServiceScheduler.Api/Program.cs` | Composition root | Controllers, Problem Details customization, OpenAPI, options validation, DbContext, Redis cache, feature services, booking procedure, health, endpoint mapping; no Serilog/OTel/telemetry options/request-logging middleware. | S4-01-S4-05 | Must Change. |
| `Infrastructure/Errors/ProblemResponse.cs`, `ProblemCodes.cs`, `SqlDependencyExceptionHandler.cs` | Error/trace contract | `traceId` is `Activity.Current?.Id` or request identifier; handler logs method/path; codes are bounded. | S4-01, S4-02, S4-05 | Must Change. |
| `Infrastructure/Caching/BoundedCache.cs`, `CacheOptions.cs`, `CacheHealthCheck.cs` | Cache telemetry candidate | Per-operation 250 ms budget; Redis exceptions/timeouts fall back; warning logs include cache key; health uses a 1 s internal budget. | S4-01-S4-03, S4-05 | Must Change. |
| `Features/ServiceCatalog/ServiceCatalogService.cs` | Cache-aside path | Cache hit/miss/corrupt/fallback reads; errors include dealership ID; cache populate on miss. | S4-01-S4-03 | Must Change. |
| `Features/Availability/AvailabilityService.cs` | Availability path | Constructs `availability` result after SQL reads; no writes/cache. | S4-02 | Must Change. |
| `Features/Appointments/AppointmentService.cs`, `BookingAllocator.cs`, `AppointmentsController.cs` | Booking path | Prechecks, bounded candidate loop, typed allocation status, final HTTP mapping; logs include attempt context and some dealership IDs. | S4-01-S4-03 | Must Change. |
| `Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs` | Direct SQL boundary | Dedicated `SqlConnection`/`SqlCommand`; maps procedure errors; logs SQL number/status; no parameters logged. | S4-02, S4-05 | Must Change for attempt metric/event; SQL instrumentation observes command. |
| `Infrastructure/Health/HealthResponseWriter.cs`, health registrations | Readiness | JSON names/statuses/durations only; SQL 5 s, Redis 2 s timeouts; no telemetry check. | S4-05 | No Change. |
| `appsettings.json`, `appsettings.Development.json` | Configuration | Logging levels and cache/booking defaults; no telemetry section. | S4-01-S4-05 | Must Change. |
| `Directory.Packages.props`, `ServiceScheduler.Api.csproj` | Package pins | EF/Microsoft.Extensions 10.0.13, `Testcontainers.MsSql`/`Testcontainers.Redis` 4.14.0, xUnit v3; no Serilog/OTel. | S4-01-S4-06 | Must Change. |
| `compose.yaml`, `.env.example`, `Dockerfile` | Local stack | SQL Server, Redis, setup, API; no telemetry services/volumes/ports. | S4-04 | Must Change (Compose only; Dockerfile may need no change). |
| `tests/.../Fixtures/SchedulerApiFactory.cs`, `SqlServerTestBase.cs`, `CacheSqlServerTestBase.cs`, `RedisContainerFixture.cs`, `CapturingLoggerProvider.cs` | Test infrastructure | In-memory config, fixed clock, real SQL/Redis, logger capture, Redis pause/unpause. | S4-01-S4-06 | Need Review/extend. |
| `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/README.md`, `docs/database/cache.md` | Delivery records | Telemetry marked planned; no actual configuration/access evidence. | S4-04, S4-06 | Must Change during implementation. |

### Gate 2: Call path and boundary verification

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| `GET /api/dealerships` | `DealershipsController.GetAsync` -> `SchedulerDbContext.Dealerships` -> SQL. | Runtime identity; no transaction; no cache. | Yes | Add HTTP/SQL spans and request metric/log from middleware. |
| `GET /api/dealerships/{id}/service-types` | `ServiceCatalogController.GetAsync` -> `ServiceCatalogService.GetAsync` -> `BoundedCache.TryGetAsync` -> SQL fallback/`SetAsync`. | Runtime identity; 250 ms per cache operation; Redis optional. | Yes | Add cache activity/metric and cache-result log; remove key. |
| `GET /api/dealerships/{id}/availability` | `AvailabilityController.GetAsync` -> `AvailabilityService.EvaluateAsync` -> EF queries. | Runtime identity; no writes/cache. | Yes | Add `availability.evaluate` span and result tag. |
| `POST /api/dealerships/{id}/appointments` | `AppointmentsController.CreateAsync` -> `AppointmentService.CreateAsync` -> `BookingAllocator.AllocateAsync` -> `SqlAppointmentBookingProcedure.ExecuteAsync` -> `dbo.CreateAppointment`. | Runtime identity; procedure owns transaction; allocator bounds retries. | Yes | Add `booking.allocate` span, final outcome metric, procedure-attempt metric; SQL spans from SqlClient. |
| `GET /api/dealerships/{id}/appointments/{appointmentId}` | Controller -> `AppointmentService.GetAsync` -> EF query. | Runtime identity; no cache. | Yes | HTTP/SQL spans and request metric/log. |
| Global exception pipeline | `UseExceptionHandler` -> `SqlDependencyExceptionHandler` -> Problem Details. | Classified 503 only; other failures controlled 500. | Yes | Sanitize route/path logging; set Problem code item for request outcome. |
| Health endpoints | `MapHealthChecks` -> SQL/Redis checks -> JSON writer. | No telemetry check; readiness 503 only for SQL. | Yes | Preserve; telemetry must not gate readiness. |
| `--db-setup` | `DatabaseSetupCommand.RunAsync` -> `DatabaseSetupRunner` -> migrations/provisioning/seeding. | Privileged setup; simple console logging. | Yes | No ticket 4 change required. |

## Phase 4: Consolidated code audit

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence / remaining verification |
| --- | --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | No Serilog, OpenTelemetry, telemetry options, request middleware, or trace-ID normalization. | Register Serilog/OTel conditionally; add request middleware before exception handling; normalize Problem Details trace ID and outcome item; keep existing routes/health. | S4-01-S4-05 | Inspected composition root. |
| Must Change | `appsettings.json` | No `Scheduler:Telemetry`; EF/Redis log categories at default Information. | Add disabled-by-default telemetry options and log-level overrides; keep cache/booking defaults unchanged. | S4-01-S4-05 | Inspected configuration. |
| Must Change | `Directory.Packages.props`, API/test project files | No Serilog/OTel/Testcontainers base package for telemetry tests. | Pin Serilog 10.0.0, OpenTelemetry 1.20.1 family, formatting/sink packages, and test packages; add projects as planned. | S4-01-S4-06 | Package metadata verified; build/restore at implementation. |
| Must Change | `Infrastructure/Errors/ProblemResponse.cs`, `ProblemCodes.cs`, `SqlDependencyExceptionHandler.cs`, `Program.cs` Problem Details callbacks | `traceId` is traceparent/request ID; handler logs raw path; no problem-code item. | Emit W3C trace ID, set a bounded Problem-code item, log route template instead of path, update OpenAPI description. | S4-01, S4-05 | Inspected. |
| Must Change | `BoundedCache.cs`, `ServiceCatalogService.cs` | Cache degradation/corrupt logs include cache key/dealership ID; no cache activities/metrics; hit/miss not distinguishable. | Remove IDs/keys; add `EventName`, operation/result/budget; record `scheduler.cache.operations` and `cache.get/set/remove` activities; preserve budgets/fallback. | S4-01-S4-03, S4-05 | Inspected. |
| Must Change | `AvailabilityService.cs`, `AppointmentService.cs`, `BookingAllocator.cs`, `SqlAppointmentBookingProcedure.cs`, `AppointmentsController.cs` | No custom spans/metrics; some logs include dealership IDs; booking outcomes not counted. | Add bounded activities/attributes and counters; remove IDs from logs; record final outcome vs internal attempts; no change to decisions/retries. | S4-01-S4-03, S4-05 | Inspected. |
| Must Change | `compose.yaml` | No Collector/Prometheus/Tempo/Grafana services, volumes, ports, or telemetry env. | Add pinned services, config mounts, volumes, loopback ports, telemetry env; API remains independent of telemetry health. | S4-03, S4-04 | Inspected. |
| Must Change | `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/README.md`, `docs/database/cache.md` | Telemetry remains planned; no access/retention/outage records. | Record implemented behavior and actual evidence after implementation; add deploy notes and test report. | S4-04, S4-06; P-03 | Inspected. |
| Need Review | Existing `ILogger` event messages | Several messages carry dealership IDs or raw path; categories may be noisy after Serilog replaces default logging. | Sanitize messages and set category overrides (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`, `StackExchange.Redis` to Warning); avoid duplicate failure events. | S4-01, S4-05 | Part 1. |
| Need Review | `traceId` API contract | Current traceparent/request identifier is used in tests/OpenAPI; changing to 32-hex trace ID is a contract refinement. | Change only the value semantics, keep the `traceId` property; update tests/OpenAPI/architecture; preserve non-empty fallback. | S4-01, S4-02 | Part 1. |
| Need Review | Request logging implementation | Serilog's `UseSerilogRequestLogging` captures `RequestPath` by default, conflicting with raw-path exclusion. | Use a custom request-completion middleware with route template and bounded fields; do not use Serilog request middleware. | S4-01, S4-05 | Part 1. |
| Need Review | ASP.NET Core server-span attributes | Current instrumentation emits `url.path` and `url.query` (query redacted by default); raw path includes IDs. | Remove `url.path`, `url.query`, `user_agent.original`, and unnecessary server attributes in the response enrichment callback. | S4-02, S4-05 | Part 1. |
| Need Review | EF Core vs SqlClient instrumentation | No EF instrumentation package; SqlClient captures EF queries and the direct procedure command. | Do not add EF Core instrumentation; avoid duplicate spans. Verify stored-procedure span and no parameter capture in tests. | S4-02, S4-05 | Part 1. |
| Need Review | Redis instrumentation | StackExchange.Redis instrumentation package is beta (`1.20.1-beta.1`). | Use explicit cache activities/metrics in `BoundedCache`; do not add the beta package. | S4-02, S4-03 | Part 1. |
| Need Review | Health/filter policy | Health requests could add spans/metrics noise; telemetry must not affect readiness. | Keep health semantics unchanged; do not add telemetry to readiness; optionally keep health spans (no filter) or filter them in the plan. | S4-05 | Parts 1-3. |
| Need Review | Telemetry test isolation | Existing SQL/Redis fixtures are already heavy; four telemetry containers add startup cost. | Prefer a separate telemetry test project/collection with its own Docker network and pinned images; keep existing suites independent. | S4-06 | Part 3. |
| No Change | Database schema/migrations/seeds/procedure/runtime permissions | No ticket 4 data change. | No migration, grant, procedure, or seed change. | Constraint | Ticket statement; inspected docs. |
| No Change | Cache key/payload/TTL/invalidation and budgets | Telemetry only observes the cache. | Preserve `scheduler:v1:...`, 10-minute TTL, 250/500/750 ms budgets, SQL fallback, health behavior. | P-02; S4-05 | Inspected. |
| No Change | Booking transaction/locking/retry/budget | Telemetry must not alter correctness. | Keep procedure, lock order, 5 candidate attempts, 2 deadlock retries, 20 s budget, no write replay. | P-01; S4-05 | Inspected. |
| No Change | Health endpoints/check statuses | Telemetry is not a readiness dependency. | No telemetry check; SQL/Redis behavior unchanged. | S4-05 | Inspected. |
| No Change | `--db-setup` logging | Setup runs before web host and uses simple console logging. | Leave setup logging out of the request telemetry pipeline; document. | Scope | Inspected. |
| Proposed Addition | `Infrastructure/Telemetry/` | Missing. | Add options/validator, resource/service metadata, Serilog configuration/enricher, custom telemetry instruments, request middleware, OTel registration, and span sanitization. | S4-01-S4-05 | Not present. |
| Proposed Addition | `telemetry/` configuration and dashboards | Missing. | Add Collector, Prometheus, Tempo, Grafana provisioning, and dashboard JSON. | S4-03, S4-04 | Not present. |
| Proposed Addition | Telemetry tests | Missing. | Add unit contract tests and end-to-end smoke/outage tests. | S4-05, S4-06 | Not present. |
| Proposed Addition | `docs/test/4-test.md`, `docs/test/4-manual-test.md`, `docs/deploy-notes/4-deploy-notes.md` | Missing. | Record executed automated/operational evidence during implementation; generate manual checklist later; document startup/access/retention/recovery. | S4-06 | Not present. |

## Phase 5: Consolidated gap register

Status vocabulary: **Resolved** means the research decision is accepted for planning; **Probed** means configuration/behavior was checked with isolated containers and primary documentation; **Deferred** means verification belongs to implementation. No blocking design gap remains open; metric-name translation and end-to-end behavior are implementation verification items.

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact | Status / evidence |
| --- | --- | --- | --- | --- | --- |
| O-01 | Decision | Which Serilog/OpenTelemetry package versions are compatible with .NET 10 and Microsoft.Data.SqlClient 6.1.7? | Serilog.AspNetCore 10.0.0, Serilog.Formatting.Compact 3.0.0, Serilog.Sinks.Console 6.1.1; OpenTelemetry 1.20.1, Extensions.Hosting 1.20.1, Exporter.OpenTelemetryProtocol 1.20.1, Instrumentation.AspNetCore/Runtime/SqlClient 1.20.1. All declare `net10.0`; SqlClient instrumentation supports Microsoft.Data.SqlClient 4.0+ (6.x included by range). Avoid prerelease 1.21.0-rc.1 and Serilog 10.0.1-dev. | S4-01-S4-03 | Resolved; NuGet metadata. |
| O-02 | Decision | Which telemetry container versions/digests should be pinned? | Collector `0.164.0`, Prometheus `v3.15.1`, Tempo `3.1.1`, Grafana `13.3.0` with the manifest-list digests in Part 2; `platform: linux/amd64`. | S4-04 | Resolved; Docker Hub/release metadata and pulls. |
| O-03 | Decision | OTLP protocol and endpoints? | App -> Collector over OTLP/gRPC `http://collector:4317` (host `http://127.0.0.1:4317`); Collector traces -> Tempo gRPC `tempo:4317`; Collector metrics -> Prometheus OTLP/HTTP base `http://prometheus:9090/api/v1/otlp` (appends `/v1/metrics`). | S4-02, S4-03 | Probed config shapes; endpoint path verified. |
| O-04 | Decision | Collector component names and pipeline bounds? | Use `otlp` receiver, `otlp_grpc` and `otlp_http` exporters (the old `otlp`/`otlphttp` names are deprecated aliases in 0.164.0), `memory_limiter` 128/32 MiB, `batch` 2 s/512, bounded sending queues, 5 s export timeout, 30 s retry window; `health_check` on 13133. | S4-03, S4-04, S4-05 | Probed: Collector 0.164.0 `validate` exit 0. |
| O-05 | Decision | Prometheus OTLP receiver path, flags, and label behavior? | Start with `--web.enable-otlp-receiver`; endpoint `/api/v1/otlp/v1/metrics`; configure `otlp.promote_resource_attributes` for `deployment.environment.name` and `service.version`, default `UnderscoreEscapingWithSuffixes`; `service.name` -> `job`, `service.instance.id` -> `instance`, other resource attributes on `target_info`. | S4-03, S4-04 | Probed: config accepted, receiver endpoint responded, Prometheus ready. |
| O-06 | Decision | Tempo 3.1 configuration/retention and API? | Tempo 3 removed `ingester`/`compactor`; use `target: all` default, OTLP gRPC/HTTP receiver, local storage (`/var/tempo/blocks`, WAL `/var/tempo/wal`), and `backend_scheduler.provider.compaction.compaction.block_retention: 24h`; query `GET /api/v2/traces/{traceId}` (JSON default) or v1 fallback; multitenancy off. | S4-02, S4-04 | Probed: Tempo 3.1.1 started with the proposed config; API docs verified. |
| O-07 | Decision | Grafana provisioning and access? | Provision Prometheus/Tempo data sources with stable UIDs `prometheus`/`tempo` and dashboard provider folder/path; dashboard UID `scheduler-overview`; anonymous Viewer access on loopback only, `allowUiUpdates: false`, no manual creation. | S4-04 | Probed: Grafana 13.3.0 provisioned data sources and dashboard; anonymous Viewer received 200. |
| O-08 | Decision | How do Problem Details, logs, and Tempo trace IDs correlate? | Use W3C trace ID (32 lowercase hex) as Problem Details `traceId` when an Activity exists, request identifier only as fallback; Serilog adds `TraceId`/`SpanId` from `Activity.Current`; Tempo stores the same trace ID; no trace IDs in metric labels. | S4-01, S4-02, AC4-01 | Resolved; Part 1. |
| O-09 | Decision | Span boundaries and attributes? | HTTP server span from ASP.NET Core; SQL spans from SqlClient for EF and direct procedure; custom `availability.evaluate`, `booking.allocate`, `cache.get/set/remove`; bounded outcome tags; no IDs/keys/parameters; remove raw `url.path`/`url.query` from server spans. | S4-02, S4-05 | Resolved; Part 1. |
| O-10 | Decision | Metric names, units, labels, buckets, and outcome vocabularies? | `scheduler.http.requests` counter; `scheduler.booking.outcomes` counter; `scheduler.booking.procedure.attempts` counter; `scheduler.cache.operations` counter; `scheduler.booking.allocation.duration` histogram; built-in `http.server.request.duration` histogram with bounded View; units/buckets/labels defined in Part 1. | S4-03, AC4-05 | Resolved; Part 1. |
| O-11 | Decision | Sampling and export/flush bounds? | ParentBased(TraceIdRatioBased(ratio)); Compose ratio 1.0; batch delay 1 s, export timeout 5 s, queue 2048, batch 512, OTLP timeout 3 s; metrics interval 5 s/timeout 3 s; smoke sends `traceparent` flags `01`; lower ratios mean unsampled traces are absent. | S4-06, S4-V01 | Resolved; Part 1. |
| O-12 | Decision | How to log one request event without raw path/query? | Custom request-completion middleware placed before the exception handler; log route template, method, status class/code, bounded outcome, elapsed, trace/span; no request body/headers/query/path. Do not use `UseSerilogRequestLogging`. | S4-01, S4-05 | Resolved; Part 1. |
| O-13 | Decision | How to protect sensitive data in spans/logs/metrics? | Remove IDs/keys from logs; remove `url.path`/`url.query`/user agent from server spans; never enable SQL parameter capture or HTTP header capture; keep SQL command text without parameter values; custom spans omit IDs; metric labels are bounded vocabularies only. | S4-05, AC4-07 | Resolved; Part 1. |
| O-14 | Decision | How does the API behave when telemetry is unavailable? | Registration does not connect; exporters run asynchronously with bounded queues/timeouts; no telemetry health check; API start/booking/readiness unaffected; new exports resume after recovery; no backfill of dropped telemetry; bounded host shutdown flush. | S4-05, S4-V01 | Resolved; Parts 1 and 3. |
| O-15 | Decision | Zero-traffic dashboard behavior? | Rate/counter panels show no data or `0` via `noValue`; latency p95 shows `No data`; hit/error ratios use a guarded denominator with `or vector(0)` and a documented 0% meaning; no synthetic latency points. | S4-03, S4-04, AC4-04 | Resolved; Part 2. |
| O-16 | Decision | How are telemetry checks automated without mocks? | Unit tests use `ActivityListener`/`MeterListener` and Serilog test sinks; end-to-end tests start real Collector/Prometheus/Tempo/Grafana via Testcontainers and query their APIs with bounded polling; operational Compose checks capture container JSON logs and dashboard inspection. | S4-06, S4-V01-S4-V03 | Resolved; Part 3. |
| O-18 | Risk | EF Core and SqlClient instrumentation could duplicate spans. | Do not add EF Core instrumentation; SqlClient observes the underlying commands for both EF reads and the direct stored-procedure call, with `db.stored_procedure.name` for the procedure. | S4-02 | Resolved; Part 1. |
| O-19 | Risk | StackExchange.Redis instrumentation is beta. | Use explicit cache activities/metrics in `BoundedCache`; no beta package. | S4-02, S4-03 | Resolved; Part 1. |
| O-20 | Risk | Grafana anonymous access could be over-privileged. | Use anonymous Viewer on loopback only; dashboards and data sources are read-only; no admin credentials committed; document local-demo access. | S4-04, S4-05 | Probed; Part 2. |
| O-21 | Risk | Image digests/tags could drift. | Pin tags plus manifest-list digests, `platform: linux/amd64`, and keep Compose/test image references aligned; record versions in deploy notes. | S4-04 | Resolved; Part 2. |
| O-22 | Decision | Documentation/deployment records? | Add `docs/test/4-test.md` during implementation, generate `docs/test/4-manual-test.md` in the test phase, and add `docs/deploy-notes/4-deploy-notes.md` with startup/access/retention/recovery/troubleshooting. | S4-06, AC4-08 | Resolved; Part 3. |

## Phase 6: Dependency-ordered change plan

Detailed file-level steps belong to the plans phase. This order keeps instrumentation contracts, backend wiring, verification, and documentation aligned.

| Step | Requirement IDs | Actual or proposed files / components | Change and dependency | Verification / completion criteria |
| --- | --- | --- | --- | --- |
| 1 | S4-01-S4-06 | `Directory.Packages.props`, API/test project files, `ServiceScheduler.slnx`, image pins | Add pinned Serilog/OTel packages, Testcontainers base package for telemetry tests, and telemetry project entries. | Restore/build succeeds; package graph has no vulnerable/prerelease drift; image digests/tags match Part 2. |
| 2 | S4-01, S4-05 | `Infrastructure/Telemetry/TelemetryOptions.cs`, validator, `LoggingSetup`, `ActivityLogEnricher`, `RequestLoggingMiddleware`; `appsettings.json`; `Program.cs` | Add Serilog JSON logging, request event, service identity, log-level overrides, trace-ID normalization, Problem-code item, and sanitized existing log messages. | Unit tests: options, enricher fields, request event route/status/outcome, no raw path/query/IDs; existing logger-capture tests still pass. |
| 3 | S4-02, S4-03, S4-05 | `Infrastructure/Telemetry/ServiceSchedulerTelemetry.cs`, OTel registration, `BoundedCache`, `AvailabilityService`, `BookingAllocator`, `SqlAppointmentBookingProcedure`, `AppointmentsController`, `Program.cs` | Add custom activities/instruments, OTel ASP.NET Core/runtime/SqlClient registration, metric Views, sampler, resource attributes, span sanitization, and asynchronous OTLP exporters. Keep booking/cache decisions and budgets unchanged. | Unit listener tests for metrics/spans/outcome vocabularies; no parameter/URL/ID attributes; telemetry-disabled behavior unchanged. |
| 4 | S4-03, S4-04, S4-05 | `telemetry/collector.yaml`, `prometheus.yml`, `tempo.yaml`, Grafana provisioning/dashboard JSON, `compose.yaml`, `.env.example`, README/setup access docs | Add pinned telemetry services, volumes, ports, resource limits, pipelines, retention, data sources, dashboard, and local access; API remains independent of telemetry health. | `docker compose config`; Collector config validate; Prometheus/Tempo/Grafana start; fresh startup provisions dashboard/data sources; zero-traffic panels behave as documented. |
| 5 | S4-02, S4-03, S4-05, S4-06 | New telemetry test project/collection, fixtures, contract tests | Add real-backend smoke checks (Prometheus/Tempo/Grafana APIs, bounded polling), trace/log correlation, span/metric attribute contract, Redis degradation, and telemetry outage/recovery checks. | All required telemetry tests pass against real containers; failures include useful diagnostics; no mocked-sink-only evidence. |
| 7 | S4-01-S4-06 | `docs/test/4-test.md`, `docs/deploy-notes/4-deploy-notes.md`, README, architecture, setup guide, docs index, cache/deploy records | Record implemented behavior, access, retention, outage/recovery, and unexecuted checks; preserve prior ticket records. | Documentation matches observed behavior; no planned result is presented as executed; links resolve. |
| 8 | S4-01-S4-06 | Verification execution and evidence consolidation | Run unit/integration/telemetry/operational checks; record Passed/Failed/Blocked/Not run; fix in-scope failures and rerun. | Required failures/blockers prevent completion; manual Grafana/log inspection remains separate; ticket acceptance only after all required automated and human evidence. |

### Database, cache, configuration, and deployment impact

- Database impact: **none**. No table, column, relationship, constraint, index, procedure, seed, migration, runtime grant, or permission change is proposed. Telemetry data is stored in Prometheus/Tempo local volumes, not SQL Server.
- Cache impact: **none beyond instrumentation**. The catalog key, payload/version, 10-minute TTL, 250 ms operation budget, 500/750 ms cumulative budgets, SQL fallback, and readiness degradation behavior remain unchanged; no cache invalidation or namespace change is required; cache activities/metrics do not block or add waits; cache keys/dealership IDs are removed from logs.
- Booking impact: **none**. The procedure, technician-then-bay lock order, transaction, bounded candidate attempts, deadlock retries, timeouts, request budget, and restricted write boundary are unchanged; telemetry records outcomes and does not add network waits inside the booking path.
- Configuration impact: new `Scheduler:Telemetry` options (disabled by default), log-level overrides, and Compose telemetry environment/config; no new secret is required for the default local stack (anonymous read-only Grafana on loopback). Prometheus/Tempo/Grafana configuration files are committed.
- Deployment impact: new local services, ports, volumes, resource limits, retention, and access instructions; `docs/deploy-notes/4-deploy-notes.md` is required. No SQL migration or cache deployment action.
- Package impact: new Serilog/OpenTelemetry/Testcontainers packages; no change to existing EF Core, Redis, SQL Server, or test-stack pins except direct references to already-used transitive packages.

## Rollback or forward recovery

| Area | Recovery action | Limits |
| --- | --- | --- |
| Application telemetry configuration | Set `Scheduler:Telemetry:Enabled=false` (or remove the Compose env) and restart the API; Serilog console logging remains; no database/cache rollback. | Telemetry traces/metrics stop; logs remain console JSON. No business data loss. |
| Telemetry services | Stop/remove Collector/Prometheus/Tempo/Grafana; keep the API/SQL/Redis running. Restart them to resume new telemetry. | Dropped telemetry cannot be recovered unless still queued; no backfill. Prometheus/Tempo volumes removed with `docker compose down -v` lose telemetry history only. |
| Collector/Prometheus/Tempo failure | Restart the failed service; check Collector queues/retries, Prometheus OTLP flag/storage, Tempo storage/WAL/retention. | API behavior is unaffected; readiness does not include telemetry. |
| Grafana provisioning | Remove/replace provisioning files or the Grafana volume; dashboards/data sources are recreated on restart. | Provisioned dashboards are not business data; `allowUiUpdates: false` means UI edits are not the source of truth. |
| Logging/OTel code rollback | Revert the API artifact and remove telemetry packages/configuration; existing routes/health/booking remain. | No SQL/cache migration to roll back. |
| Telemetry test stack | Stop Testcontainers network/containers; no persistent volumes used by tests. | Test data is ephemeral; no production/shared dependency access. |

## Handoffs to other parts

| Source part | Finding / decision and evidence | Target part | Required action | Dependency / status |
| --- | --- | --- | --- | --- |
| Overview | Package/image pins, no-DB/no-cache-change constraints, trace-ID contract. | Parts 1-3 | Use the same versions, IDs, and acceptance mappings. | Planning. |
| Part 1 | Log/span/metric contracts, sanitization, outage bounds, sampling. | Parts 2 and 3 | Build dashboards/smoke assertions against these names/attributes; test outage behavior. | Planning. |
| Part 2 | Collector/Prometheus/Tempo/Grafana config, paths, retention, PromQL, zero traffic. | Parts 1 and 3 | Align exporter endpoints, metric names, dashboard checks, and operational instructions. | Planning. |
| Part 3 | Test strategy, fixtures, evidence rules, docs. | Implementation | Write/execute tests; record actual evidence. | Planned; not executed. |
| Ticket 3 | Booking procedure/cache correctness guarantees. | Ticket 4 | Do not alter locking/retries/budgets/cache key/invalidation; instrument only. | Existing behavior. |

## Phase 7: Scope verification

### 7.1 Change-to-requirement trace

| Change / affected component | Requirement ID / source | Direct requirement or necessary dependency | Research evidence / proposed verification |
| --- | --- | --- | --- |
| Serilog JSON console logging, enricher, request middleware | S4-01, AC4-01, S4-V02 | Required | Part 1; unit log tests, T-15, S4-V02. |
| Problem Details trace-ID normalization and code item | S4-01, S4-02, AC4-01 | Required for correlation | Part 1; Problem Details/trace tests. |
| OpenTelemetry SDK, resources, sampler, OTLP exporter | S4-01-S4-05 | Required | Part 1; package/build and outage tests. |
| ASP.NET Core and SqlClient instrumentation | S4-02, AC4-02 | Required | Part 1; Tempo span checks; no EF instrumentation duplicate. |
| Custom availability/allocation/cache activities | S4-02, S4-03, AC4-02 | Required | Part 1; Tempo/cache span checks. |
| Metric instruments, Views, labels, buckets | S4-03, AC4-05, S4-V02 | Required | Part 1; Prometheus label/name checks; dashboard queries. |
| Remove IDs/keys/paths/parameters from telemetry | S4-05, AC4-07, S4-V02 | Required | Part 1; unit/log/Tempo/Prometheus contract tests. |
| Bounded exporter/Collector queues, timeouts, sampling | S4-05, S4-V01 | Required | Parts 1-3; outage/recovery tests. |
| Collector/Prometheus/Tempo/Grafana Compose/config | S4-03, S4-04, AC4-03, AC4-04 | Required | Part 2; config validation, startup, dashboard provisioning. |
| Dashboard PromQL, zero-traffic behavior, access docs | S4-03, S4-04, AC4-04 | Required | Part 2; S4-V03. |
| Telemetry contract/end-to-end/outage tests | S4-05, S4-06, S4-V01-S4-V03 | Required | Part 3; executed evidence. |
| Documentation/deploy notes | S4-04, S4-06, AC4-08 | Required | Parts 2 and 3; documentation review and executed commands. |
| No DB/cache/permission change | Ticket constraints | Required preservation | Overview; inspect migrations/cache/procedure unchanged. |

### 7.2 Scope checklist

- [x] Every S4-01-S4-06 requirement, AC4-01-AC4-08 acceptance criterion, and T-13/T-15/S4-V01-S4-V03 case has a research owner or explicit gap.
- [x] Each proposed change traces to a ticket requirement or necessary dependency.
- [x] Assumptions, exclusions, and deferred extensions remain explicit.
- [x] Optional improvements (exemplars, Loki, alerting) are separated from required work.
- [x] Existing behavior, proposed additions, and research-phase probes are distinguished; no ticket test or acceptance is claimed.
- [x] Database, permissions, cache, packaging, telemetry, and test impacts are assessed.
- [x] Proposed paths/commands are labelled; no implementation or passing evidence is invented.
- [x] Research verification completed 2026-11-02 with corrections; see [[#Verification]].

### 7.3 Items flagged for removal

| Item | Why it falls outside scope | Follow-up, if any |
| --- | --- | --- |
| Loki/centralized log storage | Ticket says console/container output is the core log path; Loki is optional/outside. | Separate ticket if centralized search is required. |
| Alerting rules and incident routing | Explicit production-operations exclusion. | Separate production design. |
| Production hosting, retention beyond local, HA | Local demo scope; no production claims. | Separate design. |
| Database/cache schema or key changes | Ticket states none intended; telemetry stores data outside SQL/Redis. | None. |
| Authentication for Grafana/Prometheus/Tempo | Local loopback demo; anonymous Viewer is read-only. | Production auth design if exposed. |
| Custom application UI | Backend-only; Grafana is operational. | N/A. |
| Exemplars | Optional in ticket; not required by acceptance. | Add only if later required and supported. |

## Dependencies

| Dependency | Requirement / component blocked | Resolution needed | Status / evidence |
| --- | --- | --- | --- |
| .NET SDK `10.0.403` and net10.0 packages | Serilog/OTel build | Pin package versions with net10.0 assets; restore/build during implementation. | Existing SDK; package metadata inspected. |
| `Microsoft.Data.SqlClient` 6.1.7 | SqlClient spans | Use current transitive version; no package upgrade required; verify no parameter capture. | Existing transitive pin; instrumentation README says 4.0+ supported. |
| SQL Server 2022 CU23 and Redis `7.4.12-alpine` | Real-dependency telemetry tests | Reuse existing Testcontainers images/fixtures. | Existing. |
| Docker `25.0.3` / Compose v2.24.6 | Telemetry stack start, smoke, outage | Docker must be running; pinned images pullable. | Existing environment; images pulled/probed. |
| Collector/Prometheus/Tempo/Grafana images | S4-03, S4-04 | Pin tags/digests in Compose and tests; keep aligned. | Probed versions/digests in Part 2. |
| Existing health/Problem Details/test fixtures | Instrumentation/correlation tests | Extend without changing readiness semantics or booking/cache budgets. | Inspected. |
| No SQL migration/cache key change | Constraint preservation | Do not add migrations, grants, key/TTL changes. | Ticket statement; inspected docs. |

## Decisions made

| Decision | Reason / alternatives | Requirement impact | Date / evidence |
| --- | --- | --- | --- |
| Use multipart research for ticket 4. | Application instrumentation, backend stack, and verification need separate evidence and have dependencies. | Keeps one primary entry point and traceability. | 2026-11-02; research guide. |
| Pin Serilog.AspNetCore 10.0.0 and OpenTelemetry 1.20.1 family. | Latest stable net10.0-compatible versions; avoid prerelease 1.21.0-rc.1 and Serilog dev builds. | S4-01-S4-03. | 2026-11-02; NuGet metadata. |
| Use OTLP/gRPC to Collector, Collector -> Tempo gRPC, Collector -> Prometheus OTLP/HTTP. | Matches .NET SDK defaults and Prometheus 3.x OTLP receiver; one Collector pipeline. | S4-02-S4-04. | 2026-11-02; docs/probes. |
| Use `otlp_grpc`/`otlp_http` Collector component names. | `otlp`/`otlphttp` are deprecated aliases in 0.164.0. | S4-03, S4-04. | 2026-11-02; Collector validate probe. |
| Use Tempo 3.1.1 with backend-scheduler retention. | Tempo 3 removed `ingester`/`compactor`; proposed config starts successfully. | S4-02, S4-04. | 2026-11-02; Tempo probe. |
| Use custom request middleware, not `UseSerilogRequestLogging`. | Default request logging captures `RequestPath`; ticket requires bounded route-template logging. | S4-01, S4-05. | 2026-11-02; Part 1. |
| Do not add EF Core or Redis instrumentation packages. | SqlClient covers EF and direct SQL; Redis package is beta; custom cache activities give control. | S4-02, S4-03. | 2026-11-02; Part 1. |
| Change Problem Details `traceId` to W3C trace ID with fallback. | Makes log/Tempo correlation direct; keeps a single API property. | S4-01, S4-02. | 2026-11-02; Part 1. |
| Use anonymous Grafana Viewer on loopback. | No credentials to manage for local demo; read-only dashboards/data sources; provisioning verified. | S4-04. | 2026-11-02; Grafana probe. |
| Use a separate telemetry test project/collection. | Keeps existing SQL/Redis suites independent; real-backend smoke checks need their own fixtures. | S4-06. | 2026-11-02; Part 3. |
| Keep telemetry disabled by default outside Compose/tests. | Existing tests/startup must not attempt exports to localhost; Compose enables explicitly. | S4-05. | 2026-11-02; Part 1. |

## Out of scope

| Item | Ticket exclusion or reason | Follow-up, if any |
| --- | --- | --- |
| Central log storage/Loki | Console/container output is the core log path; Loki optional/outside. | Separate ticket. |
| Alerting/incident management | Production operations exclusion. | Separate ticket. |
| Production hosting, HA, backup/retention policy beyond local | Local demo, no production claims. | Separate design. |
| Authentication/authorization for telemetry UIs | Loopback local demo; anonymous Viewer read-only. | Production access design. |
| Database schema/migration/procedure/permission changes | Ticket states none intended. | None. |
| Cache key/payload/TTL/invalidation changes | Ticket states none intended; telemetry observes. | None. |
| Custom application UI | Backend-only; Grafana operational. | N/A. |
| Exemplars | Optional and not required. | Add only if later required. |
| External integrations/payments/notifications/background jobs | Initial release exclusions. | Separate tickets. |

## Files referenced

Actual inspected files and symbols:

- `docs/tickets/4 - Observability.md` (updated 2026-11-02): requirements S4-01-S4-06, acceptance criteria, T-13/T-15/S4-V01-S4-V03, scope, exclusions, and open research decisions.
- `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/README.md`, `docs/database/overview.md`, `docs/database/schema.md`, `docs/database/migration.md`, `docs/database/cache.md`, `docs/database/maintenance.md`, `docs/test/README.md`, `docs/test/backend-test-template.md`, `docs/test/integration-test-template.md`, `docs/test/3-test.md`, `docs/plans/README.md`, `docs/deploy-notes/README.md`, `docs/deploy-notes/3-deploy-notes.md`, `docs/research/README.md`, `docs/templates/research-template.md`, `docs/research/3/00-overview.md`, `docs/research/3/02-booking-procedure-and-concurrency.md`.
- `src/ServiceScheduler.Api/Program.cs`; `ServiceScheduler.Api.csproj`; `appsettings.json`; `appsettings.Development.json`.
- `src/ServiceScheduler.Api/Features/Dealerships/DealershipsController.cs`; `Features/ServiceCatalog/ServiceCatalogController.cs`, `ServiceCatalogService.cs`, `ServiceCatalogCacheKeys.cs`; `Features/Availability/AvailabilityController.cs`, `AvailabilityService.cs`, `AvailabilityQueries.cs`, `StartsAtParser.cs`, `AvailabilityInterval.cs`, `OpeningCoverage.cs`, `ShiftCoverage.cs`, `HalfOpenOverlap.cs`; `Features/Appointments/AppointmentsController.cs`, `AppointmentService.cs`, `BookingAllocator.cs`, `BookingCandidatePlanner.cs`, `BookingOptions.cs`, `BookingOptionsValidator.cs`, `AppointmentBookingOutcome.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Caching/BoundedCache.cs`, `CacheOptions.cs`, `CacheHealthCheck.cs`, `RedisCacheConfiguration.cs`, `RedisCacheOptionsSetup.cs`, `CacheReadResult.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs`, `IAppointmentBookingProcedure.cs`, `SchedulerDbContext.cs`, `Configurations/SeedData.cs`; `Infrastructure/Persistence/Configurations/*.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Errors/ProblemCodes.cs`, `ProblemResponse.cs`, `SqlFailureClassifier.cs`, `SqlDependencyExceptionHandler.cs`; `Infrastructure/OpenApi/ProblemDetailsSchemaTransformer.cs`, `ApiOperationTransformer.cs`; `Infrastructure/Health/HealthResponseWriter.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Setup/DatabaseSetupCommand.cs`, `DatabaseSetupRunner.cs`, `FutureShiftSeeder.cs`, `RuntimePrincipalProvisioner.cs`.
- `tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj`; `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj`; `Fixtures/SchedulerApiFactory.cs`, `SqlServerContainerFixture.cs`, `SqlServerTestBase.cs`, `CacheSqlServerTestBase.cs`, `RedisContainerFixture.cs`, `CapturingLoggerProvider.cs`, `SchedulingTestData.cs`; `Resilience/RedisOutageTests.cs`, `Resilience/CombinedOutageTests.cs`, `Booking/BookingOutageTests.cs`, `Booking/BookingFailureBoundaryTests.cs`; `Startup/ProblemDetailsTests.cs`, `OpenApi/OpenApiTests.cs`.
- `compose.yaml`, `.env.example`, `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `ServiceScheduler.slnx`, `src/ServiceScheduler.Api/Dockerfile`, `.editorconfig`.
- Research-phase probe environment: temporary configs under the local TEMP directory and the pinned containers `otel/opentelemetry-collector:0.164.0`, `prom/prometheus:v3.15.1`, `grafana/tempo:3.1.1`, `grafana/grafana:13.3.0`; results are configuration evidence only, not ticket tests.

Proposed files/directories not yet existing:

- `src/ServiceScheduler.Api/Infrastructure/Telemetry/` (`TelemetryOptions`, validator, `ServiceSchedulerTelemetry`, `LoggingSetup`, `ActivityLogEnricher`, `RequestLoggingMiddleware`, OTel registration/span sanitization).
- `telemetry/` (`collector.yaml`, `prometheus.yml`, `tempo.yaml`, Grafana provisioning/data sources/dashboard JSON).
- `tests/ServiceScheduler.TelemetryTests/` or an equivalent telemetry collection, and telemetry fixtures/checks.
- `docs/test/4-test.md`, `docs/test/4-manual-test.md`, `docs/deploy-notes/4-deploy-notes.md`, and a plan under `docs/plans/4/`.

## Technical references

Primary documentation and metadata checked on 2026-11-02 (implementation must revalidate exact package/image behavior):

- [OpenTelemetry .NET OTLP exporter](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md): endpoint/protocol, timeout, batch/metric-reader settings, retry behavior.
- [OpenTelemetry .NET metrics customization](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/customizing-the-sdk/README.md): `AddView`, histogram boundaries, tag-key filtering.
- [OpenTelemetry SqlClient instrumentation](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.SqlClient/README.md): options, emitted attributes, parameter-capture defaults, stored-procedure representation.
- [OpenTelemetry ASP.NET Core instrumentation](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.AspNetCore/README.md): options, emitted attributes, `url.path`/`url.query`, filtering/enrichment.
- [OpenTelemetry runtime instrumentation](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.Runtime/README.md): runtime metrics and registration.
- [OpenTelemetry HTTP semantic conventions](https://opentelemetry.io/docs/specs/semconv/http/http-metrics/): `http.server.request.duration`, attributes, recommended buckets.
- [Prometheus OpenTelemetry guide](https://prometheus.io/docs/guides/opentelemetry/): `--web.enable-otlp-receiver`, OTLP endpoint, resource-attribute translation.
- [Prometheus OTLP configuration](https://prometheus.io/docs/prometheus/latest/configuration/configuration/#otlp_config): `otlp.promote_resource_attributes`, `translation_strategy`.
- [Collector OTLP HTTP exporter](https://github.com/open-telemetry/opentelemetry-collector/blob/main/exporter/otlphttpexporter/README.md) and [Collector OTLP gRPC exporter](https://github.com/open-telemetry/opentelemetry-collector/blob/main/exporter/otlpexporter/README.md): component names, endpoints, TLS, queues/retries.
- [Tempo configuration](https://grafana.com/docs/tempo/latest/configuration/) and [Tempo 3 migration](https://grafana.com/docs/tempo/latest/set-up-for-tracing/setup-tempo/migrate-to-3/): Tempo 3 removal of `ingester`/`compactor`, backend scheduler, local storage.
- [Tempo HTTP API](https://grafana.com/docs/tempo/latest/api_docs/): trace-by-ID and search endpoints.
- [Grafana provisioning](https://grafana.com/docs/grafana/latest/administration/provisioning/): data sources, dashboard providers, UIDs, and provisioning paths.
- [Serilog.AspNetCore](https://github.com/serilog/serilog-aspnetcore): host/DI logging, JSON console output, and request-logging behavior.
- NuGet `api.nuget.org` package metadata: version/target-framework/dependency inspection for the pinned packages; Docker Hub/GitHub release metadata and `docker pull` for the pinned image tags/digests.

## Verification

- Command: `/orchestrator-ticket verify research 4`.
- Status: **Passed** for planning.
- Verified on: 2026-11-02.
- Checkout: HEAD `92a81efde5dac1070669fff13193e452dc9b6529` on `main`; the four research files are untracked working-tree additions and `docs/README.md` is modified. Application code, Compose, and test projects match the cited baseline.
- Supersedes: the prior verification record, whose Part 2 and Part 3 fingerprints did not match the current research files. This pass re-inspected all four parts and re-ran the metadata and configuration checks below; the fingerprints here describe the current inputs.
- Input fingerprints (SHA-256 of LF-normalized content; the overview fingerprint covers the content before this Verification section; each part fingerprint covers the whole current file after the corrections listed below):

| Input | Fingerprint |
| --- | --- |
| `docs/tickets/4 - Observability.md` | `89292f938818ce75699709580513c7922a84c12853c8e52115265917b99db3bc` |
| `docs/research/4/00-overview.md` (content before this section) | `0b5d6a76a93071dc86a8a1511cd17f9c281d744c200ff798ea41f63a4025cb37` |
| `docs/research/4/01-instrumentation-and-correlation.md` | `bdfcf8f90adfee08b203fc589f0f4a0343782186899e237bd43ead386d8d469b` |
| `docs/research/4/02-telemetry-stack-and-dashboards.md` | `26a61ce76f7df9049a64e4b7efcb653b07d83216e0c2005af184ecc4ea5b7372` |
| `docs/research/4/03-verification-workload-and-operations.md` | `d37e15d866031cf33abf14a43a1dba26e3bc2b4cde388dfd3ec5f54f0946137f` |

- Forward trace: every S4-01-S4-06 requirement, AC4-01-AC4-08 acceptance criterion, ticket-defined T-13 and T-15 case, and S4-V01-S4-V03 case has a research owner in the overview, the relevant part, and Phase 7.1. The ticket's eight unlabeled acceptance bullets were checked in order against AC4-01-AC4-08; the labels remain research-local and are not new ticket IDs.
- Reverse trace: every proposed change in Phase 6 and Phase 7.1 traces to a ticket requirement or a necessary dependency; exclusions and assumptions preserve the ticket boundaries; no implementation, executed test, or acceptance is claimed.
- Requirement traceability (the T/S4-V IDs are required checks; executed results belong to `docs/test/4-test.md` and are not established here):

| Requirements | Research coverage | Verification mapping |
| --- | --- | --- |
| P-03; S4-01, AC4-01, AC4-07 | Overview; Part 1 logging, identity, and correlation; Part 3 data-contract tests | T-15, S4-V02; TC4-02, TC4-03, TC4-08, TC4-14 |
| S4-02, AC4-01, AC4-02 | Part 1 tracing and span boundaries; Part 2 OTLP paths; Part 3 traces | T-15, S4-V02; TC4-04-TC4-06, TC4-14 |
| S4-03, AC4-03, AC4-05 | Part 1 metrics; Part 2 PromQL/dashboards; Part 3 delivery checks | T-15, S4-V02, S4-V03; TC4-05, TC4-07, TC4-09, TC4-10 |
| S4-04, AC4-03, AC4-04, AC4-08 | Part 2 stack/provisioning/access; Part 3 documentation plan | T-15, S4-V03; TC4-09, TC4-10, TC4-18 |
| S4-05, AC4-06, AC4-07 | Overview/Part 1 sanitization and outage bounds; Parts 2-3 outage/delivery | T-13, S4-V01, S4-V02; TC4-08, TC4-11-TC4-13, TC4-19 |
| S4-06, AC4-08 | Part 3 smoke/outage checks and recording | S4-V03; TC4-09, TC4-18 |
| P-01, P-02 constraints; S4-05 | Overview no-change sections; Part 1/Part 3 preservation checks | TC4-11, TC4-13, TC4-19; prior ticket 1-3 evidence remains separate |

- Existing-evidence checks: inspected `Program.cs`, `ProblemCodes`/`ProblemResponse`/`SqlDependencyExceptionHandler`, `BoundedCache`, `ServiceCatalogService`, `AppointmentService`/`BookingAllocator`/`AppointmentsController`/`AppointmentBookingOutcome`, `SqlAppointmentBookingProcedure`, `DatabaseSetupRunner`, `HealthResponseWriter`, `Directory.Packages.props` and project files, `compose.yaml`, `appsettings.json`, the test fixtures (`SchedulerApiFactory`, `SqlServerContainerFixture`, `SqlServerTestBase`, `CacheSqlServerTestBase`, `RedisContainerFixture`, `CapturingLoggerProvider`, `SchedulingTestData`), `SeedData`, and the architecture/README/cache/test guides. The cited files, symbols, seed IDs, fixed clock, time zones, opening hours, cache budgets, Health/write boundaries, absence of telemetry code, and package/image baseline match the checkout. The resolved `Microsoft.Data.SqlClient` version is 6.1.7.
- Impact checks: database impact is none (no table, column, constraint, index, procedure, grant, seed, or migration change; telemetry data lives outside SQL Server). Cache impact is instrumentation-only (key/version, 10-minute TTL, 250 ms per-operation and 500/750 ms cumulative budgets, SQL fallback, invalidation, and readiness behavior unchanged). Booking impact is none (procedure, technician-then-bay locking, transaction, candidate/deadlock bounds, request budget, and restricted write boundary unchanged). Configuration/deployment impact is the new `Scheduler:Telemetry` options, Compose telemetry services/volumes, pinned packages/images, access instructions, and deploy notes; no new secret is required for the local loopback stack.
- Verification re-checks performed in this pass (supplementing, not replacing, the research probes): NuGet flat-container HEAD requests returned `200` for all ten proposed package/version pairs; the Serilog.AspNetCore 10.0.0 nuspec shows the `net10.0` dependency group and the cited Serilog/Sinks versions; `docker buildx imagetools inspect` resolved all four image tags to the recorded manifest-list digests; the current Part 2 Collector configuration passed `validate` with exit code 0; the current Prometheus configuration passed `promtool check config` (`SUCCESS`); Tempo 3.1.1 started with the current configuration and its `/ready` endpoint returned `200`. Grafana provisioning was not rerun; the recorded Grafana probe remains the reference for data-source/dashboard provisioning. These checks are verification evidence, not ticket tests.
- Corrections applied in this pass: replaced the stale Part 2 and Part 3 fingerprints with current content hashes; corrected the operational log-capture command in Part 3 to `docker compose logs --no-color --no-log-prefix api` so captured lines are JSON without Compose prefixes; clarified the Tempo readiness probe to `:3200/ready` after verifying the endpoint. The earlier line-citation, package-baseline, and `appsettings.Development.json` corrections from the prior record remain applied. The affected forward and reverse traces were rechecked after these edits.
- Non-blocking observations for planning:
  - Cache hit ratio is transport-level: a corrupt cached payload counts as a `get` hit before `CacheCorruptPayload` triggers SQL fallback. If the dashboard must reflect effective catalog hits, define a separate bounded counter or document the ratio's meaning.
  - `BoundedCache` currently names log operations `read`/`write`/`remove`; the metric contract uses `get`/`set`/`remove`. The implementation must map these explicitly.
  - `scheduler.http.requests` records all requests, including `/health/live` and `/health/ready`. The plan should filter health traffic or document it as request-rate/latency traffic.
  - The procedure-attempt vocabulary includes `unexpected`, but `BookingProcedureStatus` has no such member. Define it as an unmapped-exception bucket or remove it from the declared contract.
  - The proposed 30-second delivery/observation window is a planning default; implementation must record the chosen window and measured result rather than treating it as verified.
  - `scheduler.booking.outcomes` is defined as recorded after `AppointmentService.CreateAsync` returns; controller-level request-shape and `startsAt`-parse failures return before that call. The plan must decide whether those responses are intentionally covered only by `scheduler.http.requests` or recorded through a controller-level wrapper.
- Result: **Passed** for planning. This record does not establish implementation, executed tests, Compose behavior, dashboard rendering, or ticket acceptance; all ticket checks remain **Not run**. The next phase is `/orchestrator-ticket plan 4`.
