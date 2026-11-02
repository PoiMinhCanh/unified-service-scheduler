---
title: "4 - Observability: Telemetry Stack and Dashboards"
tags: [research, service-scheduler, observability, opentelemetry-collector, prometheus, tempo, grafana]
ticket: 4
status: verified
updated: 2026-11-02
---

# 4 - Telemetry stack and dashboards

- Ticket: [[4 - Observability|Ticket 4]]
- Research overview: [[research/4/00-overview|Ticket 4 research overview]]
- Requirements: S4-03, S4-04, S4-05; AC4-03, AC4-04, AC4-06; T-15, S4-V01, S4-V03.
- Evidence baseline: HEAD `92a81efde5dac1070669fff13193e452dc9b6529` with a clean working tree; inspected `compose.yaml`, `.env.example`, Dockerfile, application configuration, and existing health/cache/booking behavior. Research-phase configuration probes used the pinned images on 2026-11-01/2026-11-02 and are recorded in [[#Research-phase configuration probes]]; they are not ticket tests.

> [!IMPORTANT]
> No Collector, Prometheus, Tempo, Grafana, provisioning, dashboard, or telemetry Compose service exists. The configuration below is proposed. The probes validate configuration shapes and startup; they do not establish end-to-end delivery, dashboards under workload, zero-traffic behavior in the ticket environment, or ticket acceptance.

## Existing evidence

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| Compose currently runs SQL Server, Redis, one-shot setup, and the API only. | `compose.yaml` services `sqlserver`, `redis`, `db-setup`, `api`; no telemetry services, volumes, or ports. | Add telemetry services without adding an API dependency on them. |
| The API depends only on successful `db-setup`, not Redis health. | `compose.yaml` `api.depends_on.db-setup.condition: service_completed_successfully`; no Redis condition. | Preserve API independence from Redis and telemetry. |
| Existing ports are loopback-published. | `127.0.0.1:1433:1433`, `127.0.0.1:6379:6379`, `127.0.0.1:8080:8080`. | Publish telemetry ports to `127.0.0.1` only. |
| Existing images are tag+digest pinned and `platform: linux/amd64`. | `mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04@sha256:...`, `redis:7.4.12-alpine@sha256:...`. | Follow the same pinning style for telemetry images. |
| `.env.example` carries SQL/Redis credentials/endpoints; no telemetry configuration. | `.env.example`; Compose interpolation. | Add non-secret telemetry configuration to Compose/`appsettings`; no new credential is required for anonymous read-only Grafana. |
| No telemetry backend is present in the checkout. | `telemetry/` does not exist; no config mounts. | All backend files are proposed additions. |

## Pinned components

The image tags and manifest-list digests below were observed by pulling the images on 2026-11-01 with Docker `25.0.3`. They must be re-verified with `docker pull image@sha256:...` during implementation; a tag alone is not a pin.

| Component | Image reference | Role | Host ports | Local retention/resource intent |
| --- | --- | --- | --- | --- |
| OpenTelemetry Collector | `otel/opentelemetry-collector:0.164.0@sha256:28b6d41ac51bff6b41ba5e8fe503569ffb9d6a1ea90f7a78419e86f34e44f750` | OTLP receivers, batch/memory limits, fan-out to Tempo/Prometheus | `127.0.0.1:4317` gRPC, `127.0.0.1:4318` HTTP, `127.0.0.1:13133` health | Memory limiter 128/32 MiB; bounded queues; `mem_limit: 256m`, `cpus: 1.0` |
| Prometheus | `prom/prometheus:v3.15.1@sha256:fa3302279710be2f21a8301ec17a8b4f2301fd829bd113839b3b6b02b6f191a2` | OTLP metrics receiver and query backend | `127.0.0.1:9090` | 24 h / 512 MB TSDB retention; named volume; `mem_limit: 512m`, `cpus: 1.0` |
| Tempo | `grafana/tempo:3.1.1@sha256:64e2fbd56d9c2b3412a4b430a58ea59e91a670b1e75f920a0c702213ad145044` | OTLP trace receiver/storage/query | `127.0.0.1:3200` HTTP; OTLP ports remain internal | 24 h block retention; local block/WAL volume; `mem_limit: 512m`, `cpus: 1.0` |
| Grafana | `grafana/grafana:13.3.0@sha256:488984b716c0862639f91a698adbe2862677a5a07bad42da5f03f3030883b205` | Provisioned Prometheus/Tempo data sources and dashboard | `127.0.0.1:3000` | Named Grafana volume for provisioning state; `mem_limit: 512m`, `cpus: 1.0` |

All telemetry services use `platform: linux/amd64`, consistent with the existing stack. Image tags/digests are implementation pins; if a digest cannot be pulled, the plan must record the blocked check rather than silently switching to a floating tag.

## Collector design

### Proposed configuration (`telemetry/collector.yaml`)

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318
processors:
  memory_limiter:
    check_interval: 1s
    limit_mib: 128
    spike_limit_mib: 32
  batch:
    timeout: 2s
    send_batch_size: 512
    send_batch_max_size: 1024
exporters:
  otlp_grpc/tempo:
    endpoint: tempo:4317
    tls:
      insecure: true
    timeout: 5s
    retry_on_failure:
      enabled: true
      initial_interval: 1s
      max_interval: 5s
      max_elapsed_time: 30s
    sending_queue:
      enabled: true
      num_consumers: 2
      queue_size: 1000
  otlp_http/prometheus:
    endpoint: http://prometheus:9090/api/v1/otlp
    timeout: 5s
    retry_on_failure:
      enabled: true
      initial_interval: 1s
      max_interval: 5s
      max_elapsed_time: 30s
    sending_queue:
      enabled: true
      num_consumers: 2
      queue_size: 1000
extensions:
  health_check:
    endpoint: 0.0.0.0:13133
service:
  extensions: [health_check]
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp_grpc/tempo]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp_http/prometheus]
```

Notes:

- Collector 0.164.0 renamed the exporters to `otlp_grpc` and `otlp_http`; `otlp`/`otlphttp` remain deprecated aliases. Use the new names to avoid deprecation warnings.
- The app sends both traces and metrics to the Collector over OTLP/gRPC at `collector:4317`; the HTTP receiver is available for manual/debug clients.
- The Collector sends traces to Tempo over gRPC and metrics to Prometheus over OTLP/HTTP. The Prometheus exporter's base endpoint `http://prometheus:9090/api/v1/otlp` appends `/v1/metrics` for metrics.
- The `batch` and `memory_limiter` processors bound memory; exporter queues/retries are bounded. The Collector is not an application dependency.
- `health_check` is exposed at 13133 for manual/automated readiness checks; no Compose healthcheck is proposed because the Collector image's shell tooling should not be assumed. Smoke checks poll the HTTP endpoint.
- No logs pipeline is configured. Serilog writes JSON to container stdout; centralized log storage is outside scope.

