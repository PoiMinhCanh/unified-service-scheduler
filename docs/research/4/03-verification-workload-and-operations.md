---
title: "4 - Observability: Verification and Operations"
tags: [research, service-scheduler, observability, testing, deployment]
ticket: 4
status: verified
updated: 2026-11-02
---

# 4 - Verification and operations

- Ticket: [[4 - Observability|Ticket 4]]
- Research overview: [[research/4/00-overview|Ticket 4 research overview]]
- Requirements: S4-05, S4-06; AC4-03, AC4-04, AC4-06, AC4-07, AC4-08; T-13, T-15, S4-V01-S4-V03.
- Evidence baseline: HEAD `92a81efde5dac1070669fff13193e452dc9b6529` with a clean working tree; inspected existing unit/integration test projects, fixtures, test reports, Compose, and documentation. No telemetry test, test report, or deployment note exists.

> [!IMPORTANT]
> Every scenario in this part is planned. No telemetry test, smoke check, dashboard inspection, outage check, or manual correlation has been executed for ticket 4. Research-phase container probes in Part 2 are configuration evidence only. Statuses remain **Not run** until implementation records actual commands and observed results in `docs/test/4-test.md`.

## Existing test infrastructure

| Component | Inspected evidence | Reuse / gap |
| --- | --- | --- |
| Unit project | `tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj`; xUnit v3, `Microsoft.Extensions.TimeProvider.Testing`, API project reference. | Extend with telemetry options/enricher/middleware/listener tests. |
| Integration project | `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj`; WebApplicationFactory, SQL Server/Redis Testcontainers 4.14.0, fixed clock. | Leave existing suites independent; add a telemetry project/collection for real-backend checks. |
| API factory | `SchedulerApiFactory` supports in-memory settings, a `TimeProvider` override, and `ConfigureTestServices`. | Reuse for telemetry-enabled API hosts; set `Scheduler:Telemetry:*` overrides. |
| SQL/Redis fixtures | `SqlServerContainerFixture`, `SqlServerTestBase`, `CacheSqlServerTestBase`, `RedisContainerFixture`; database-per-class, Redis DB index, pause/unpause. | Reuse for booking/cache correctness during telemetry/Redis outage tests. |
| Logger capture | `CapturingLoggerProvider` receives `ILogger` events from test-added providers. | Verify Serilog remains an additional provider; use a Serilog test sink for JSON/field assertions. |
| Fixed clock/data | `FixedClockUtc` 2026-11-02T00:00Z; seeded Saigon/London IDs, one active technician/bay per dealership, weekday 09:00-17:00 shifts. | Generate future weekday starts within the shift horizon; use seeded IDs for smoke checks. |
| Existing reports | [[test/1-test]], [[test/2-test]], [[test/3-test]]; ticket 4 preparation does not rerun them. | Preserve prior records; ticket 4 evidence belongs in `docs/test/4-test.md`. |

## Test architecture

### Unit-level contracts

| Area | Proposed test approach | Evidence |
| --- | --- | --- |
| Telemetry options | Validate disabled defaults; invalid endpoint/ratio/timeouts/queue/batch values fail; valid enabled values pass. | Options validator tests. |
| Serilog enrichment | Create a Serilog logger with `ActivityLogEnricher` and a test `ILogEventSink`; assert `TraceId`, `SpanId`, `TraceFlags`, application identity fields, and JSON-serializable properties. | Log contract test. |
| Request middleware | Invoke middleware with a fake endpoint/route template, status, Problem-code item, and fake logger; assert one event with route/method/status class/outcome/elapsed and no raw path/query/body/IDs. | Middleware test. |
| Custom spans/metrics | Use `ActivityListener` and `MeterListener` with the `ServiceScheduler` source/meter; assert availability/allocation/cache tags and counters; assert no keys/IDs. | Listener tests. |
| Problem Details trace ID | Start a test `ActivitySource` activity; assert `traceId` is 32 lowercase hex and matches the activity; assert fallback when no activity; assert OpenAPI description. | API/contract tests. |
| Cache telemetry | Drive `BoundedCache` with fake cache hits/misses/exceptions/timeouts; assert cache counter/spans, no key, fallback unchanged, cancellation not counted as failure. | Unit tests with `FakeDistributedCache` and listeners. |

### End-to-end telemetry smoke

