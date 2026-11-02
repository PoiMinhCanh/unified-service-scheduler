---
title: "4 - Observability: Executed Test Report"
tags: [test, service-scheduler, observability, telemetry, prometheus, grafana, tracing]
ticket: 4
status: automated-passed-manual-pending
updated: 2026-11-02
---

# 4 - Observability: Executed test report

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/01-instrumentation-and-correlation|Instrumentation and correlation]]; [[research/4/02-telemetry-stack-and-dashboards|Telemetry stack and dashboards]]; [[research/4/03-verification-workload-and-operations|Verification and operations]].
- Plan: [[plans/4/README|Ticket 4 implementation plan]].
- Review: [[review/4-review|Ticket 4 review]].
- Deploy notes: [[deploy-notes/4-deploy-notes|Ticket 4 deployment notes]].
- Manual checklist: [[test/4-manual-test|Ticket 4 manual tests]] (generated 2026-11-02; all cases Not run; human execution and visual Grafana inspection remain pending).
- Requirements: S4-01 through S4-06; acceptance labels AC4-01 through AC4-08; T-13, T-15, S4-V01 through S4-V03; project P-03 with P-01/P-02 constraints.

> [!IMPORTANT]
> This report records executed automated and operational evidence. It does not establish human Grafana/log inspection, execute the generated [[test/4-manual-test|manual checklist]], or mark ticket acceptance Passed. Compose operational checks were executed manually and are recorded below; no standalone verification script is required.

## Scope, revision, and environment

- Implementation revision: checkout HEAD `92a81efde5dac1070669fff13193e452dc9b6529` plus the uncommitted ticket 4 working-tree change (application, telemetry configuration, tests, and documentation), including the review fixes R-01-R-07: client-cancellation handling, fixed test clock, appointment cleanup, procedure-attempt catch, strengthened health-exclusion check, and documentation corrections.
- Executed on: 2026-11-02 local time (container logs are UTC).
- Host: Windows 11, .NET SDK `10.0.403`, Docker server `25.0.3`, Docker Compose `v2.24.6-desktop.1`.
- Unit project: `tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj` (xUnit v3, `MeterListener`/`ActivityListener`; unit parallelization disabled because instrumentation listeners are process-wide).
- Integration project: `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj` (WebApplicationFactory, real SQL Server/Redis Testcontainers, real pinned Collector/Prometheus/Tempo/Grafana Testcontainers).
- Compose project: `scheduler-t4`, fresh named volumes.
- Synthetic data: seeded Saigon dealership `a1000000-0000-0000-0000-000000000001`, customer `c1000000-…0001`, vehicle `b1000000-…0001`, service type `51000000-…0001`; fixed test clock `2026-11-02T00:00:00Z` applied to both the SQL seed and the telemetry API hosts; future weekday starts within the 28-day shift horizon.
- Pinned telemetry images: Collector `0.164.0`, Prometheus `v3.15.1`, Tempo `3.1.1`, Grafana `13.3.0` with the manifest-list digests in [[research/4/02-telemetry-stack-and-dashboards|the stack research]] and `compose.yaml`.
- Packages: Serilog.AspNetCore `10.0.0`, Serilog.Formatting.Compact `3.0.0`, Serilog.Sinks.Console `6.1.1`, OpenTelemetry `1.20.1` family, Testcontainers `4.14.0`, `Microsoft.Data.SqlClient` `6.1.7`, `StackExchange.Redis` `2.7.27`.
- No migration, seed, procedure, runtime grant, cache key/payload/TTL, or booking-behavior change was made.

## Final automated results

