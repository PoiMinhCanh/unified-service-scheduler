---
title: "4 - Observability: Phase 3 - Telemetry Stack and Dashboards"
tags: [plans, service-scheduler, observability, opentelemetry-collector, prometheus, tempo, grafana, compose]
ticket: 4
status: implemented
updated: 2026-11-02
---

# Phase 3 - Telemetry stack, provisioning, and dashboards

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/02-telemetry-stack-and-dashboards|Telemetry stack and dashboards]].
- Plan overview: [[plans/4/README|Ticket 4 implementation plan]].
- Requirements: S4-03, S4-04, S4-05; AC4-03, AC4-04, AC4-06; TC4-09/TC4-10 prerequisites and TC4-18 operational instructions; project P-04.
- Dependencies: Phase 2 metric/span names and `Scheduler:Telemetry` options.

## Scope and intended behavior

This phase adds the pinned local telemetry stack and provisioning: OpenTelemetry Collector, Prometheus with the OTLP receiver, Tempo with local storage, and Grafana with provisioned Prometheus/Tempo data sources and a committed operations dashboard. Compose starts these services independently of the API; the API keeps its existing `db-setup` dependency only and starts/books/reads when the telemetry services are stopped or unreachable.

All telemetry ports bind to `127.0.0.1`. Grafana uses anonymous Viewer access with `allowUiUpdates: false`; no admin credential is committed. Prometheus/Tempo/Grafana use named volumes and local retention/resource limits. No Loki or logs pipeline is added.

Exclusions: no production hosting, alerting, centralized logs, authentication design, exemplars, or application UI. The stack is a local operational visualization layer, not a booking dependency.

## Pinned components

The tags/digests below come from the verified research and must be re-verified with `docker pull image@sha256:...` during implementation; if a digest cannot be pulled, record the check as Blocked instead of silently using a floating tag.

| Component | Image reference | Role | Host ports | Local limits/retention |
| --- | --- | --- | --- | --- |
| OpenTelemetry Collector | `otel/opentelemetry-collector:0.164.0@sha256:28b6d41ac51bff6b41ba5e8fe503569ffb9d6a1ea90f7a78419e86f34e44f750` | OTLP receivers and fan-out | `127.0.0.1:4317` gRPC, `127.0.0.1:4318` HTTP, `127.0.0.1:13133` health | Memory limiter 128/32 MiB; bounded queues; `mem_limit: 256m`, `cpus: 1.0` |
| Prometheus | `prom/prometheus:v3.15.1@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2` | OTLP metrics receiver/query backend | `127.0.0.1:9090` | 24 h / 512 MB TSDB retention; named volume; `mem_limit: 512m`, `cpus: 1.0` |
| Tempo | `grafana/tempo:3.1.1@sha256:64e2fbd56d9c2b3412a4b430a58ea59e91a670b1e75f920a0c702213ad145044` | OTLP trace receiver/storage/query | `127.0.0.1:3200` HTTP; OTLP internal | 24 h block retention; local blocks/WAL volume; `mem_limit: 512m`, `cpus: 1.0` |
| Grafana | `grafana/grafana:13.3.0@sha256:488984b716c0862639f91a698adbe2862677a5a07bad42da5f03f3030883b205` | Provisioned data sources/dashboard | `127.0.0.1:3000` | Named Grafana volume; `mem_limit: 512m`, `cpus: 1.0` |

All telemetry services use `platform: linux/amd64`, consistent with the existing SQL Server/Redis services.

## Configuration files

### `telemetry/collector.yaml`

- `otlp` receiver with gRPC `0.0.0.0:4317` and HTTP `0.0.0.0:4318`.
- `memory_limiter` (`check_interval: 1s`, `limit_mib: 128`, `spike_limit_mib: 32`) and `batch` (`timeout: 2s`, `send_batch_size: 512`, `send_batch_max_size: 1024`).
- `otlp_grpc/tempo` exporter to `tempo:4317` with insecure TLS, 5 s timeout, bounded retry (`1s` initial, `5s` max interval, `30s` max elapsed), and sending queue (`1000` items, 2 consumers).
- `otlp_http/prometheus` exporter to `http://prometheus:9090/api/v1/otlp` with the same bounded timeout/retry/queue.
- `health_check` extension on `0.0.0.0:13133`; traces and metrics pipelines only; no logs pipeline.
- Use the `0.164.0` component names `otlp_grpc`/`otlp_http`, not deprecated aliases.

### `telemetry/prometheus.yml`

- `global.scrape_interval: 15s`.
- `otlp.promote_resource_attributes: [deployment.environment.name, service.version]`.
- `otlp.translation_strategy: UnderscoreEscapingWithSuffixes`.
- `scrape_configs: []`; metrics arrive through the Collector's OTLP/HTTP exporter.
- Command flags: `--config.file=/etc/prometheus/prometheus.yml`, `--storage.tsdb.path=/prometheus`, `--storage.tsdb.retention.time=24h`, `--storage.tsdb.retention.size=512MB`, `--web.enable-otlp-receiver`, `--web.listen-address=0.0.0.0:9090`.

