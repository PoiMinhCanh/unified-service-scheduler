---
title: "4 - Observability: Phase 4 - Automated Telemetry Verification"
tags: [plans, service-scheduler, observability, testing, smoke, prometheus, tempo, grafana]
ticket: 4
status: implemented
updated: 2026-11-02
---

# Phase 4 - Automated telemetry verification

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/03-verification-workload-and-operations|Verification and operations]].
- Plan overview: [[plans/4/README|Ticket 4 implementation plan]].
- Requirements: S4-01 through S4-06; AC4-01 through AC4-07; TC4-05 through TC4-14 and TC4-19; T-15; S4-V01, S4-V02, and S4-V03; project P-01/P-02/P-03 constraints.
- Dependencies: Phase 3 pinned telemetry configuration/Compose files and Phase 2 instrumentation.

## Scope and intended behavior

This phase adds automated end-to-end telemetry tests that start the real pinned Collector, Prometheus, Tempo, and Grafana through Testcontainers on a dedicated Docker network, plus real SQL Server and Redis through the existing collection fixtures. The tests generate synthetic catalog, availability, booking, conflict, Redis-outage, telemetry-outage, and SQL-dependency traffic and query the real backend APIs with bounded polling.

The phase proves delivery and data contracts, not just exporter registration: requests must reach Prometheus/Tempo through the Collector; spans must contain the expected HTTP/SQL/custom/cache activity; metrics must use bounded labels and distinguish outcomes; logs/spans/metrics must exclude sensitive values; Grafana must be provisioned; and telemetry backends must not gate booking/readiness.

Exclusions: this phase does not run the Compose-level operational outage/recovery checks (Phase 5), does not generate the human manual checklist, and does not change application behavior.

## Test architecture

### Placement and isolation

- Add the telemetry tests under `tests/ServiceScheduler.IntegrationTests/Telemetry/` rather than a separate project. This reuses `SchedulerApiFactory`, `SqlServerContainerFixture`, `SqlServerTestBase`, `CacheSqlServerTestBase`, `RedisContainerFixture`, `SchedulingTestData`, and the existing non-parallel collection model without duplicating or relocating fixtures.
- `TelemetryStackFixture` is a class-level fixture (`IClassFixture<TelemetryStackFixture>`) that starts the four pinned telemetry containers on a dedicated Docker network with aliases `collector`, `prometheus`, `tempo`, and `grafana`, mounts the repository `telemetry/` configuration, exposes dynamic host ports, and polls readiness with bounded deadlines.
- `TelemetryDeliveryTests` derives from `CacheSqlServerTestBase` and is in `SqlServerAndRedisCollection`; it uses the collection's real SQL/Redis fixtures plus the telemetry stack. `TelemetryProvisioningTests` uses its own telemetry stack fixture and no SQL/Redis, so zero-traffic/provisioning checks run on a fresh stack.
- Tests locate the repository root by walking up from `AppContext.BaseDirectory` until `telemetry/` and the solution file are found; a missing file fails with a clear message. Testcontainers networks/containers are disposed after the class; no persistent volumes are used.
- The telemetry classes run with `Scheduler:Telemetry:Enabled=true`, `OtlpEndpoint=http://127.0.0.1:<collector-grpc-port>`, `SamplingRatio=1.0`, and short batch/metric intervals for deterministic export. Requests use a known `traceparent: 00-<32hex>-<16hex>-01`.

### Fixture responsibilities

| Fixture/helper | Responsibility |
| --- | --- |
| `TelemetryStackFixture` | Start/stop/dispose Collector/Prometheus/Tempo/Grafana containers; expose mapped ports and readiness probes; stop/start the Collector for outage/recovery tests; resolve repository config paths. |
| `TelemetryTestQueries` | Bounded polling for Prometheus query results, Tempo trace-by-ID, Grafana health/data-source/dashboard APIs, and Collector health; fail with endpoint/query/elapsed/status/expected-signal diagnostics. |
| `TelemetryDeliveryTests` | Delivery, span coverage, metric labels/outcomes, sensitive-data contract, Redis degradation, telemetry outage/recovery, SQL dependency, and log/trace correlation. |
| `TelemetryProvisioningTests` | Fresh-stack Grafana provisioning and zero-traffic PromQL/dashboard behavior. |

### Required scenarios