Use a dedicated telemetry test project/collection, for example `tests/ServiceScheduler.TelemetryTests/`, so the existing SQL/Redis integration suite keeps its runtime and isolation. The plan may instead add a telemetry collection to the existing project if it can preserve isolation; the research recommendation is a separate project.

| Fixture concern | Proposed design |
| --- | --- |
| Containers | Start pinned Prometheus, Tempo, Grafana, and Collector on a dedicated Docker network with network aliases `prometheus`, `tempo`, `grafana`, `collector`; mount the same `telemetry/` config files used by Compose. |
| Ports | Expose Prometheus 9090, Grafana 3000, Tempo 3200, and Collector 4317/13133 to the test host; use dynamically mapped host ports and build the API OTLP endpoint from the Collector's mapped gRPC port. |
| API host | `SchedulerApiFactory` with `Scheduler:Telemetry:Enabled=true`, `OtlpEndpoint=http://<host>:<collector-grpc-port>`, `SamplingRatio=1.0`, short batch/metric intervals for deterministic export, and the real SQL/Redis runtime connections. |
| Configuration files | Locate the repository `telemetry/` directory from the test working directory or copy config files to test output; fail with a clear message if a file is missing. |
| Readiness | Poll Collector `:13133`, Prometheus `/-/ready`, Tempo `:3200/ready`, and Grafana `/api/health` with bounded timeouts before generating load. |
| Correlation | Send requests with a known `traceparent: 00-<32hex>-<16hex>-01`; use that trace ID for Tempo lookup and assert the response/Problem Details trace ID and captured log trace field. |
| Cleanup | Dispose API hosts/containers/network; use ephemeral container storage; delete test appointments with the setup identity; flush only test Redis data. |

Prometheus/Tempo/Grafana polling helpers must use bounded deadlines (proposed 30 s for delivery, adjusted only with recorded rationale) and fail with diagnostics that include the query/endpoint, elapsed time, last HTTP status/body, and the expected series/trace. Do not treat exporter registration, a mocked sink, or a successful container start as delivery evidence.

### Operational checks

The ticket requires automated operational evidence beyond in-process tests. Use the real Compose stack:

- Start the stack with telemetry services available; generate catalog/availability/booking/conflict requests; query Prometheus and Tempo; query Grafana dashboard/data-source APIs; capture `docker compose logs --no-color --no-log-prefix api` JSON.
- Start the API while telemetry services are absent/unreachable; verify API start, booking, and readiness; start telemetry services; generate new requests; verify new exports within a documented window.
- Stop Redis; verify cache-failure logs/metrics, SQL fallback, booking/retrieval correctness, and readiness `Degraded`/`200`; restart Redis; verify recovery.
- Capture the commands, environment, revision, and observed results in `docs/test/4-test.md`.

Manual Grafana inspection remains a human check; it does not replace automated Prometheus/Tempo/Grafana API checks.

## Scenario matrix