### Collector pipeline boundaries

| Signal | Receiver | Processors | Exporter | Storage |
| --- | --- | --- | --- | --- |
| Traces | `otlp` (gRPC/HTTP) | `memory_limiter`, `batch` | `otlp_grpc/tempo` | Tempo local blocks/WAL |
| Metrics | `otlp` (gRPC/HTTP) | `memory_limiter`, `batch` | `otlp_http/prometheus` | Prometheus TSDB |

No sampling/transform processor is added. Application sampling is decided in the SDK; the Collector preserves traces. No tail sampling is required for the local demo.

## Prometheus design

### Proposed configuration (`telemetry/prometheus.yml`)

```yaml
global:
  scrape_interval: 15s
otlp:
  promote_resource_attributes:
    - deployment.environment.name
    - service.version
  translation_strategy: UnderscoreEscapingWithSuffixes
scrape_configs: []
```

### Proposed command/flags

```text
--config.file=/etc/prometheus/prometheus.yml
--storage.tsdb.path=/prometheus
--storage.tsdb.retention.time=24h
--storage.tsdb.retention.size=512MB
--web.enable-otlp-receiver
--web.listen-address=0.0.0.0:9090
```

Notes:

- The OTLP receiver is disabled by default; `--web.enable-otlp-receiver` is required. The receiver accepts OTLP/HTTP metrics at `/api/v1/otlp/v1/metrics`.
- No scrape jobs are required because metrics are pushed by the Collector. `scrape_configs: []` is explicit; the OTLP receiver is the source.
- `service.name` becomes the `job` label (`unified-service-scheduler-api`); `service.instance.id` becomes `instance` (`scheduler-api`). `deployment.environment.name` and `service.version` are promoted to metric labels; other resource attributes remain on `target_info`.
- Default `UnderscoreEscapingWithSuffixes` adds `_total` to monotonic counters and `_seconds` to second-based histograms. Expected names are in Part 1; the smoke test must assert the actual names before they are treated as a dashboard contract.
- Retention is time- and size-bounded; the local demo does not claim durable metrics beyond the local volume. `docker compose down -v` removes telemetry history.
- Prometheus is not an API readiness dependency.