### `telemetry/tempo.yaml`

- Tempo 3.1 shape: `stream_over_http_enabled: true`, HTTP server on `3200`, OTLP gRPC/HTTP receivers on `4317`/`4318`.
- `backend_scheduler.provider.compaction.compaction.block_retention: 24h`; do not reuse Tempo 2.x `ingester`/`compactor` blocks.
- Local storage at `/var/tempo/blocks` with WAL `/var/tempo/wal`; named volume mounted at `/var/tempo`; `usage_report.reporting_enabled: false`; multitenancy disabled.
- Trace lookup: `GET /api/v2/traces/{traceId}` (JSON default) with the v1 endpoint as fallback; search via `/api/search` for manual inspection.

### Grafana provisioning and dashboard

- `telemetry/grafana/provisioning/datasources/datasources.yaml`: Prometheus uid `prometheus`, Tempo uid `tempo`, proxy access, `editable: false`, Prometheus default.
- `telemetry/grafana/provisioning/dashboards/dashboards.yaml`: provider `scheduler`, folder `Unified Service Scheduler`, path `/var/lib/grafana/dashboards`, `updateIntervalSeconds: 30`, `allowUiUpdates: false`.
- `telemetry/grafana/dashboards/scheduler-overview.json`: UID `scheduler-overview`, title `Unified Service Scheduler - Operations`, tags `service-scheduler`, refresh `10s`, default last 15 minutes.
- Compose mounts provisioning at `/etc/grafana/provisioning:ro` and dashboards at `/var/lib/grafana/dashboards:ro`; environment `GF_AUTH_ANONYMOUS_ENABLED=true`, `GF_AUTH_ANONYMOUS_ORG_ROLE=Viewer`, `GF_USERS_ALLOW_SIGN_UP=false`.

Dashboard panels and PromQL:

| Panel | Type | PromQL | Unit | Zero-traffic behavior |
| --- | --- | --- | --- | --- |
| Request rate by route/method | Timeseries | `sum by (route, method) (rate(scheduler_http_requests_total[$__rate_interval]))` | req/s | No data until traffic; `noValue` documents it. |
| HTTP p95 latency | Timeseries | `histogram_quantile(0.95, sum by (le, http_route, http_request_method) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))` | seconds | `No data`; no synthetic latency. |
| 5xx error ratio | Stat/Timeseries | `sum(rate(scheduler_http_requests_total{status_class="5xx"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_http_requests_total[$__rate_interval])), 1e-9)` or `or vector(0)` | percentunit | `0%` with a guarded denominator. |
| Booking final outcomes | Timeseries/Bar | `sum by (outcome) (rate(scheduler_booking_outcomes_total[$__rate_interval]))` | req/s | `0` via `noValue`; no outcome series yet. |
| Booking procedure attempts | Timeseries | `sum by (result) (rate(scheduler_booking_procedure_attempts_total[$__rate_interval]))` | req/s | `0` via `noValue`; distinguishes internal attempts from final outcomes. |
| Booking allocation p95 | Timeseries | `histogram_quantile(0.95, sum by (le, outcome) (rate(scheduler_booking_allocation_duration_seconds_bucket[$__rate_interval])))` | seconds | `No data`. |
| Cache hit ratio | Stat | `sum(rate(scheduler_cache_operations_total{operation="get", result="hit"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_cache_operations_total{operation="get", result=~"hit\|miss"}[$__rate_interval])), 1e-9)` or `or vector(0)` | percentunit | `0%` when no get operations; documented as "no traffic or no hits", with the operations/failure panels distinguishing traffic and outages. |
| Cache get failures | Timeseries | `sum(rate(scheduler_cache_operations_total{operation="get", result="failure"}[$__rate_interval]))` | ops/s | `0` via `noValue`; Redis degradation signal. |
| Cache operations | Timeseries | `sum by (operation, result) (rate(scheduler_cache_operations_total[$__rate_interval]))` | ops/s | No data until traffic; shows hits/misses/success/failures. |
| Correlation instructions | Text | n/a | n/a | Explains copying `TraceId` from logs/Problem Details into Grafana Explore -> Tempo; metrics use aggregate bounded labels only. |