| ID | Requirement IDs | Level | Setup / action | Expected result | Status |
| --- | --- | --- | --- | --- | --- |
| TC4-01 | S4-01, S4-05 | Unit | Validate telemetry options with disabled defaults, invalid endpoint/ratio/timeouts, valid enabled values. | Disabled defaults pass; invalid values fail clearly; no network required. | Not run |
| TC4-02 | S4-01, AC4-07 | Unit | Serilog logger with test sink; emit request/domain events with trace activity. | JSON properties include identity, `TraceId`/`SpanId`; no raw path/query/body/VIN/customer ID. | Not run |
| TC4-03 | S4-01, S4-02 | Unit/API | Request middleware with known route/status/Problem code; trace activity present and absent. | One completion event with route template/method/status class/outcome; trace ID 32-hex when active; documented fallback otherwise. | Not run |
| TC4-04 | S4-02, S4-03 | Unit | `ActivityListener`/`MeterListener`; evaluate availability, allocate success/conflict, cache hit/miss/failure/cancel. | Custom span names/outcomes and metric labels match the contract; no IDs/keys; cancellation not a failure. | Not run |
| TC4-05 | S4-02, S4-03, AC4-03 | Automated smoke | Start real Collector/Prometheus/Tempo with API enabled; generate requests with known traceparent. | All requests reach Prometheus/Tempo through Collector; queryable series/trace exists within bounded polling. | Not run |
| TC4-06 | S4-02, AC4-02 | Automated smoke | Generate availability, booking-conflict, and catalog cache miss/hit requests; inspect their Tempo traces. | Availability/booking traces contain HTTP, EF SQL, `availability.evaluate`, and (for booking) `dbo.CreateAppointment` SQL plus `booking.allocate`; catalog traces contain `cache.get`/`cache.set` hit/miss spans and attributes. | Not run |
| TC4-07 | S4-03, S4-05, AC4-05, AC4-07 | Automated smoke | Generate success, conflict, dependency-error, and cache scenarios; inspect Prometheus labels/Tempo attributes/log JSON. | Booking success/conflict/dependency outcomes and cache hit/miss/failure are distinguishable; no forbidden labels/attributes/payloads. | Not run |
| TC4-08 | S4-05, AC4-06, AC4-07 | Automated smoke | Send synthetic customer/vehicle/service IDs, VIN, query strings, and route IDs; inspect logs/spans/metrics. | Values do not appear in logs/spans/metric labels; only bounded route/outcome/label vocabularies remain. | Not run |
| TC4-09 | S4-04, AC4-04, S4-V03 | Automated smoke + inspection | Fresh telemetry stack with real workload; query Grafana health/dashboard/datasources/panel queries. | Data sources/dashboard provisioned without manual creation; p95/booking outcomes/cache hit ratio panels return expected signals. | Not run |
| TC4-10 | S4-03, S4-04, AC4-04 | Automated smoke | Query dashboard panels before any workload on a fresh Prometheus/Grafana stack. | Zero-traffic behavior matches documentation (`0`/`0%`/`No data`); no undefined division or error. | Not run |
| TC4-11 | S4-05, S4-V01, AC4-06 | Automated operational | Start API with telemetry services absent/unreachable; perform catalog/availability/booking; check readiness and persisted state. | API starts; requests/booking succeed; the appointment is retrievable and present in SQL; readiness reflects SQL/Redis only; no telemetry gate. | Not run |
| TC4-12 | S4-05, S4-V01 | Automated operational | Start telemetry services after the API; generate new requests; poll backends. | New traces/metrics export within the documented observation window (proposed 30 s); no backfill claim for dropped telemetry. | Not run |
| TC4-13 | S4-05, T-13, AC4-06 | Automated smoke + inspection | Stop/pause Redis; request catalog and book; inspect logs/metrics/Tempo; assert persisted row; restart Redis. | Cache failure logs/metrics and cache activity visible; SQL fallback and booking/retrieval correct; persisted appointment present; readiness Degraded/200; recovery observed. | Not run |
| TC4-14 | S4-01, S4-02, T-15, AC4-01 | Automated + manual | Generated booking with known traceparent; inspect JSON logs and Tempo trace through Grafana. | Same trace ID in log, Problem Details, and Tempo; log route template and span IDs allow correlation. | Not run |
| TC4-18 | S4-04, S4-06, AC4-08 | Operational/manual | Fresh Compose startup, access checks, dashboard/log inspection, troubleshooting instructions. | Documented startup/access/retention/recovery instructions match observed behavior; unexecuted human steps remain Not run. | Not run |
| TC4-19 | S4-03, S4-05, AC4-05, AC4-06 | Automated smoke + inspection | Use an unreachable runtime SQL endpoint or isolated SQL outage; request catalog/availability/booking; inspect metrics/logs/Tempo; restore SQL. | Dependency error is `503 dependency_unavailable`, not a capacity conflict; booking outcome/procedure attempt signals reflect dependency; readiness is `503` only because SQL is down; no unexpected appointment is stored. | Not run |

## Documentation and deployment notes plan