| Check | Command | Observed result | Status |
| --- | --- | --- | --- |
| Restore/build | `dotnet restore`; `dotnet build --no-restore` | Build succeeded with 0 warnings and 0 errors. | Passed |
| Format | `dotnet format --verify-no-changes --no-restore` | No changes required after applying formatting to new files. | Passed |
| Unit suite | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | 188 passed, 0 failed. | Passed |
| Telemetry unit subset | `dotnet test ... --filter "FullyQualifiedName~Telemetry"` | 53 passed: options, trace-ID helper, enrichers, request middleware, client-cancellation handling, counters/spans, cache telemetry, allocation telemetry. | Passed |
| Integration suite | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | 175 passed, 0 failed, 3 m 25 s after the review fixes (real SQL Server/Redis plus pinned telemetry containers). | Passed |
| Telemetry integration subset | `dotnet test ... --filter "FullyQualifiedName~Telemetry"` | 16 passed: trace IDs, delivery/span coverage, metric contract, sensitive data, outages, Redis degradation, SQL dependency, provisioning/zero traffic, health exclusion, and procedure-attempt mapping/unexpected/cancellation. | Passed |

- Review fixes re-verified on 2026-11-02: client-aborted requests record `client_cancelled` with status 499/`4xx` at Information level; telemetry delivery tests use the fixed fixture clock and clean up created appointments; the health-exclusion check waits for positive-control series; and procedure attempts record `unexpected` for non-SQL command failures. See [[review/4-review|the review record]].

## Configuration and stack verification

| Check | Command / scenario | Observed result | Status |
| --- | --- | --- | --- |
| Compose resolution | `docker compose -p scheduler-t4 config` | Resolved with API telemetry enabled and no telemetry health dependency. | Passed |
| Collector config | `docker run --rm … otel/opentelemetry-collector:0.164.0 validate --config=…` | Exit code 0. | Passed |
| Prometheus config | `docker run --rm --entrypoint promtool … prom/prometheus:v3.15.1 check config …` | `SUCCESS` syntax validation. | Passed |
| Image pins | `docker compose up -d --build --wait` with pinned tags/digests | Collector, Prometheus, Tempo, Grafana, SQL Server, Redis, API all started; API image built. | Passed |
| Stack readiness | `curl http://127.0.0.1:13133/`; `:9090/-/ready`; `:3200/ready`; `:3000/api/health` | All returned HTTP 200. | Passed |
| Grafana provisioning | `GET /api/datasources/uid/prometheus`, `/uid/tempo`, `/api/dashboards/uid/scheduler-overview` | All returned 200 with the expected UIDs and dashboard title/panels. | Passed |
| API readiness after startup | `GET /health/live`, `/health/ready` | Live 200; readiness was transiently 503 while SQL warmed, then 200. | Passed with transient startup observation |
| API independence | `docker compose stop collector prometheus tempo grafana`; catalog/availability/booking/readiness | Live 200, ready 200, catalog 200, availability 200, booking 201, persisted appointment retrievable; telemetry restart resumed new exports. | Passed |
| Health traffic exclusion | Automated `HealthTrafficIsExcludedFromRequestMetricsAndServerSpans` | After health probes and a positive-control catalog request whose request/server-duration series were confirmed, `scheduler_http_requests_total{route=~"/health/.*"}` and `http_server_request_duration_seconds_count{http_route=~"/health/.*"}` were empty. | Passed |

## Delivery, traces, metrics, and correlation