## Tempo design

### Proposed configuration (`telemetry/tempo.yaml`)

Tempo 3.1 removed the legacy `ingester` and `compactor` blocks; a Tempo 2.x-style config fails to parse. Use the 3.1 shape:

```yaml
stream_over_http_enabled: true
server:
  http_listen_port: 3200
distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          endpoint: 0.0.0.0:4317
        http:
          endpoint: 0.0.0.0:4318
backend_scheduler:
  provider:
    compaction:
      compaction:
        block_retention: 24h
storage:
  trace:
    backend: local
    local:
      path: /var/tempo/blocks
    wal:
      path: /var/tempo/wal
usage_report:
  reporting_enabled: false
```

Notes:

- `target` defaults to `all` (monolithic single binary). The backend scheduler/worker run in-process in monolithic mode; the retention block is under `backend_scheduler.provider.compaction.compaction.block_retention`.
- OTLP gRPC/HTTP receivers listen internally on 4317/4318; the Collector reaches Tempo at `tempo:4317`. Only the HTTP query/API port 3200 is published to the host for inspection.
- Local storage is monolithic-only and uses `/var/tempo/blocks` plus `/var/tempo/wal`; mount a named volume at `/var/tempo` so a normal container restart preserves recent telemetry.
- Multitenancy is disabled by default; no `X-Scope-OrgID` is required. `usage_report.reporting_enabled: false` avoids external reporting.
- Trace lookup: `GET /api/v2/traces/{traceId}` returns OpenTelemetry JSON by default; `GET /api/traces/{traceId}` is the v1 fallback. Both accept `Accept: application/protobuf` for binary. Search is `GET /api/search?tags=service.name%3D...` for manual inspection.
- Retention is 24 h for the local demo; telemetry beyond that is intentionally dropped. No Loki/log storage is added.
- Tempo is not an API readiness dependency.

## Grafana design

### Provisioning files

- `telemetry/grafana/provisioning/datasources/datasources.yaml`:

```yaml
apiVersion: 1
datasources:
  - name: Prometheus
    uid: prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    editable: false
  - name: Tempo
    uid: tempo
    type: tempo
    access: proxy
    url: http://tempo:3200
    editable: false
```

- `telemetry/grafana/provisioning/dashboards/dashboards.yaml`:

```yaml
apiVersion: 1
providers:
  - name: scheduler
    orgId: 1
    folder: Unified Service Scheduler
    type: file
    disableDeletion: false
    updateIntervalSeconds: 30
    allowUiUpdates: false
    options:
      path: /var/lib/grafana/dashboards
```