| Scenario | Setup/action | Expected evidence |
| --- | --- | --- |
| Delivery (TC4-05, T-15) | Generate catalog, availability, booking-success, and booking-conflict requests with known trace IDs; poll Prometheus/Tempo. | Expected `scheduler_*`/`http_server_request_duration_seconds_*` series exist; each known trace is queryable in Tempo; the Collector received the data. |
| Span coverage (TC4-06) | Query the catalog-miss, catalog-hit, availability, booking-success, and booking-conflict traces. | HTTP server spans; SQL spans for EF reads and `dbo.CreateAppointment`; `availability.evaluate`; `booking.allocate`; `cache.get`/`cache.set` with bounded attributes; no `url.path`/`url.query`/SQL parameters. |
| Metric contract (TC4-07, AC4-05) | Generate success, validation-failure, capacity-conflict, dependency-error, cache hit/miss/failure cases; inspect Prometheus labels and Tempo attributes. | Booking outcomes distinguish created/validation/capacity/dependency; cache operations distinguish hit/miss/failure; labels are limited to the declared bounded vocabularies; no forbidden labels. |
| Sensitive data (TC4-08, AC4-07) | Send synthetic customer/vehicle/service IDs, VIN-like values, query strings, and path IDs; inspect captured logs, spans, and metric attributes. | Synthetic values do not appear in telemetry; only route templates, status/outcome vocabularies, and bounded labels remain. |
| Provisioning (TC4-09, S4-V03) | Query Grafana health, data sources, dashboard UID, and panel queries on a fresh stack. | Data sources/dashboard exist without manual creation; panels use the expected PromQL; expected workload signals are visible. |
| Zero traffic (TC4-10) | Query dashboard PromQL before any workload on a fresh stack. | Counter panels show `0`/no data; latency panels show `No data`; ratios show `0%` without NaN/error. |
| Telemetry outage (TC4-11, S4-V01) | Stop/unreachable Collector; start API; perform catalog/availability/booking; check readiness and persisted state; restart Collector; generate new traffic. | API starts; reads/booking succeed and the appointment is retrievable/persisted; readiness reflects SQL/Redis only; new exports appear within the documented window after recovery; no backfill claim. |
| Redis degradation (TC4-13, T-13) | Pause/stop Redis with telemetry enabled; request catalog and book; inspect logs/metrics/Tempo; assert persisted row; restart Redis. | Cache-failure log/metric/cache activity visible; SQL fallback and booking/retrieval correct; appointment persisted; readiness `Degraded`/`200`; recovery observed. |
| SQL dependency (TC4-19, AC4-05/AC4-06) | Use an unreachable runtime SQL endpoint with telemetry enabled; request catalog/availability/booking; inspect metrics/logs/Tempo; verify no unintended row. | Responses are `503 dependency_unavailable`, not capacity conflicts; request metric/outcome reflects dependency; readiness `503` because SQL is down; no appointment is stored. |
| Correlation (TC4-14, AC4-01) | Generate a booking with a known `traceparent`; compare the Problem Details trace ID, captured request-event fields, and the Tempo trace; query the trace directly and through the Grafana Tempo data-source proxy; Phase 5 repeats the check with Compose JSON logs. | Same 32-hex trace ID appears in the API response and Tempo, including through Grafana's Tempo proxy; request-event route/status fields permit correlation; Phase 5 verifies the Serilog `TraceId` in container JSON logs; metrics remain aggregate. |

### Diagnostics and cleanup

- Every backend poll uses a bounded deadline (default 30 s, recorded with rationale if changed) and fails with the endpoint/query, elapsed time, last HTTP status/body, and expected series/trace.
- Tests delete only their own appointments through the setup identity, flush only the test Redis database index, restore paused Redis, restart the Collector, and dispose containers/network.
- A missing Docker daemon or unpullable image marks the check Blocked with the actual error; it is never reported as Passed.
- Tests do not use mocked sinks as delivery evidence; unit listener tests remain complementary.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 4.1 | S4-06 | `Directory.Packages.props`, `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj`; new `Telemetry/TelemetryStackFixture.cs`, `TelemetryTestQueries.cs` | Add the Testcontainers 4.14.0 base package and implement the four-container fixture, dynamic ports/network aliases, config mounts, readiness polling, Collector stop/start, disposal, and bounded query helpers with diagnostics. | Fixture starts/stops/disposes the pinned stack; readiness probes pass; query helpers time out with useful diagnostics; no persistent volumes. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 4.2 | S4-01, S4-02, S4-03, S4-05 | New `Telemetry/TelemetryDeliveryTests.cs`; modify `Fixtures/CapturingLoggerProvider.cs` to capture structured state | Generate synthetic requests with known trace IDs; assert delivery, span coverage, metric names/labels/outcomes, sensitive-data exclusion, SQL dependency, correlation, and the Grafana Tempo proxy trace lookup. | All delivery/contract scenarios pass against real backends; failures include the missing series/trace and diagnostics. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 4.3 | S4-03, S4-04, S4-05 | New `Telemetry/TelemetryProvisioningTests.cs` | Start a fresh telemetry stack; query Grafana health/data sources/dashboard; evaluate zero-traffic PromQL and dashboard panel behavior. | Provisioning and zero-traffic scenarios pass with no manual Grafana setup. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 4.4 | S4-05 | `TelemetryDeliveryTests` outage/Redis sections | Stop/start the Collector; pause/unpause Redis; use an unreachable SQL runtime endpoint; assert booking/readiness/persisted state and recovery. | Outage/recovery and Redis degradation checks pass; telemetry backends are not readiness dependencies; no unintended writes occur. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 4.5 | S4-06 | `docs/test/4-test.md` | Record actual telemetry-test commands, environment/image references, observed results, evidence links, and Blocked/unexecuted checks. | Executed evidence is recorded with revision/environment; failures/Blocked checks remain visible; no acceptance criterion is marked Passed solely from a checklist. | Implemented 2026-11-02 (see [[test/4-test]]) |