| Record | Required content |
| --- | --- |
| `docs/test/4-test.md` | Requirement coverage, actual commands, revision, environment, dependency/image versions, observed results, evidence, unexecuted/Blocked checks. |
| `docs/test/4-manual-test.md` | Generated in the orchestrator `test` phase from the implemented routes/commands; new cases remain Not run/Blocked; human Grafana/log inspection remains manual. |
| `docs/deploy-notes/4-deploy-notes.md` | Telemetry startup order, access locations, `Scheduler:Telemetry` configuration, Prometheus/Tempo retention, resource limits, outage behavior, recovery, troubleshooting, and local-only access limitations. |
| `README.md` | Status (implemented/planned), telemetry access commands, and links to setup/deploy/test records. |
| `docs/architecture.md` | Implemented observability components, actual span/metric/log contracts, OTLP paths, sampling, outage behavior, and retention; keep booking/cache guarantees visible. |
| `docs/setup-guide.md` | Telemetry prerequisites, ports, access, configuration, startup/outage/recovery. |
| `docs/README.md` | Ticket 4 research/plan/test/deploy status and links. |
| `docs/database/cache.md` | Ticket 4 cache impact: no key/payload/TTL/invalidation change; instrumentation only; budgets/fallback preserved. |
| `docs/database/overview.md`, `schema.md`, `migration.md` | Record **Database impact: none** with the reason; no migration/schema/procedure/permission change. |

Do not present planned commands or probes as executed evidence. Preserve [[test/1-test|ticket 1]], [[test/2-test|ticket 2]], and [[test/3-test|ticket 3]] records.

## Research-phase evidence limitations

- Container probes validated configuration shapes/startup but did not send application telemetry through the full pipeline.
- No telemetry test, dashboard inspection, outage/recovery check, or manual correlation has run.
- Existing ticket 1-3 results are not rerun and do not prove ticket 4 behavior.
- Grafana anonymous Viewer access was probed on Grafana 13.3.0; if a later implementation changes Grafana/auth, recheck dashboard/panel access.

## Code audit for this concern

| Classification | File / component | Current behavior / gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | Unit test project | No telemetry tests. | Add options/enricher/middleware/listener tests. | S4-01-S4-03, S4-05 |
| Proposed Addition | Telemetry test project/collection and fixtures | Missing. | Add real-backend smoke, contract, outage/recovery, Grafana checks. | S4-05, S4-06, S4-V01-S4-V03 |
| Proposed Addition | Operational verification tests | Missing. | Automate Compose startup, requests, backend queries, JSON log capture, outage/recovery. | S4-06, S4-V01, S4-V02 |
| Proposed Addition | `docs/test/4-test.md`, deploy notes | Missing. | Record executed evidence and operational instructions. | S4-04, S4-06, AC4-08 |
| No Change | Existing SQL/Redis test suites | Existing correctness coverage. | Keep independent; do not use ticket 4 tests as a substitute for booking/cache verification. | P-01, P-02 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| V4-01 | Decision | How to test real delivery without mocks? | Testcontainers Collector/Prometheus/Tempo/Grafana plus actual API requests; query backend APIs with bounded polling. | Resolved; implementation pending. |
| V4-02 | Decision | How to test logs without a production test hook? | Unit Serilog test sink plus operational `docker compose logs` JSON capture; no production-only sink. | Resolved. |
| V4-03 | Decision | How to test telemetry outage/recovery? | Start API with unreachable endpoint; verify requests/readiness; start Collector on the endpoint; generate new requests and poll. | Resolved; implementation detail to verify. |
| V4-04 | Risk | Four telemetry containers increase test time/resources. | Separate telemetry project/collection; pinned images; resource limits; keep existing suites independent. | Resolved with documented cost. |
| V4-05 | Risk | Dashboard/panel queries may be brittle. | Use dashboard UID and PromQL contract; assert expected series/values, not pixel rendering; manual inspection for visual behavior. | Resolved. |
| V4-08 | Risk | No Docker in some environments. | Mark telemetry/operational checks Blocked, not Passed; continue independent unit/contract checks. | Resolved. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S4-01, S4-02, AC4-01, AC4-02 | TC4-02-TC4-06, TC4-14; log/trace correlation and span coverage. | Unit + automated smoke + manual |
| S4-03, S4-04, AC4-03, AC4-04 | TC4-05, TC4-07, TC4-09, TC4-10; delivery, labels, provisioning, zero traffic. | Automated smoke + inspection |
| S4-05, AC4-06, AC4-07 | TC4-07, TC4-08, TC4-11-TC4-13; sensitive-data/outage/Redis degradation. | Unit + automated smoke + operational |
| S4-06, AC4-08 | TC4-18; operational checks and documentation. | Operational/manual |

All checks are **Not run**. The next phase after research verification is planning under [[plans/README|the plans guide]]; detailed test files, commands, and phase exits belong there and in `docs/test/4-test.md` during implementation.
