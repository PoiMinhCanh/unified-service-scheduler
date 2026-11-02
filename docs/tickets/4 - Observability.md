---
title: "4 - Observability"
tags: [ticket, service-scheduler, observability, prometheus, grafana, tracing]
ticket: 4
status: specified
created: 2026-10-28
updated: 2026-11-02
---

# 4 - Observability

## Objective and baseline

Make catalog, availability, and booking requests diagnosable through correlated structured logs, metrics, and distributed traces without making telemetry a requirement for successful booking.

Read [README](../../README.md) and [[architecture]]. Add automated telemetry smoke checks alongside instrumentation.

### Inspected starting point

Tickets 1–3 provide the .NET API, catalog and advisory availability reads, appointment confirmation/retrieval, SQL Server booking procedure, bounded Redis catalog cache, dependency health checks, and SQL/Redis test coverage. Inspect `src/ServiceScheduler.Api/Program.cs`, the feature services, `Infrastructure/Caching/BoundedCache.cs`, `Infrastructure/Errors/SqlDependencyExceptionHandler.cs`, and `Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs` as instrumentation entry points. Existing evidence belongs to [[test/1-test]], [[test/2-test]], and [[test/3-test]]; preparing this ticket does not rerun those checks.

The API already supplies Problem Details trace identifiers and uses `ILogger` for dependency/cache failures. Its project references do not yet include Serilog or OpenTelemetry. Extend the current host and `compose.yaml`; treat the Collector, Prometheus, Tempo, Grafana, dashboards, and telemetry checks as proposed additions until research inspects the checkout. Existing trace identifiers alone do not establish exported traces or log correlation.

### Requirement traceability

Use these ticket-local IDs in research, plans, review, and execution evidence. All support project requirement P-03; existing booking and cache correctness guarantees remain constraints.

| ID | Deliverable |
| --- | --- |
| S4-01 | Structured request/domain/dependency logs with usable trace correlation and stable application identity. |
| S4-02 | HTTP, SQL, availability/allocation, and cache tracing through OTLP and Tempo. |
| S4-03 | Bounded request, booking, and cache metrics exported through the Collector to Prometheus. |
| S4-04 | Pinned local telemetry stack, provisioned Grafana data sources/dashboards, and operational instructions. |
| S4-05 | Sensitive-data protection, bounded telemetry processing, and booking/readiness resilience during telemetry outages. |
| S4-06 | Automated telemetry smoke checks and recorded inspection. |

### Research handoff and open decisions

This specification is ready for research; research, planning, implementation, and ticket 4 verification remain pending. Keep `status: specified` consistent with the other ticket specifications. Follow [[research/README|Research guide]] and [[templates/research-template|Research Protocol]], using one primary entry point: `docs/research/4-research.md` or `docs/research/4/00-overview.md`.

Research must resolve the following with inspected code/configuration and current primary documentation; this ticket does not prescribe unverified package versions or measured results.

- Identify compatible pinned Serilog/OpenTelemetry packages and container versions for the current .NET SDK and `Microsoft.Data.SqlClient`; verify instrumentation of both EF queries and the direct stored-procedure command without duplicate spans or SQL parameter capture.
- Define service name/version/environment resource attributes, W3C trace propagation, request-event middleware placement, and the relationship between Problem Details identifiers, JSON trace/span fields, and Tempo trace IDs. Cover successful, conflict, validation, and dependency-error responses without duplicating existing failure events.
- Define span boundaries, bounded outcome/error vocabularies, metric names/units, histogram buckets, aggregation, and PromQL for p95 latency, booking outcomes, and cache hit ratio. Distinguish internal allocation retries from the final HTTP booking outcome and define which cache operations count as hits, misses, and failures.
- Resolve OTLP protocols/endpoints and the Collector-to-Prometheus OTLP path, receiver flags, translation/resource-label behavior, and Grafana-to-Tempo lookup. Specify sampling and flush/poll bounds so generated smoke-test traces can be found deterministically; document any limits on finding ordinary request traces under sampling.
- Choose bounded queues, export timeouts/retries, sampling, and local retention/resource limits. Specify handling of missing, unavailable, and recovered telemetry backends, including startup and shutdown behavior; the API must start and remain usable without them.

No schema, booking procedure, runtime permission, or cache key/payload/TTL change is intended. Research must assess instrumentation overhead and catalog-cache impact explicitly, preserving cache budgets, SQL fallback, booking retry/timeout limits, and the restricted SQL write boundary. Any required change to these contracts needs an explicit justification and requirement mapping.

## Scope