## Verification

| Case | Command / scenario | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| TC4-05, T-15 delivery | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TelemetryDelivery"` | Requests reach Prometheus and Tempo through the Collector; expected series/traces exist within bounded polling. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-06 span coverage | Same telemetry delivery filter/assertions | Catalog/availability/booking/conflict traces contain HTTP, SQL, `availability.evaluate`, `booking.allocate`, and cache spans with no forbidden attributes. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-07, TC4-08, S4-V02 metric/data contract | Same telemetry delivery filter; synthetic-ID scenario | Outcomes/labels are bounded and distinguishable; captured logs/spans/metric attributes exclude sensitive payloads, raw paths/query strings, and unbounded identifiers; correlation fields match the generated scenarios. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-09, TC4-10 provisioning/zero traffic | `dotnet test ... --filter "FullyQualifiedName~TelemetryProvisioning"` | Fresh Grafana provisioning and zero-traffic behavior match the dashboard contract. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-11-TC4-13, TC4-19 outage/Redis/SQL | Telemetry delivery outage sections | API starts/books/reads with telemetry unavailable; Redis fallback/booking correct; SQL dependency returns 503 and no false capacity; recovery exports new telemetry. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-14 correlation | Telemetry delivery correlation scenario plus Phase 5 Compose JSON log capture | The known trace ID matches the response and the Tempo trace, including through the Grafana Tempo data-source proxy; request-event route/status fields permit correlation; the Serilog `TraceId` in container JSON logs is verified in Phase 5. | Not run (plan-time record; see [[test/4-test]]) |
| Full affected regression | `dotnet test tests/ServiceScheduler.UnitTests/... --no-build`; `dotnet test tests/ServiceScheduler.IntegrationTests/... --no-build` | Existing unit/integration suites plus telemetry tests pass, or failures/Blocked checks are documented. | Not run (plan-time record; see [[test/4-test]]) |

## Exit criteria

- The telemetry stack fixture starts and disposes the pinned Collector/Prometheus/Tempo/Grafana stack with bounded readiness/query polling and useful diagnostics.
- All required delivery, span, metric, sensitive-data, provisioning, zero-traffic, outage/recovery, Redis, SQL-dependency, and correlation scenarios pass against real backends and real SQL Server/Redis.
- Telemetry outage does not change booking correctness or readiness; Redis fallback/booking remains correct with degradation signals; no unintended writes occur.
- `docs/test/4-test.md` records actual commands, environment, image references, observed results, and any Blocked/unexecuted checks.
- No manual dashboard execution or final ticket acceptance is claimed from this phase.

## Impacts and remaining decisions

- Database/migrations: none; tests use existing seeded/probe data and clean up only their own rows.
- Cache: no key/payload/TTL change; tests pause/unpause or use a test database index and restore it.
- Booking: no behavior change; tests assert existing correctness under telemetry/Redis/SQL outages.
- Test infrastructure: new Testcontainers base package and telemetry fixtures; no change to production dependency boundaries.
- Remaining decision: Testcontainers custom-container API details and dynamic network aliases must be verified against version 4.14.0 during implementation; the fixture design may adjust without changing the required scenarios.
- Remaining decision: Tempo/Prometheus response formats and metric translation names must be asserted from observed responses; dashboards and queries may be adjusted in Phase 3/4 if translation differs.
- Remaining decision: in-process log capture sees application `ILogger` state, not Serilog enricher properties; the Serilog `TraceId`/`SpanId` contract is covered by unit enricher tests and the Phase 5 Compose JSON log capture, while this phase asserts Problem Details/Tempo correlation and structured request-event fields.