- `telemetry/grafana/dashboards/scheduler-overview.json` uses dashboard UID `scheduler-overview`, title `Unified Service Scheduler - Operations`, tags `service-scheduler`, refresh `10s`, and a default time range of the last 15 minutes. Data source references use the stable UIDs `prometheus`/`tempo`.
- Mount provisioning at `/etc/grafana/provisioning:ro` and dashboards at `/var/lib/grafana/dashboards:ro`.
- Grafana environment for the local demo: `GF_AUTH_ANONYMOUS_ENABLED=true`, `GF_AUTH_ANONYMOUS_ORG_ROLE=Viewer`, `GF_USERS_ALLOW_SIGN_UP=false`. This gives loopback-only read access to dashboards/data sources without committing admin credentials. Provisioning remains the source of truth (`allowUiUpdates: false`); no manual data-source/dashboard creation is required.

### Dashboard panels and PromQL

| Panel | Type | PromQL | Unit | Zero-traffic behavior |
| --- | --- | --- | --- | --- |
| Request rate by route/method | Timeseries | `sum by (route, method) (rate(scheduler_http_requests_total[$__rate_interval]))` | req/s | No data until traffic; panel `noValue` documents it. |
| HTTP p95 latency | Timeseries | `histogram_quantile(0.95, sum by (le, http_route, http_request_method) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))` | seconds | `No data` (no synthetic latency). |
| 5xx error ratio | Stat/Time series | `sum(rate(scheduler_http_requests_total{status_class="5xx"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_http_requests_total[$__rate_interval])), 1e-9)` or `or vector(0)` | percentunit | `0%` with `or vector(0)`; denominator guard avoids NaN. |
| Booking final outcomes | Time series/Bar | `sum by (outcome) (rate(scheduler_booking_outcomes_total[$__rate_interval]))` | req/s | `0` via `noValue`; no outcome series yet. |
| Booking procedure attempts | Time series | `sum by (result) (rate(scheduler_booking_procedure_attempts_total[$__rate_interval]))` | req/s | `0` via `noValue`; distinguishes internal attempts from final outcomes. |
| Booking allocation p95 | Time series | `histogram_quantile(0.95, sum by (le, outcome) (rate(scheduler_booking_allocation_duration_seconds_bucket[$__rate_interval])))` | seconds | `No data`. |
| Cache hit ratio | Stat | `sum(rate(scheduler_cache_operations_total{operation="get", result="hit"}[$__rate_interval])) / clamp_min(sum(rate(scheduler_cache_operations_total{operation="get", result=~"hit\|miss"}[$__rate_interval])), 1e-9)` or `or vector(0)` | percentunit | `0%` when no get operations; documented as "no traffic or no hits", with the cache-operations panel distinguishing traffic from failures. |
| Cache get failures | Time series | `sum(rate(scheduler_cache_operations_total{operation="get", result="failure"}[$__rate_interval]))` | ops/s | `0` via `noValue`; degradation signal during Redis outage. |
| Cache operations | Time series | `sum by (operation, result) (rate(scheduler_cache_operations_total[$__rate_interval]))` | ops/s | No data until traffic; shows hits/misses/success/failures separately. |
| Correlation instructions | Text | n/a | n/a | Explains: copy `TraceId` from JSON logs or Problem Details `traceId`, open Grafana Explore -> Tempo, query the trace ID; metrics are aggregate and use bounded labels only. |

Zero-traffic rules are explicit in the dashboard description/panel descriptions. Counter panels may display `0`; latency panels display `No data`; ratio panels use a guarded denominator and display `0%`; no panel divides by an unbounded or missing denominator without a guard. Panels do not use `or vector(0)` for latency because a synthetic 0 ms value would be misleading.

### Access and local operation