| Check | Observed result | Status |
| --- | --- | --- |
| Collector delivery | Prometheus contained `scheduler_http_requests_total`, `scheduler_booking_outcomes_total`, `scheduler_booking_procedure_attempts_total`, `scheduler_cache_operations_total`, and `http_server_request_duration_seconds_count`; Tempo returned known trace IDs. | Passed |
| Prometheus labels | `job="unified-service-scheduler-api"`, `instance="scheduler-api"`, `deployment_environment_name="Development"`, and a `service_version` label (Compose image `1.0.0`; test host observed `1.0.0+92a81ef…`); bounded route/method/status/outcome/result labels only. No entity IDs, VINs, trace IDs, raw paths, or query strings in labels. | Passed |
| Span coverage | Known trace `1111…` returned 22 spans: HTTP server spans, EF `SELECT` spans for catalog/availability/booking, `EXECUTE dbo.CreateAppointment`, `cache.get`, `cache.set`, `availability.evaluate`, and `booking.allocate`. | Passed |
| Server-span sanitization | HTTP spans carried `http.route`, method, and status; `url.path`, `url.query`, user-agent, server address/port, and original method were absent. | Passed |
| SQL sanitization | SQL spans carried `db.system.name`, `db.namespace`, operation/query text, and the stored-procedure name; no parameter values were present (`SetDbQueryParameters` was not enabled). | Passed |
| Booking outcomes | Prometheus distinguished `created`, `capacity_unavailable`, `validation_error`; escaped dependency failures were represented by `scheduler.http.requests{outcome="dependency_unavailable"}` because the SQL connection failed before a controller booking result existed. | Passed with documented nuance |
| Cache outcomes | Prometheus distinguished `get/hit`, `get/miss`, `get/failure`, `set/success`, and `remove` results; the health probe was excluded from the catalog counters. | Passed |
| Booking conflict trace | The overlap precheck removed the only candidate pair, so the conflict trace contained HTTP and SQL spans but no `booking.allocate` span; the HTTP span and request outcome identified the conflict. | Passed with corrected expectation |
| Log/trace correlation | Compose request with `traceparent` `abab…` produced one JSON `RequestCompleted` event with the same `TraceId`, route template, status class, and outcome; the same trace ID was queryable in Tempo and through the Grafana Tempo proxy. | Passed |
| Log sanitization in Compose | Captured JSON line contained `RouteTemplate` and `TraceId`; it contained no `RequestPath`, `RequestId`, `ConnectionId`, raw path, query string, or synthetic VIN. ASP.NET Core scope properties are removed by `ScopePropertyFilter`. | Passed |
| Client cancellation | Unit tests for `ClientCancellationExceptionHandler` and `RequestLoggingMiddleware`: a request-aborted cancellation records `client_cancelled` with status 499/`4xx` at Information level, records no `internal_error`/5xx outcome, and carries no exception payload. | Passed |

## Outage and degradation checks

| Check | Observed result | Status |
| --- | --- | --- |
| Telemetry outage | With the Collector stopped, the API started and served catalog/readiness/booking (201) and retrieval (200); readiness remained 200 and reflects SQL/Redis only. No appointment was lost. | Passed |
| Telemetry recovery | After Collector restart, new traffic produced a queryable Tempo trace (`5555…`) and Prometheus request counters increased; lost telemetry was not backfilled. | Passed |
| Redis degradation | With Redis paused, catalog returned 200 via SQL fallback, booking returned 201, retrieval returned 200, readiness reported `Degraded` with HTTP 200, and a cache `get/failure` metric plus `CacheOperation` warning were emitted. Recovery returned readiness to `Healthy`. | Passed |
| SQL dependency | With an unreachable runtime SQL endpoint, catalog/availability/booking returned 503 `dependency_unavailable` (not 409 capacity), readiness returned 503 because SQL was down, the request outcome metric recorded `dependency_unavailable`, and the appointment count was unchanged. | Passed |
| Telemetry shutdown | Provider disposal is best-effort; a provider shutdown exception is logged and does not fail host shutdown. | Passed |

## Requirement and acceptance traceability

