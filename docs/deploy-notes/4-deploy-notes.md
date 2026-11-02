---
title: "4 - Observability: Deployment Notes"
tags: [deploy-notes, service-scheduler, observability, telemetry, prometheus, grafana, tracing]
ticket: 4
status: implemented-local
updated: 2026-11-02
---

# Ticket 4 - Deployment notes

## Scope and references

- Ticket: [[4 - Observability|Ticket 4]]
- Change summary: Serilog JSON console logging, conditional OpenTelemetry traces/metrics over OTLP, request/metric correlation, a pinned local Collector/Prometheus/Tempo/Grafana stack, provisioned Grafana data sources/dashboard, and automated telemetry checks.
- Implementation revision: checkout HEAD `92a81efde5dac1070669fff13193e452dc9b6529` plus the uncommitted ticket 4 working-tree change.
- Related records: [[plans/4/README|Implementation plan]], [[test/4-test|Executed test report]], [[test/4-manual-test|Manual tests]], [[architecture#Observability and failure behavior|Architecture observability]], [[database/cache#Ticket 4 cache impact|Cache impact]].
- Database impact: **none**. No migration, schema, procedure, grant, seed, cache key, payload, TTL, or invalidation change.
- Target environment: local Docker Compose demo only. No production hosting, centralized logging, alerting, or authentication for telemetry UIs is included.

## Prerequisites and deployment order

- Docker with Linux containers and Compose v2, .NET SDK `10.0.403` for host commands, and the local `.env` with SQL/Redis credentials.
- Required images: Collector `otel/opentelemetry-collector:0.164.0`, Prometheus `prom/prometheus:v3.15.1`, Tempo `grafana/tempo:3.1.1`, Grafana `grafana/grafana:13.3.0`; the manifest-list digests are pinned in `compose.yaml` and the research record.
- No new secret is required. Grafana uses anonymous Viewer access bound to loopback only.
- Startup order: SQL Server and Redis start; `db-setup` applies migrations/grants/seeds; the API starts after setup; Prometheus, Tempo, Grafana, and the Collector start independently. The API does not depend on any telemetry service.
- Configuration source: `appsettings.json` supplies disabled-by-default `Scheduler:Telemetry` values; `compose.yaml` explicitly enables telemetry for the API and sets `Scheduler__Telemetry__OtlpEndpoint=http://collector:4317` and `SamplingRatio=1.0`.
- Host runs must export `Scheduler__Telemetry__Enabled=true` and `Scheduler__Telemetry__OtlpEndpoint=http://127.0.0.1:4317`; otherwise telemetry remains disabled while Serilog JSON logging stays active.

## Configuration

| Key | Default / Compose value | Purpose |
| --- | --- | --- |
| `Scheduler:Telemetry:Enabled` | `false`; Compose `true` | Creates OTel providers/exporters only when enabled. |
| `Scheduler:Telemetry:OtlpEndpoint` | `http://127.0.0.1:4317`; Compose `http://collector:4317` | OTLP/gRPC endpoint. Must be an absolute HTTP(S) URI when enabled. |
| `Scheduler:Telemetry:SamplingRatio` | `1.0` | Parent-based trace sampling ratio; lower values intentionally omit traces. |
| `Scheduler:Telemetry:ServiceName` / `ServiceInstanceId` | `unified-service-scheduler-api` / `scheduler-api` | OTLP resource identity and Prometheus `job`/`instance`. |
| `Scheduler:Telemetry:ServiceVersion` | assembly informational version | OTLP `service.version`. |
| Trace batch bounds | delay 1000 ms, export timeout 5000 ms, queue 2048, batch 512 | Bounded asynchronous trace export. |
| `OtlpTimeoutMilliseconds` | 3000 | Exporter network timeout. |
| Metric export interval / timeout | 5000 ms / 3000 ms | Bounded periodic metric export. |
| `Logging:LogLevel` | Default Information; ASP.NET Core/EF Core/Redis Warning | Framework noise reduction; Serilog also applies these overrides. |

## Access and ports

| Service | Local URL / endpoint | Notes |
| --- | --- | --- |
| API | `http://127.0.0.1:8080` | Existing API/Swagger UI. |
| Grafana | `http://127.0.0.1:3000` | Anonymous Viewer; dashboard UID `scheduler-overview` at `/d/scheduler-overview`. |
| Prometheus | `http://127.0.0.1:9090` | OTLP receiver enabled with `--web.enable-otlp-receiver`. |
| Tempo | `http://127.0.0.1:3200` | Trace lookup `GET /api/v2/traces/{traceId}`; search `/api/search`. |
| Collector | `http://127.0.0.1:13133` | Health-check extension; OTLP gRPC `4317`, HTTP `4318`. |

- All telemetry ports bind to `127.0.0.1` only.
- Grafana provisioning is read-only (`allowUiUpdates: false`); data sources use stable UIDs `prometheus` and `tempo`.
- Retention and limits: Prometheus 24 h/512 MB, Tempo 24 h block retention, Collector 128/32 MiB memory limiter, Compose `mem_limit`/`cpus` bounds. Prometheus, Tempo, and Grafana use named volumes; `docker compose down -v` removes them (and the SQL volume) destructively.

## Cache, database, and booking impact

- Cache: instrumentation only. Key `scheduler:v1:dealership:{id}:service-types`, web-default JSON payload, 10-minute TTL, 250 ms operation budget, 500/750 ms cumulative budgets, SQL fallback, and readiness degradation are unchanged. Cache-health probes use a non-recording path so they do not enter catalog ratios.
- Database: none. Telemetry data lives in Prometheus/Tempo volumes, not SQL Server or Redis.
- Booking: none. The procedure, technician-then-bay lock order, transaction, candidate/deadlock bounds, request budget, command timeout, and restricted runtime writes are unchanged. Telemetry adds local activity/counter work and asynchronous export only.
- Metric health filter: the Collector drops `http.server.request.duration` datapoints whose `http.route` starts with `/health/`, because the framework emits them independently of the trace instrumentation filter. Request logs and custom request metrics already exclude health traffic in the API.

## Smoke checks

```powershell
docker compose -p scheduler-t4 config
docker compose -p scheduler-t4 up -d --build --wait
Invoke-WebRequest http://127.0.0.1:13133/
Invoke-WebRequest http://127.0.0.1:9090/-/ready
Invoke-WebRequest http://127.0.0.1:3200/ready
Invoke-WebRequest http://127.0.0.1:3000/api/health
Invoke-WebRequest http://127.0.0.1:3000/api/datasources/uid/prometheus
Invoke-WebRequest http://127.0.0.1:3000/api/datasources/uid/tempo
Invoke-WebRequest http://127.0.0.1:3000/api/dashboards/uid/scheduler-overview
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

- Generate a request with a known `traceparent: 00-<32hex>-<16hex>-01`, then copy its `TraceId` from the API JSON logs or Problem Details `traceId` into Grafana Explore -> Tempo. The same 32-hex trace ID must resolve.
- Query Prometheus for `scheduler_http_requests_total`, `scheduler_booking_outcomes_total`, and `scheduler_cache_operations_total`; use Grafana Explore if the dashboard is not open.

## Outage and recovery

| Scenario | Behavior and recovery |
| --- | --- |
| Collector stopped/unreachable | API starts and serves reads/bookings; readiness reflects SQL/Redis only; exporters drop after bounded queues/timeouts. Restart the Collector and generate new traffic; new telemetry appears within the polling window. Dropped telemetry is not backfilled. |
| Prometheus/Tempo unavailable | The Collector retries/drops within its bounded 30 s retry window; API behavior is unaffected. Restart the service and generate new traffic. |
| Grafana unavailable | Dashboard/data-source access fails; API, booking, and collectors are unaffected. Restart Grafana; provisioning recreates data sources/dashboards. |
| Redis outage | Catalog falls back to SQL; readiness `Degraded`/200; cache failure logs/metrics appear; booking remains correct. Restart Redis; readiness returns to `Healthy`. |
| SQL outage | Reads/booking return `503 dependency_unavailable`; readiness `503`; no false capacity result; no appointment is stored. Restore SQL and rerun the check. |
| Telemetry volume growth | Prometheus 24 h/512 MB and Tempo 24 h limits apply; remove telemetry volumes only with a targeted volume removal. `docker compose down -v` is destructive to local business data. |

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No metrics in Prometheus | Collector metrics pipeline, `--web.enable-otlp-receiver`, OTLP endpoint, `scheduler_*` metric family names, `job="unified-service-scheduler-api"`. |
| No traces in Tempo | App sampling ratio and `traceparent` flags, Collector trace pipeline/queues, Tempo `/ready` and local storage/WAL, trace lookup path. |
| Dashboard missing | Grafana provisioning mounts, provider YAML, `/api/dashboards/uid/scheduler-overview`, data-source UIDs. |
| Dashboard empty with no traffic | Expected for rate/latency panels; counter/ratio panels show documented `0`/`0%`; latency panels show `No data`. |
| Cache hit ratio 0% during traffic | Inspect cache operation/failure panels; 0% may mean no get traffic or no hits. |
| API logs missing trace correlation | Confirm Serilog JSON output on stdout, `TraceId`/`SpanId` properties, and that the request used a sampled `traceparent`. |
| Telemetry outage mistaken for API failure | Check `/health/live` and `/health/ready`; telemetry is not a readiness dependency. |

## Verification record

- Local Compose verification on 2026-11-02: pinned images started; all readiness/provisioning probes returned 200; delivery, spans, metrics, log correlation, Redis degradation, telemetry outage/recovery, and SQL dependency checks passed. Full evidence is in [[test/4-test|the ticket 4 test report]].
- **Operational verification:** Compose startup, provisioning, Redis outage, telemetry outage/recovery, and JSON log correlation checks were executed manually and recorded in [[test/4-test]]. No standalone verification script is required. The manual checklist was generated 2026-11-02 as [[test/4-manual-test]] with all cases Not run; human execution and visual Grafana inspection remain pending.
- No migration, seed, grant, cache invalidation, or backfill is required.