| Service | URL / endpoint | Notes |
| --- | --- | --- |
| Grafana | `http://127.0.0.1:3000` | Anonymous Viewer; dashboard folder `Unified Service Scheduler`. |
| Dashboard | `http://127.0.0.1:3000/d/scheduler-overview` | Provisioned UID; no manual creation. |
| Prometheus | `http://127.0.0.1:9090` | Query UI/API; OTLP receiver enabled. |
| Tempo | `http://127.0.0.1:3200` | Trace API/query; OTLP ports remain internal. |
| Collector health | `http://127.0.0.1:13133` | Health-check extension. |
| API | `http://127.0.0.1:8080` | Existing API/Swagger. |

For a host `dotnet run`, export `Scheduler__Telemetry__Enabled=true` and `Scheduler__Telemetry__OtlpEndpoint=http://127.0.0.1:4317`; Compose supplies the container endpoint `http://collector:4317`. Telemetry remains disabled by default so existing tests and telemetry-free local runs do not attempt exports.

## Compose integration

### Proposed service additions

```yaml
  collector:
    image: otel/opentelemetry-collector:0.164.0@sha256:...
    platform: linux/amd64
    command: ["--config=/etc/otelcol/collector.yaml"]
    volumes:
      - ./telemetry/collector.yaml:/etc/otelcol/collector.yaml:ro
    ports:
      - "127.0.0.1:4317:4317"
      - "127.0.0.1:4318:4318"
      - "127.0.0.1:13133:13133"
    depends_on:
      prometheus:
        condition: service_started
      tempo:
        condition: service_started
    mem_limit: 256m
    cpus: 1.0
    restart: unless-stopped

  prometheus:
    image: prom/prometheus:v3.15.1@sha256:...
    platform: linux/amd64
    command: [...bounded flags...]
    volumes:
      - ./telemetry/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - scheduler-prometheus-data:/prometheus
    ports:
      - "127.0.0.1:9090:9090"
    mem_limit: 512m
    cpus: 1.0
    restart: unless-stopped

  tempo:
    image: grafana/tempo:3.1.1@sha256:...
    platform: linux/amd64
    command: ["-config.file=/etc/tempo/tempo.yaml"]
    volumes:
      - ./telemetry/tempo.yaml:/etc/tempo/tempo.yaml:ro
      - scheduler-tempo-data:/var/tempo
    ports:
      - "127.0.0.1:3200:3200"
    mem_limit: 512m
    cpus: 1.0
    restart: unless-stopped

  grafana:
    image: grafana/grafana:13.3.0@sha256:...
    platform: linux/amd64
    environment:
      GF_AUTH_ANONYMOUS_ENABLED: "true"
      GF_AUTH_ANONYMOUS_ORG_ROLE: "Viewer"
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./telemetry/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./telemetry/grafana/dashboards:/var/lib/grafana/dashboards:ro
      - scheduler-grafana-data:/var/lib/grafana
    ports:
      - "127.0.0.1:3000:3000"
    depends_on:
      prometheus:
        condition: service_started
      tempo:
        condition: service_started
    mem_limit: 512m
    cpus: 1.0
    restart: unless-stopped
```

The API service keeps its existing `depends_on: db-setup` only. Add `Scheduler__Telemetry__Enabled=true`, `Scheduler__Telemetry__OtlpEndpoint=http://collector:4317`, and `Scheduler__Telemetry__SamplingRatio=1.0` to its environment. The API must start and serve requests if the telemetry services are stopped or fail; Compose must not add a telemetry health condition to `api`.

Add named volumes `scheduler-prometheus-data`, `scheduler-tempo-data`, and `scheduler-grafana-data`; existing `scheduler-sql-data` is unchanged. `docker compose down` keeps telemetry volumes; `docker compose down -v` removes them (destructive to local telemetry history only).

### Startup and recovery order

1. SQL Server and Redis start; `db-setup` applies migrations/grants/seeds; the API starts after setup.
2. Prometheus, Tempo, Grafana, and Collector start independently. The API does not wait for them.
3. Collector exporters may retry while Prometheus/Tempo start; queues are bounded. Grafana may return an error until its data sources are reachable; provisioning still completes.
4. If telemetry services are stopped, API requests/readiness are unaffected; restarting them resumes new exports. Dropped telemetry is not backfilled.
5. If the Collector is unavailable, the API SDK drops after bounded queue/timeout; no request failure. If Prometheus/Tempo are unavailable, the Collector retries/drops within its window.