| Requirement / case | Evidence | Status |
| --- | --- | --- |
| S4-01 structured logs and correlation | Unit enricher/middleware/options tests; client-cancellation handler and middleware tests; integration Problem Details trace IDs; Compose JSON log correlation; scope-property filtering. | Passed |
| S4-02 HTTP/SQL/availability/cache tracing | Tempo spans for known traces; server-span sanitization; stored-procedure span. | Passed |
| S4-03 bounded metrics | Prometheus names/labels/outcomes; unit listener tests; dashboard PromQL checks. | Passed |
| S4-04 pinned stack/provisioning/instructions | Compose config, pinned images, Grafana provisioning API checks, deploy notes/setup/README updates. | Passed automated; visual inspection Not run |
| S4-05 sensitive data/bounded outage behavior | Trace/log/metric contract tests; Collector health filter; telemetry outage/recovery; Redis/SQL outage checks; client cancellation records `client_cancelled`/499 rather than a 5xx/internal error. | Passed |
| S4-06 automated checks | Unit + integration telemetry suites with the fixed fixture clock and appointment cleanup; direct Compose operational checks recorded above. | Passed automated and direct Compose checks |
| AC4-01 booking log/Tempo correlation | Compose and automated trace/log correlation. | Passed automated |
| AC4-02 SQL/custom spans and cache activity | Trace `1111…` span inventory. | Passed |
| AC4-03 Collector delivery | Prometheus/Tempo queries through the Collector. | Passed automated |
| AC4-04 provisioned dashboards and zero traffic | Grafana API provisioning and fresh-stack zero-traffic PromQL. | Passed automated; visual inspection Not run |
| AC4-05 bounded outcome metrics | Booking/cache outcome series and bounded-label assertions. | Passed |
| AC4-06 Redis/telemetry outages | Automated outage/recovery and degradation tests. | Passed |
| AC4-07 sensitive-data exclusion | Captured logs/spans/metric labels with synthetic IDs and VIN-like query. | Passed |
| AC4-08 documented startup | README/setup/deploy/test updates. | Passed automated; human inspection Not run |
| T-13 Redis degradation signals | Redis pause test and Compose outage check. | Passed |
| T-15 observability wiring | Delivery, backend queries, log correlation. | Passed |
| S4-V01 telemetry outage and recovery | Automated outage/recovery and Compose stop/start checks. | Passed |
| S4-V02 telemetry data contract | Sensitive-data, label, and outcome assertions. | Passed |
| S4-V03 provisioning and dashboard behavior | Fresh-stack Grafana/PromQL tests. | Passed automated; visual inspection Not run |

## Issues, deviations, and unexecuted checks

- **Operational verification:** Compose startup, provisioning, Redis outage, telemetry outage/recovery, and JSON log correlation checks were executed manually and recorded in [[test/4-test]]. No standalone verification script is required. Human Grafana visual inspection and human execution of the generated [[test/4-manual-test|manual checklist]] remain pending.
- **Review findings R-01-R-07:** fixed and re-verified on 2026-11-02; see [[review/4-review#Re-review and resolution|the review resolution record]] for the corrections and commands.
- **Not run (human):** visual Grafana dashboard inspection, including panel rendering and zero-traffic display. Automated Grafana API and PromQL checks passed; human inspection remains pending. The generated [[test/4-manual-test|ticket 4 manual checklist]] covers this inspection and all cases remain Not run.
- **Not run (literal clean clone):** the implementation is uncommitted in the working tree, so a literal `git clone` was not performed. A fresh Compose project (`scheduler-t4`) with fresh named volumes was used; record the clean-clone limitation rather than claiming it.
- **Deviation:** ASP.NET Core instrumentation filtering excludes health spans, but the framework emits `http.server.request.duration` health datapoints independently of the trace filter. A Collector `filter/health` processor drops datapoints with `http.route` matching `^/health/` before Prometheus; the automated health-exclusion test passes on a fresh stack. This is an implementation detail beyond the original plan wording.
- **Nuance:** an unreachable SQL runtime fails inside service prechecks, so the controller never records `scheduler.booking.outcomes{outcome="dependency_unavailable"}`; the escaped dependency failure is recorded by `scheduler.http.requests{outcome="dependency_unavailable"}`. The booking outcome counter covers controller-returned outcomes. Likewise, a malformed body rejected by `[ApiController]` model validation before the action is represented by `scheduler.http.requests{outcome="validation_error"}`; the automated contract test uses a bindable body with an invalid `startsAt` to exercise the controller-level `validation_error` counter.
- **Nuance:** a capacity conflict is filtered by the availability/overlap precheck and may not reach `booking.allocate`; its trace contains HTTP/SQL spans and the request outcome, while a successful booking trace contains `booking.allocate` and `EXECUTE dbo.CreateAppointment`.
- **Documented meaning:** the transport-level cache hit ratio treats a corrupt cached payload as a `get` hit before fallback; failures are a separate series.
- **Documented limitation:** the 30-second delivery/recovery polling window is a chosen observation value, not a measured guarantee.
- Prior ticket 1-3 reports and manual records were preserved and not rerun as part of ticket 4.