No latency panel uses `or vector(0)`; a synthetic 0 ms value would be misleading. Ratio panels use guarded denominators and document the 0% meaning.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 3.1 | S4-02, S4-03, S4-04, S4-05 | New `telemetry/collector.yaml`, `prometheus.yml`, `tempo.yaml` | Add the receiver/processor/exporter/pipeline/retention configuration from the contract above. | Collector `validate` exits 0; `promtool check config` succeeds; no deprecated Collector component names; Tempo 3.1 config parses. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 3.2 | S4-03, S4-04, S4-05 | `compose.yaml`, `.env.example` | Add pinned Collector/Prometheus/Tempo/Grafana services, config mounts, named volumes, loopback ports, resource limits, restart policies, and internal dependencies; add `Scheduler__Telemetry__Enabled=true`, `OtlpEndpoint=http://collector:4317`, and `SamplingRatio=1.0` to the API environment; keep the API `depends_on` limited to `db-setup`. | `docker compose config` succeeds; API has no telemetry health dependency; only loopback ports are published; `.env.example` documents non-secret telemetry examples. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 3.3 | S4-03, S4-04 | New `telemetry/grafana/provisioning/datasources/datasources.yaml`, `telemetry/grafana/provisioning/dashboards/dashboards.yaml`, `telemetry/grafana/dashboards/scheduler-overview.json` | Add stable data-source UIDs, dashboard provider, and the committed dashboard with the panels/PromQL/zero-traffic behavior above. | Fresh Grafana starts without manual creation; `/api/datasources/uid/prometheus`, `/api/datasources/uid/tempo`, and `/api/dashboards/uid/scheduler-overview` return the expected objects; dashboard JSON is valid. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 3.4 | S4-04, S4-06 | New `docs/deploy-notes/4-deploy-notes.md`; `docs/setup-guide.md`, `README.md` telemetry access sections | Document startup order, ports/URLs, configuration names, retention/resource limits, outage/recovery, troubleshooting, and local-only access. | Documents match the implemented Compose/config; no secret is recorded; commands are labelled planned until executed. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 3.5 | S4-05 | Compose/stack startup check; API independence check | Start the stack; probe Collector `:13133`, Prometheus `/-/ready`, Tempo `:3200/ready`, Grafana `/api/health`; stop the telemetry services and verify the API starts/reads/books and readiness is unaffected. | All probes succeed with telemetry running; with telemetry stopped, API liveness/readiness and a synthetic booking/read remain correct; restarting telemetry resumes new exports. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 3.6 | S4-06 | `docs/test/4-test.md` | Record Phase 3 configuration-validation, startup, provisioning, zero-traffic, and API-independence commands/results, including Blocked checks and image/digest verification. | Executed evidence is recorded with revision/environment; no planned result is marked Passed; unresolved checks remain explicit. | Implemented 2026-11-02 (see [[test/4-test]]) |

## Verification

| Case | Command / scenario | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Configuration syntax | `docker compose config`; Collector `validate`; `promtool check config` | Compose resolves; Collector/Tempo configs parse; Prometheus config is valid. | Not run (plan-time record; see [[test/4-test]]) |
| Stack readiness | Start the Compose stack; probe Collector `http://127.0.0.1:13133`, Prometheus `http://127.0.0.1:9090/-/ready`, Tempo `http://127.0.0.1:3200/ready`, Grafana `http://127.0.0.1:3000/api/health` | All endpoints report ready within bounded startup time. | Not run (plan-time record; see [[test/4-test]]) |
| Provisioning | `Invoke-RestMethod http://127.0.0.1:3000/api/datasources/uid/prometheus`; `.../tempo`; `.../api/dashboards/uid/scheduler-overview` | Data sources and dashboard exist with the expected UIDs/title/panels; no manual creation. | Not run (plan-time record; see [[test/4-test]]) |
| Zero-traffic PromQL | Query the dashboard PromQL against a fresh Prometheus before generating traffic | Counter panels show `0`/no data as documented; latency panels show `No data`; ratio panels show `0%` without NaN/error. | Not run (plan-time record; see [[test/4-test]]) |
| API independence | Stop telemetry services; check `/health/live`, `/health/ready`, a catalog read, and a synthetic booking; restart telemetry | API starts/responds/books with telemetry stopped; readiness reflects SQL/Redis only; new telemetry resumes after restart. | Not run (plan-time record; see [[test/4-test]]) |
| Port/resource review | `docker compose config`; `docker compose ps`; inspect volumes/limits | Only loopback ports are published; pinned images/digests, named volumes, retention/resource limits match the plan. | Not run (plan-time record; see [[test/4-test]]) |

## Exit criteria

- Pinned Collector/Prometheus/Tempo/Grafana configuration and Grafana provisioning/dashboard files exist and validate.
- The stack starts with health/query endpoints available; fresh Grafana provisioning exposes the data sources and dashboard without manual creation.
- Zero-traffic behavior matches the dashboard documentation; no panel produces an undefined division or misleading synthetic latency.
- The API remains usable and readiness stays SQL/Redis-based with telemetry services stopped; restarting telemetry resumes new exports.
- Deployment notes and setup/README access instructions reflect actual commands/observations; Phase 3 evidence is recorded in `docs/test/4-test.md`.
- No end-to-end delivery or acceptance claim is made before Phase 4/5 evidence.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: no key/payload/TTL/invalidation change; telemetry stack is outside Redis.
- Booking: no behavior change; telemetry services are not a startup/readiness dependency.
- Configuration/deployment: new services, volumes, loopback ports, resource limits, retention, telemetry environment, and deploy notes.
- Remaining decision: image digests must be re-pulled and verified during implementation; an unavailable digest/image is Blocked, not a reason to use a floating tag.
- Remaining decision: zero-traffic panel display depends on Grafana panel options (`noValue`, `or vector(0)`); implementation must record the actual rendered/query behavior, not assume it.