## Operational instructions and troubleshooting

| Symptom | What to inspect |
| --- | --- |
| No metrics in Prometheus | Collector logs/pipeline, OTLP endpoint, Prometheus `--web.enable-otlp-receiver`, `/api/v1/otlp/v1/metrics`, metric names/labels, `job`/`instance` values. |
| No traces in Tempo | Collector trace pipeline, Tempo OTLP receiver, sampling ratio/`traceparent` flags, Tempo local storage/WAL, trace ID lookup `/api/v2/traces/{traceId}`. |
| Grafana dashboard missing | Provisioning mounts/paths, provider YAML, Grafana logs, `/api/dashboards/uid/scheduler-overview`; data source UIDs `prometheus`/`tempo`. |
| Dashboard shows no data with no workload | Expected for rate/latency panels; verify documented zero-traffic behavior and query a fresh stack before generating traffic. |
| Dashboard shows no data after workload | Check time range, Prometheus labels (`job="unified-service-scheduler-api"`, `instance="scheduler-api"`), metric translation, and Collector export. |
| Cache hit ratio 0% during traffic | Check cache operation series; 0% can mean all misses or no get operations; inspect cache failures/Redis health. |
| Telemetry outage vs API failure | Check `/health/live` and `/health/ready`; telemetry is not a readiness dependency. API success during telemetry outage is expected. |
| Retention/volume growth | Prometheus 24 h/512 MB limits; Tempo 24 h block retention; `down -v` removes telemetry volumes. |
| Port conflict | All ports bind to `127.0.0.1`; change host ports only with a documented local override; Compose service-to-service DNS remains unchanged. |

## Research-phase configuration probes

These probes used temporary config files and isolated pinned containers on 2026-11-01/2026-11-02. They validate configuration shapes and startup; they are not ticket tests and do not establish T-15/S4-V03 or acceptance.

| Probe | Command / setup | Observed result |
| --- | --- | --- |
| Collector config | `docker run --rm -v <temp>:/etc/otelcol otel/opentelemetry-collector:0.164.0 validate --config=/etc/otelcol/collector.yaml` with the proposed `otlp_grpc`/`otlp_http` config | Exit code 0; no component/config error. |
| Prometheus config | `promtool check config` in `prom/prometheus:v3.15.1` with the proposed `otlp` block and empty `scrape_configs` | `SUCCESS: ... is valid prometheus config file syntax`. |
| Prometheus OTLP endpoint | Start with `--web.enable-otlp-receiver`; `/api/v1/status/config` and `/api/v1/query?query=vector(1)`; `POST /api/v1/otlp/v1/metrics` with an empty body | Server ready; config/query returned 200; OTLP endpoint returned 200 (endpoint exists; empty payload behavior is not a delivery test). |
| Tempo 2.x-style config | Start `grafana/tempo:3.1.1` with `ingester`/`compactor` blocks | Failed: fields not found in `app.Config`; Tempo 3 removed those blocks. |
| Tempo 3.1 config | Start with `target` default, OTLP receiver, `storage.trace.local`/`wal`, and `backend_scheduler.provider.compaction.compaction.block_retention: 24h` | Started successfully; HTTP server on 3200 and backend scheduler/worker modules started; no config parse error. |
| Grafana provisioning | Start `grafana/grafana:13.3.0` with provisioning/data-source/dashboard mounts, anonymous Viewer; query `/api/health`, `/api/dashboards/uid/scheduler-overview`, `/api/datasources/uid/prometheus`, `/api/datasources/uid/tempo` | Health 200 (`version 13.3.0`); dashboard 200 with expected title/panel; both data source endpoints 200. |
| Image pulls | `docker pull` for the four pinned references | Manifest-list digests observed; image tags exist for the proposed versions. |