- Configure Serilog structured JSON console logs with one request event plus useful booking success/conflict and dependency-failure events. Include trace/span IDs, application version, and stable error codes where appropriate.
- Add the OpenTelemetry .NET SDK and OTLP exporter with ASP.NET Core, runtime, and SQL Server instrumentation, including `OpenTelemetry.Instrumentation.SqlClient` for `Microsoft.Data.SqlClient`. Verify compatible package versions and stored-procedure spans.
- Add custom availability/allocation activities and explicit Redis activities if suitable cache instrumentation is not enabled.
- Configure the OpenTelemetry Collector to send metrics to Prometheus with its OTLP receiver enabled and traces to Grafana Tempo.
- Provision Grafana Prometheus/Tempo data sources and committed dashboard JSON. Grafana visualizes data; Tempo stores traces. Console/container output remains the core log access path; Loki is optional and outside this ticket.
- Collect request rate, error rate, latency histograms, booking outcomes, and cache hits/misses/failures. Dashboard panels include p95 latency, booking outcomes, and cache hit ratio with defined zero-traffic behavior.
- Use bounded attributes such as route template, method, status class, and outcome. Do not use raw URLs, appointment IDs, VINs, customer IDs, or trace IDs as metric labels; avoid logging request bodies or personal contact data.
- Use bounded asynchronous telemetry processing/export. Missing telemetry backends must not change booking correctness or make them hard readiness dependencies.
- Add pinned Collector, Prometheus, Tempo, and Grafana containers/configuration to the local Compose setup. Document startup, access locations, retention/resource settings, and troubleshooting.

## Acceptance criteria

- [ ] A generated booking request can be correlated between structured logs and its Tempo trace through Grafana.
- [ ] Availability and booking-conflict traces include the relevant SQL/custom spans; cache activity is visible.
- [ ] Requests reach Prometheus and Tempo through the Collector, verified by automated smoke checks and recorded manual inspection.
- [ ] Provisioned Grafana dashboards work without manual data-source or dashboard creation and handle zero traffic predictably.
- [ ] Metrics distinguish successful bookings, capacity conflicts, dependency errors, and cache hits/misses/failures using bounded labels.
- [ ] Redis outage shows degradation signals while catalog fallback and booking stay correct; telemetry backend outages do not prevent booking or fail readiness solely due to telemetry.
- [ ] Tests/checks confirm sensitive data and unbounded identifiers are excluded from metric labels and logged payloads.
- [ ] Documented startup instructions, architecture and README reflect actual observations.

For correlation, use a generated synthetic request with known W3C trace context and verify matching JSON log fields and a queryable Tempo trace. Prometheus metrics remain aggregate signals: correlate them by workload time window and bounded dimensions, without introducing trace IDs as labels. Exemplars are optional, subject to research and backend support.

For zero traffic, document whether each dashboard panel shows zero or no data; avoid misleading booking/cache ratios and undefined divisions. Verify cold cache, warm cache, Redis failure, booking success, capacity conflict, and dependency failure separately. Outage checks must assert HTTP results and persisted booking state as well as emitted signals, then verify telemetry resumes after backend recovery within a documented observation window.

## Verification and scope boundaries

Owns P-03 and the telemetry checks below. Preserve existing business, cache, persistence, and concurrency tests. Record automated versus manual results and unexecuted checks separately in `docs/test/4-test.md` using [[test/README|Testing guidance]].

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-13 Redis degradation signals | Automated smoke + inspection | Controlled Redis outage produces correlated cache-failure logs/metrics while catalog fallback and booking remain correct. | P-03 |
| T-15 Observability wiring | Automated smoke + inspection | Requests reach provisioned metric and trace backends, and logs contain matching trace identifiers. | P-03 |
| S4-V01 Telemetry outage and recovery | Automated operational | API startup, booking, and readiness remain correct with telemetry backends unavailable; bounded export resumes after recovery. | S4-05 |
| S4-V02 Telemetry data contract | Automated | Captured logs, spans, and exported metric attributes exclude sensitive payloads, raw request paths/query strings, and unbounded identifiers; outcome counters and correlation fields match generated scenarios. | S4-01, S4-02, S4-03, S4-05 |
| S4-V03 Provisioning and dashboard behavior | Automated smoke + inspection | Fresh local startup provisions data sources/dashboards; panels show expected workload signals and documented zero-traffic behavior. | S4-03, S4-04 |

Smoke checks must query the actual Prometheus and Tempo APIs with bounded polling and fail with useful diagnostics when expected data is absent; exporter registration or mocked sinks alone do not establish end-to-end delivery. Define prerequisites, synthetic fixtures, deterministic conflict generation, expected signals, outage restoration, and cleanup in the plan. Keep automated evidence separate from human dashboard/trace inspection and leave unexecuted checks marked Not run or Blocked with a reason.

Implementation updates [[architecture]], [README](../../README.md), [[setup-guide]], and `docs/test/4-test.md`, and adds `docs/deploy-notes/4-deploy-notes.md` for telemetry startup, access locations, configuration, retention, recovery, and troubleshooting. Preserve existing test reports and manual execution records.

Central log storage, alerting, production hosting are outside this ticket's scope.