## Code audit for this concern

| Classification | File / component | Current behavior / gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `compose.yaml` | No telemetry services/volumes/ports/env; API depends only on setup. | Add pinned Collector/Prometheus/Tempo/Grafana, volumes, loopback ports, resource limits, telemetry env; do not add an API telemetry dependency. | S4-03, S4-04, S4-05 |
| Must Change | `.env.example` / README / setup guide | No telemetry endpoints or access instructions. | Document `Scheduler__Telemetry__*` host/Compose values and local access; no new secret for anonymous Viewer. | S4-04 |
| Proposed Addition | `telemetry/collector.yaml` | Missing. | Add receiver/processors/exporters/pipelines/health check. | S4-03-S4-05 |
| Proposed Addition | `telemetry/prometheus.yml` | Missing. | Add OTLP config/promotion/translation and empty scrape jobs. | S4-03, S4-04 |
| Proposed Addition | `telemetry/tempo.yaml` | Missing. | Add Tempo 3.1 OTLP/local storage/backend-scheduler retention. | S4-02, S4-04 |
| Proposed Addition | Grafana provisioning/dashboard JSON | Missing. | Add data sources, provider, dashboard panels/PromQL/zero-traffic descriptions. | S4-03, S4-04 |
| No Change | SQL Server/Redis services and volumes | Existing local dependencies. | Leave unchanged. | Constraint |
| No Change | API health/readiness and booking/cache behavior | Correctness and availability contract. | No telemetry health check; no budget/behavior change. | P-01, P-02, S4-05 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| K4-01 | Decision | Collector/Prometheus/Tempo/Grafana versions? | 0.164.0 / 3.15.1 / 3.1.1 / 13.3.0 with manifest-list digests. | Resolved; pulls/probes. |
| K4-02 | Decision | Collector component names? | `otlp` receiver; `otlp_grpc`/`otlp_http` exporters. | Probed. |
| K4-03 | Decision | Prometheus OTLP path/flags/labels? | `--web.enable-otlp-receiver`; `/api/v1/otlp/v1/metrics`; promote env/version; default translation. | Probed. |
| K4-04 | Decision | Tempo 3 retention/config? | `backend_scheduler.provider.compaction.compaction.block_retention: 24h`; local blocks/WAL. | Probed. |
| K4-05 | Decision | Grafana access/provisioning? | Anonymous Viewer on loopback; provisioned UIDs/folder/dashboard; no manual creation. | Probed. |
| K4-06 | Risk | Metric translation names may differ. | Smoke test asserts actual names/labels before dashboard contract is considered verified; adjust dashboard/Queries if needed. | Deferred to implementation. |
| K4-07 | Risk | Tempo 3 config schema changes. | Pin 3.1.1 and use the validated config; do not reuse Tempo 2.x blocks. | Resolved; probe. |
| K4-08 | Risk | Telemetry stack increases local resource use. | Resource limits, 24 h/512 MB Prometheus, 24 h Tempo, named volumes; document local-only scope. | Resolved with documented limits. |
| K4-09 | Risk | Grafana anonymous access. | Viewer only, loopback only, read-only provisioning; no admin credentials; production access out of scope. | Resolved; probe. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S4-03, AC4-03 | Collector config validate; Prometheus/Tempo receive workload data; query real APIs; metric names/labels. | Automated smoke |
| S4-04, AC4-04 | Fresh Compose/test stack provisions data sources/dashboard; Grafana health/dashboard/panel queries; zero-traffic behavior. | Automated smoke + inspection |
| S4-05, AC4-06 | API remains usable with telemetry services stopped/unreachable; readiness unaffected; recovery resumes exports. | Automated operational |
| S4-V03 | Dashboard panel signals and documented zero-traffic behavior. | Automated smoke + inspection |

All checks are **Not run**. Detailed test design is in [[research/4/03-verification-workload-and-operations|Part 3]].
