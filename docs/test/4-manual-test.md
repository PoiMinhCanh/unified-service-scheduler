---
title: "4 - Manual Tests"
tags: [test, manual, service-scheduler, observability, telemetry, prometheus, grafana, tracing]
ticket: 4
status: not-run
updated: 2026-11-02
---

# 4 - Manual tests

## Scope and prerequisites

- Ticket: [[4 - Observability|Ticket 4]]
- Research, plan, and primary test report: [[research/4/00-overview|Ticket 4 research overview]] with [[research/4/01-instrumentation-and-correlation|instrumentation and correlation]], [[research/4/02-telemetry-stack-and-dashboards|telemetry stack and dashboards]], and [[research/4/03-verification-workload-and-operations|verification and operations]]; [[plans/4/README|Ticket 4 implementation plan]]; [[test/4-test|ticket 4 executed test report]]; [[deploy-notes/4-deploy-notes|ticket 4 deployment notes]].
- Review: [[review/4-review|Ticket 4 review]] (2026-11-02; R-01-R-07 fixed and re-verified; no automated finding remains open).
- Reviewed revision and implementation scope: checkout HEAD `92a81efde5dac1070669fff13193e452dc9b6529` plus the staged ticket 4 working-tree change (application telemetry, Compose telemetry services, telemetry tests, and documentation). The recorded automated run was executed on 2026-11-02. Checklist generation did not re-execute automated or operational checks.
- Requirement IDs: S4-01 through S4-06; acceptance labels AC4-01 through AC4-08; ticket verification cases T-13 and T-15; S4-V01 through S4-V03; project requirement P-03 with P-01/P-02 booking/cache constraints.
- Exclusions: database schema, migration, procedure, runtime permission, cache key/payload/TTL/invalidation, booking algorithm/locking/retry, Loki/centralized logging, alerting, production hosting, telemetry-UI authentication, a custom application UI, exemplars, and performance targets. Health semantics remain SQL/Redis-only; telemetry is not a readiness dependency.
- Environment and API base URL: local Docker Compose stack; API `http://127.0.0.1:8080`; Swagger UI `http://127.0.0.1:8080/swagger/index.html`; OpenAPI `http://127.0.0.1:8080/openapi/v1.json`; Grafana `http://127.0.0.1:3000`; Prometheus `http://127.0.0.1:9090`; Tempo `http://127.0.0.1:3200`; Collector health `http://127.0.0.1:13133`. All ports bind to loopback; there is no HTTPS and no login in the initial local demo.
- Command shell: the examples use Windows PowerShell and `curl.exe`; run them from the repository root. Record the actual shell and versions if they differ.
- Dependency versions: .NET SDK `10.0.403`; Docker server `25.0.3`; Compose `v2.24.6-desktop.1`; SQL Server 2022 CU23; Redis `7.4.12-alpine`; Collector `0.164.0`; Prometheus `v3.15.1`; Tempo `3.1.1`; Grafana `13.3.0`. Serilog.AspNetCore `10.0.0`; OpenTelemetry `1.20.1` family. The pins are in `Directory.Packages.props`, `compose.yaml`, and the deploy notes.
- Seed and migration baseline: migrations `20261031092016_InitialCreate` and `20261101161530_AddBookingProcedureAndTimeZoneMappings`; no ticket 4 migration or seed. Setup seeds weekday shifts for a 28-day horizon from the setup time; `db-setup` reruns idempotently and adds missing future shifts.
- Dealership time zone: Saigon `Asia/Ho_Chi_Minh` (UTC+7, no DST); weekday opening hours 09:00-17:00 local. The seeded active Saigon technician and bay form the single eligible pair used by booking checks.

| Entity | Identifier | Notes |
| --- | --- | --- |
| Saigon dealership | `a1000000-0000-0000-0000-000000000001` | `Asia/Ho_Chi_Minh`; weekday 09:00-17:00 |
| Customer An Nguyen | `c1000000-0000-0000-0000-000000000001` | Owns the An Nguyen vehicle |
| An Nguyen vehicle | `b1000000-0000-0000-0000-000000000001` | Synthetic VIN; do not record the VIN in evidence |
| Saigon Oil Change | `51000000-0000-0000-0000-000000000001` | 60 minutes; one active qualified technician/bay |
| Saigon active technician | `71000000-0000-0000-0000-000000000001` | Read-only trace interpretation; not sent in requests |
| Saigon active bay | `81000000-0000-0000-0000-000000000001` | Read-only trace interpretation; not sent in requests |
| Catalog cache key | `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types` | 10-minute TTL; cold/warm checks delete only this key |

- Future-time preparation: run this snippet and use the printed `$startsAt` (and the alternate slots where a case names them); it selects a weekday at 10:00 Saigon local at least two days ahead and within the seeded 28-day horizon when setup was run recently.

```powershell
$base = 'http://127.0.0.1:8080'
$saigonId = 'a1000000-0000-0000-0000-000000000001'
$anId = 'c1000000-0000-0000-0000-000000000001'
$anVehicleId = 'b1000000-0000-0000-0000-000000000001'
$oilChange = '51000000-0000-0000-0000-000000000001'
$appointments = "$base/api/dealerships/$saigonId/appointments"
$availability = "$base/api/dealerships/$saigonId/availability"
$catalog = "$base/api/dealerships/$saigonId/service-types"
$grafana = 'http://127.0.0.1:3000'
$prometheus = 'http://127.0.0.1:9090'
$tempo = 'http://127.0.0.1:3200'

$saigon = [System.TimeZoneInfo]::FindSystemTimeZoneById('Asia/Ho_Chi_Minh')
$localNow = [System.TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $saigon)
$date = $localNow.Date.AddDays(2)
while ($date.DayOfWeek -in @([System.DayOfWeek]::Saturday, [System.DayOfWeek]::Sunday)) { $date = $date.AddDays(1) }
$startsAt = [DateTimeOffset]::new($date.AddHours(10), $saigon.GetUtcOffset($date)).ToString('yyyy-MM-ddTHH:mm:sszzz')
$startsAtAlt = [DateTimeOffset]::new($date.AddHours(13), $saigon.GetUtcOffset($date)).ToString('yyyy-MM-ddTHH:mm:sszzz')
$startsAtAlt2 = [DateTimeOffset]::new($date.AddHours(15), $saigon.GetUtcOffset($date)).ToString('yyyy-MM-ddTHH:mm:sszzz')
$startsAtQuery = [uri]::EscapeDataString($startsAt)
$startsAtAltQuery = [uri]::EscapeDataString($startsAtAlt)
Write-Output "startsAt: $startsAt; alternate: $startsAtAlt; second alternate: $startsAtAlt2"

function New-TraceContext {
    $traceId = [guid]::NewGuid().ToString('N')
    $spanId = [guid]::NewGuid().ToString('N').Substring(0, 16)
    [pscustomobject]@{
        TraceId = $traceId
        SpanId = $spanId
        Traceparent = "00-$traceId-$spanId-01"
    }
}

function Get-T4Logs {
    docker compose -p scheduler-t4-manual logs --no-color --no-log-prefix api
}

function Query-Prometheus {
    param([string]$Query)
    $encoded = [uri]::EscapeDataString($Query)
    (Invoke-RestMethod -Uri "$prometheus/api/v1/query?query=$encoded").data.result
}

function Remove-T4Appointment {
    param([string]$AppointmentId)
    $sa = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''
    docker compose -p scheduler-t4-manual exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $sa -C -d ServiceScheduler -Q "DELETE FROM dbo.Appointments WHERE Id = '$AppointmentId';"
}
```

- If availability is `false` for the chosen slot, the date may be outside the seeded horizon or the slot may already be booked. Rerun setup with `docker compose -p scheduler-t4-manual down` then `docker compose -p scheduler-t4-manual up -d --wait` (both keep the named SQL volume and add missing future shifts), or choose a later weekday and regenerate the variables.
- Required setup: run `Copy-Item .env.example .env`, replace both placeholder passwords in all four places (the two variables and the `Password=` segment of each matching connection string), then start an isolated project. Only one scheduler Compose stack can bind the fixed loopback ports; stop any existing scheduler stack first, or reuse the running stack and substitute its project name in the commands.
- Start with `docker compose -p scheduler-t4-manual up -d --build --wait`; wait for `/health/ready` to return `200` (the first call may briefly return `503` while SQL warms; retry once). Compose enables telemetry for the API with `Scheduler__Telemetry__Enabled=true`, `Scheduler__Telemetry__OtlpEndpoint=http://collector:4317`, and `Scheduler__Telemetry__SamplingRatio=1.0`.
- Cleanup: there is no API delete operation. Use `Remove-T4Appointment` with the setup identity only for appointments created by this checklist, or use a fresh isolated project/volume for a clean rerun. Never grant direct appointment writes to the runtime identity. At the end, `docker compose -p scheduler-t4-manual down` keeps the named volume; use `down -v` only after confirming the project is isolated and no data is needed.

> [!NOTE]
> These checks supplement the executed automated and operational evidence in [[test/4-test|the ticket 4 test report]]. They do not replace it. Real SQL Server locking/permission and telemetry delivery checks remain automated; Swagger and Grafana success alone do not establish booking or concurrency correctness.

## Requirement coverage

| Requirement ID | Case IDs | Relevant automated evidence / remaining coverage |
| --- | --- | --- |
| S4-01 | TC-M03, TC-M06 | Serilog enricher/request-middleware/options tests, client-cancellation tests, Problem Details trace-ID tests, Compose JSON log correlation; human log inspection remains here. |
| S4-02 | TC-M03, TC-M04, TC-M05 | `TelemetryDeliveryTests` Tempo span inventory for HTTP, SQL, `availability.evaluate`, `booking.allocate`, `EXECUTE dbo.CreateAppointment`, and cache spans; human trace inspection remains here. |
| S4-03 | TC-M04, TC-M05, TC-M07, TC-M08, TC-M11 | Unit listener tests for counters/histograms/outcomes; Prometheus name/label/outcome checks; dashboard PromQL and zero-traffic tests; SQL dependency-error outcome. |
| S4-04 | TC-M01, TC-M02, TC-M10 | Compose/Collector/Prometheus validation, pinned images, Grafana provisioning API checks, deploy notes/setup instructions; visual dashboard inspection remains here. |
| S4-05 | TC-M06, TC-M07, TC-M08, TC-M11 | Sensitive-data and label assertions, Collector health filter, telemetry outage/recovery, Redis/SQL outage checks, client-cancellation handling; human outage/log inspection remains here. |
| S4-06 | TC-M10 | Unit/integration telemetry suites and direct Compose operational checks; human documentation inspection remains here. |
| AC4-01 | TC-M03 | Compose and automated log/Tempo correlation for a known trace ID. |
| AC4-02 | TC-M04, TC-M05 | Trace `1111…` span inventory and corrected conflict-trace expectation. |
| AC4-03 | TC-M01, TC-M03, TC-M04 | Prometheus/Tempo delivery through the Collector and Grafana provisioning. |
| AC4-04 | TC-M01, TC-M02 | Fresh-stack Grafana provisioning and zero-traffic PromQL checks; visual panel inspection remains here. |
| AC4-05 | TC-M04, TC-M05, TC-M07, TC-M11 | Booking/cache outcome series with bounded labels; dependency-error outcome. |
| AC4-06 | TC-M07, TC-M08, TC-M11 | Redis degradation, telemetry outage/recovery, and SQL dependency checks; readiness remains SQL/Redis-only. |
| AC4-07 | TC-M06 | Captured log/span/metric assertions with synthetic values; human inspection of the live stack remains here. |
| AC4-08 | TC-M10 | README/setup/deploy-note updates; human documentation inspection remains here. |
| T-13 Redis degradation signals | TC-M07 | Automated Redis pause test and Compose stop/start check; manual inspection supplements. |
| T-15 Observability wiring | TC-M01, TC-M03, TC-M04, TC-M08 | Delivery, backend queries, and log correlation; manual inspection supplements. |
| S4-V01 Telemetry outage and recovery | TC-M07, TC-M08, TC-M11 | Automated outage/recovery and Compose stop/start checks; SQL dependency portion. |
| S4-V02 Telemetry data contract | TC-M03, TC-M05, TC-M06 | Sensitive-data, label, and outcome assertions; manual inspection uses the live stack. |
| S4-V03 Provisioning and dashboard behavior | TC-M01, TC-M02 | Fresh-stack Grafana/PromQL tests; visual inspection remains here. |

## Test cases

### TC-M01: Fresh stack startup, readiness, and provisioned telemetry access

> [!NOTE]
> Covers: S4-04, AC4-03 (manual portion), AC4-04, T-15 (manual inspection), S4-V03 (prerequisite). Type: operational.

- Preconditions and synthetic data: Docker running; `.env` copied and both placeholder passwords replaced; no other scheduler stack bound to the loopback ports; repository root.
- [ ] **Step 1:** `docker compose -p scheduler-t4-manual config`
    - **Expected:** exit code `0`; the rendered configuration resolves `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, `ConnectionStrings__Setup`, `ConnectionStrings__Runtime`, and `ConnectionStrings__Cache`; no interpolation error; the API service has telemetry enabled and no telemetry health dependency.
- [ ] **Step 2:** `docker compose -p scheduler-t4-manual up -d --build --wait`
    - **Expected:** exit code `0`; SQL Server, Redis, Collector, Prometheus, Tempo, and Grafana start; one-shot `db-setup` exits successfully; API starts; no container restart loop.
- [ ] **Step 3:** Check liveness, readiness, and telemetry health:
    ```powershell
    Invoke-WebRequest http://127.0.0.1:8080/health/live | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:8080/health/ready | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:13133/ | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:9090/-/ready | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3200/ready | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3000/api/health | Select-Object StatusCode
    ```
    - **Expected:** all probes return `200`. Readiness JSON reports `database: Healthy` and `redis: Healthy`; telemetry is not a readiness check. Retry readiness once if startup SQL warm-up returns `503`.
- [ ] **Step 4:** Check provisioned data sources and dashboard:
    ```powershell
    Invoke-WebRequest http://127.0.0.1:3000/api/datasources/uid/prometheus | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3000/api/datasources/uid/tempo | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3000/api/dashboards/uid/scheduler-overview | Select-Object StatusCode
    ```
    - **Expected:** each returns `200`; the Prometheus and Tempo data-source UIDs exist; the dashboard UID is `scheduler-overview` with title `Unified Service Scheduler - Operations` and the expected panels.
- [ ] **Step 5:** Open `http://127.0.0.1:3000/d/scheduler-overview`.
    - **Expected:** the dashboard loads without manually creating a data source or dashboard; no Grafana login is required (anonymous Viewer); panels include request rate, HTTP p95 latency, 5xx error ratio, cache hit ratio, cache get failures, booking final outcomes, booking procedure attempts, booking allocation p95, cache operations, and correlation instructions.
- Cleanup: keep the stack running for the remaining cases.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M02: Zero-traffic dashboard behavior

> [!NOTE]
> Covers: S4-04, AC4-04, S4-V03; research TC4-10. Type: operational + visual inspection.

- Preconditions and synthetic data: TC-M01 completed; no business API request has been sent since the current stack started. Health and readiness probes are excluded from request logs, request metrics, and server spans, so they do not create business traffic. If business traffic already occurred, use a fresh isolated project/volume or wait for the dashboard's 15-minute window to clear; otherwise mark this case Blocked rather than claiming zero-traffic behavior.
- [ ] **Step 1:** Open `http://127.0.0.1:3000/d/scheduler-overview` with the default `now-15m` range and `10s` refresh.
    - **Expected:** request-rate, booking-outcome, procedure-attempt, cache-operation, and cache-failure panels show zero, no data, or no series; HTTP p95 and booking-allocation p95 show `No data`; the 5xx error ratio and cache hit ratio show `0%` via guarded denominators; no synthetic latency value is drawn.
- [ ] **Step 2:** Wait at least one refresh interval and recheck.
    - **Expected:** the same zero-traffic presentation; health probes still create no `route=~"/health/.*"` request series or `http_route=~"/health/.*"` latency series.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M03: Correlated booking request from JSON logs to Tempo

> [!NOTE]
> Covers: S4-01, S4-02, AC4-01, T-15, S4-V02; research TC4-02, TC4-03, TC4-14. Type: manual API + operational inspection.

- Preconditions and synthetic data: TC-M01 passed; `$startsAt` is a free future weekday slot; prepare `$trace = New-TraceContext`, `$body`, and the appointment URL. Use a free slot or a fresh isolated volume.
- [ ] **Step 1:** Send a booking with a known W3C trace context:
    ```powershell
    $trace = New-TraceContext
    $body = @"
    {"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$startsAt"}
    "@
    $response = Invoke-WebRequest -Uri $appointments -Method Post -ContentType 'application/json' -Headers @{ traceparent = $trace.Traceparent } -Body $body
    $response.StatusCode
    $response.Headers.Location
    $created = $response.Content | ConvertFrom-Json
    $created
    ```
    - **Expected:** `201 Created`; non-empty `Location`; body `id`, `dealershipId`, `customerId`, `vehicleId`, `serviceTypeId`, `technicianId`, `serviceBayId`, `startsAt`, `endsAt`, `durationMinutes`, `status`, `createdAt`; `startsAt` is UTC (`Z` form), `endsAt` is exactly 60 minutes later, `durationMinutes` is `60`, `status` is `Confirmed`, technician/bay IDs are non-empty; record `$created.id` as `$appointmentId`.
- [ ] **Step 2:** Retrieve the appointment by dealership and ID:
    ```powershell
    Invoke-RestMethod -Uri "$appointments/$($created.id)" | Format-List
    ```
    - **Expected:** `200`; all persisted fields match the POST response, including the same ID, UTC interval, 60-minute duration snapshot, status, and creation time.
- [ ] **Step 3:** Find the request event in the API JSON logs:
    ```powershell
    $logMatches = Get-T4Logs | Select-String -SimpleMatch $trace.TraceId
    $logMatches
    ```
    - **Expected:** one or more JSON lines; at least one has `EventName: "RequestCompleted"`, `Application: "unified-service-scheduler-api"`, a non-empty `ApplicationVersion`, `Environment: "Development"`, `TraceId` equal to the 32-lowercase-hex `$trace.TraceId`, a non-empty `SpanId`, `RequestMethod: "POST"`, `RouteTemplate: "/api/dealerships/{dealershipId}/appointments"`, `StatusCode: 201`, `StatusClass: "2xx"`, `Outcome: "none"`, and `ElapsedMilliseconds`. The request event carries the bounded no-code outcome for a successful 2xx response; the separate `scheduler.booking.outcomes{outcome="created"}` counter records the booking result. The line must not contain `RequestPath`, `RequestId`, `ConnectionId`, the raw URL, or the query string.
- [ ] **Step 4:** Query Tempo for the same trace ID:
    ```powershell
    (Invoke-WebRequest -Uri "$tempo/api/v2/traces/$($trace.TraceId)").StatusCode
    ```
    - **Expected:** `200`; the trace contains an HTTP server span for `POST /api/dealerships/{dealershipId}/appointments`, SQL `SELECT` spans for booking prechecks, a `booking.allocate` span, and an `EXECUTE dbo.CreateAppointment` SQL span. No SQL parameter values are present.
- [ ] **Step 5:** Open Grafana Explore, select the provisioned Tempo data source, and query the same 32-hex trace ID.
    - **Expected:** the same trace and spans are visible through Grafana; the trace ID and span IDs match the JSON log properties. Metric labels do not contain the trace ID.
- Cleanup: `Remove-T4Appointment -AppointmentId $created.id`; confirm the row is removed with the setup identity, or leave it and use a fresh volume for a clean rerun.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M04: Catalog and availability trace coverage with cache hit/miss metrics

> [!NOTE]
> Covers: S4-02, S4-03, AC4-02, AC4-05, T-15; research TC4-04, TC4-05, TC4-07. Type: manual API + operational inspection.

- Preconditions and synthetic data: TC-M01 passed; `$catalog`, `$availability`, `$startsAtAltQuery`, `$oilChange`; Redis key `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types`. Use `New-TraceContext` for each request.
- [ ] **Step 1:** Force a cold catalog read by deleting only the catalog key:
    ```powershell
    docker compose -p scheduler-t4-manual exec -T redis redis-cli DEL "scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types"
    $coldTrace = New-TraceContext
    Invoke-WebRequest -Uri $catalog -Headers @{ traceparent = $coldTrace.Traceparent } | Select-Object StatusCode
    ```
    - **Expected:** `DEL` returns `1` or `0`; the catalog returns `200` with the seeded service types, including Oil Change with `durationMinutes: 60`; the trace contains `cache.get` with `cache.result=miss`, SQL fallback `SELECT` spans, and `cache.set` with `cache.result=success`; Prometheus `scheduler_cache_operations_total{operation="get",result="miss"}` and `{operation="set",result="success"}` are present/increased.
- [ ] **Step 2:** Send a warm catalog request:
    ```powershell
    $warmTrace = New-TraceContext
    Invoke-WebRequest -Uri $catalog -Headers @{ traceparent = $warmTrace.Traceparent } | Select-Object StatusCode
    ```
    - **Expected:** `200`; the trace contains `cache.get` with `cache.result=hit`; no SQL fallback read is required for the catalog; Prometheus `scheduler_cache_operations_total{operation="get",result="hit"}` is present/increased.
- [ ] **Step 3:** Send an availability request:
    ```powershell
    $availabilityTrace = New-TraceContext
    Invoke-RestMethod -Uri "$availability?serviceTypeId=$oilChange&startsAt=$startsAtAltQuery" -Headers @{ traceparent = $availabilityTrace.Traceparent }
    ```
    - **Expected:** `200`; `available` is `true` for a free slot; `startsAt` is the UTC-normalized start, `endsAt` is exactly 60 minutes later, and `evaluatedAt` ends with `Z`; the trace contains an `availability.evaluate` span and SQL `SELECT` spans; no write span.
- [ ] **Step 4:** Inspect the custom metric label keys:
    ```powershell
    Query-Prometheus 'scheduler_http_requests_total' | ForEach-Object { $_.metric.PSObject.Properties.Name } | Sort-Object -Unique
    Query-Prometheus 'scheduler_cache_operations_total' | ForEach-Object { $_.metric.PSObject.Properties.Name } | Sort-Object -Unique
    ```
    - **Expected:** HTTP labels are bounded to `route`, `method`, `status_class`, `outcome` plus Prometheus resource labels (`job`, `instance`, `deployment_environment_name`, `service_version`); cache labels are `operation` and `result` plus resource labels. No appointment/customer/vehicle/dealership IDs, trace IDs, raw paths, or query strings appear.
- Cleanup: optionally delete the catalog key to restore a cold state: `docker compose -p scheduler-t4-manual exec -T redis redis-cli DEL "scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types"`.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M05: Booking-conflict trace and bounded outcome metrics

> [!NOTE]
> Covers: S4-02, S4-03, AC4-02, AC4-05, S4-V02; research TC4-06, TC4-07. Type: manual API + operational inspection.

- Preconditions and synthetic data: use `$startsAtAlt` (13:00 Saigon local on the prepared weekday), which is free and not used by TC-M03. The single active Saigon technician/bay means a second identical booking has no eligible pair.
- [ ] **Step 1:** Create the first booking at `$startsAtAlt` with a trace context:
    ```powershell
    $firstTrace = New-TraceContext
    $firstBody = @"
    {"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$startsAtAlt"}
    "@
    $firstResponse = Invoke-WebRequest -Uri $appointments -Method Post -ContentType 'application/json' -Headers @{ traceparent = $firstTrace.Traceparent } -Body $firstBody
    $first = $firstResponse.Content | ConvertFrom-Json
    $first
    ```
    - **Expected:** `201 Created`; record `$first.id` as `$appointmentId`; the response interval is the UTC form of 13:00-14:00 Saigon local with `durationMinutes: 60`.
- [ ] **Step 2:** Send the identical booking again with a new trace context:
    ```powershell
    $conflictTrace = New-TraceContext
    curl.exe -s -i -X POST $appointments -H 'Content-Type: application/json' -H "traceparent: $($conflictTrace.Traceparent)" -d $firstBody
    ```
    - **Expected:** `409 Conflict` with `application/problem+json`; body `status: 409`, `code: "capacity_unavailable"`, a non-empty `traceId` equal to `$conflictTrace.TraceId` (32 lowercase hex), and a detail explaining that no eligible technician/bay pair was reserved. No `500` or `503`; no new appointment.
- [ ] **Step 3:** Confirm the first appointment still returns unchanged and the conflict did not overwrite it:
    ```powershell
    Invoke-RestMethod -Uri "$appointments/$appointmentId" | Format-List
    ```
    - **Expected:** `200`; the original ID, interval, duration, technician, bay, status, and creation time are unchanged.
- [ ] **Step 4:** Query Prometheus for booking outcomes:
    ```powershell
    Query-Prometheus 'scheduler_booking_outcomes_total{outcome="created"}'
    Query-Prometheus 'scheduler_booking_outcomes_total{outcome="capacity_unavailable"}'
    ```
    - **Expected:** both series are present (or increased by this case) with only the bounded `outcome` label plus resource labels; no entity IDs or trace IDs.
- [ ] **Step 5:** Query Tempo for the conflict trace:
    ```powershell
    (Invoke-WebRequest -Uri "$tempo/api/v2/traces/$($conflictTrace.TraceId)").StatusCode
    ```
    - **Expected:** `200`; the trace contains an HTTP server span for the POST and SQL precheck spans. Per the recorded expectation, a conflict filtered by the overlap precheck may have no `booking.allocate` span; if the conflict reaches allocation, `booking.allocate` is present. Record which form was observed.
- [ ] **Step 6:** Inspect the successful booking trace from Step 1 for procedure-attempt and allocation-duration metrics:
    ```powershell
    Query-Prometheus 'scheduler_booking_procedure_attempts_total'
    Query-Prometheus 'scheduler_booking_allocation_duration_seconds_count'
    ```
    - **Expected:** series exist with bounded `result`/`outcome` labels; internal attempts are distinguishable from the final HTTP booking outcome; the allocation-duration histogram has observations after the successful booking and the dashboard's booking-allocation p95 panel shows data after refresh; no retry or locking behavior is changed.
- Cleanup: remove the first appointment with `Remove-T4Appointment -AppointmentId $appointmentId`; the conflict created no row.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M06: Sensitive-data exclusion and bounded telemetry attributes

> [!NOTE]
> Covers: S4-05, AC4-07, S4-V02; research TC4-08. Type: operational inspection + manual API.

- Preconditions and synthetic data: TC-M03 and TC-M04 produced logs, traces, and metrics; a synthetic probe marker such as `SYNTHETIC-SENSITIVE-001`; do not use real customer data or credentials.
- [ ] **Step 1:** Send a catalog request with a synthetic query-string probe:
    ```powershell
    $probeTrace = New-TraceContext
    Invoke-WebRequest -Uri "$catalog?probe=SYNTHETIC-SENSITIVE-001" -Headers @{ traceparent = $probeTrace.Traceparent } | Select-Object StatusCode
    ```
    - **Expected:** `200`; the unknown query parameter is ignored. The probe value must not appear in API logs, request-event properties, Tempo span attributes, or metric labels.
- [ ] **Step 2:** Search the captured API logs for forbidden properties and synthetic identifiers:
    ```powershell
    Get-T4Logs | Select-String -Pattern 'RequestPath|RequestId|ConnectionId|SYNTHETIC-SENSITIVE-001|b1000000-|c1000000-'
    ```
    - **Expected:** no matches for framework scope properties, the probe value, the synthetic vehicle ID, or the synthetic customer ID. Route templates may contain placeholders such as `{dealershipId}`, but not the actual GUID.
- [ ] **Step 3:** Inspect Prometheus metric labels from TC-M04 and TC-M05.
    - **Expected:** only bounded route/method/status/outcome/result/operation labels plus resource labels; no trace IDs, entity IDs, VINs, raw URLs, or query strings.
- [ ] **Step 4:** Inspect an HTTP server span and a SQL span in Tempo/Grafana from TC-M03 or TC-M04.
    - **Expected:** the HTTP span has `http.route`, method, and status; `url.path`, `url.query`, `user_agent.original`, server address/port, and original method are absent. SQL spans have `db.system.name`, `db.namespace`, operation/query text, and the stored-procedure name; no parameter values are present. Cache spans have `cache.name`, `cache.operation`, `cache.budget_ms`, and `cache.result`; no cache key or dealership ID.
- [ ] **Step 5:** Inspect the Problem Details response from TC-M05 and any `400`/`404` response.
    - **Expected:** `code` and `traceId` extensions are present; `traceId` is the 32-lowercase-hex W3C trace ID when an Activity exists; no stack trace, connection string, or sensitive payload appears.
- Cleanup: none; the synthetic probe created no business row.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M07: Redis outage degradation with catalog fallback and booking correctness

> [!NOTE]
> Covers: S4-05, AC4-06, T-13, S4-V01 (Redis portion); research TC4-11, TC4-13. Type: operational + manual API.

- Preconditions and synthetic data: stack running; a free future weekday slot for a booking (`$startsAtAlt` after TC-M05 cleanup); a known catalog URL; current appointment count if desired. Redis service name is `redis`.
- [ ] **Step 1:** `docker compose -p scheduler-t4-manual stop redis`
    - **Expected:** Redis stops; SQL Server, the API, and telemetry services remain running; the API does not restart or fail startup.
- [ ] **Step 2:** Check health:
    ```powershell
    Invoke-WebRequest http://127.0.0.1:8080/health/live | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:8080/health/ready | Select-Object StatusCode
    ```
    - **Expected:** liveness `200`; readiness `200` with `database: Healthy` and `redis: Degraded`; telemetry is not a readiness check. Retry readiness once if SQL warms.
- [ ] **Step 3:** Request the catalog with a trace context:
    ```powershell
    $redisTrace = New-TraceContext
    Invoke-RestMethod -Uri $catalog -Headers @{ traceparent = $redisTrace.Traceparent }
    ```
    - **Expected:** `200`; the catalog returns the seeded service types via SQL fallback within the bounded cache budget; no Redis error is surfaced to the client; the trace contains a failed `cache.get` and SQL fallback spans.
- [ ] **Step 4:** Query Prometheus and inspect logs:
    ```powershell
    Query-Prometheus 'scheduler_cache_operations_total{operation="get",result="failure"}'
    Get-T4Logs | Select-String -Pattern 'CacheOperation|CacheResult'
    ```
    - **Expected:** a `get/failure` series is present (or increased); a JSON `CacheOperation` event reports the bounded operation, budget, `CacheName: "service_catalog"`, and `CacheResult: "failure"`; no cache key or dealership ID is logged.
- [ ] **Step 5:** Create and retrieve a booking at a free slot:
    ```powershell
    $redisBooking = New-TraceContext
    $redisBody = @"
    {"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$startsAtAlt"}
    "@
    $redisResponse = Invoke-WebRequest -Uri $appointments -Method Post -ContentType 'application/json' -Headers @{ traceparent = $redisBooking.Traceparent } -Body $redisBody
    $redisCreated = $redisResponse.Content | ConvertFrom-Json
    Invoke-RestMethod -Uri "$appointments/$($redisCreated.id)" | Format-List
    ```
    - **Expected:** `201` and retrieval `200` with the stored values; booking correctness does not depend on Redis; readiness remains `Degraded`/`200`.
- [ ] **Step 6:** `docker compose -p scheduler-t4-manual start redis`; then check readiness and the catalog.
    - **Expected:** readiness returns to `200` with `redis: Healthy` after a short recovery interval; the catalog remains `200`; the cache may repopulate on the next request.
- Cleanup: ensure Redis is started; `Remove-T4Appointment -AppointmentId $redisCreated.id`.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M08: Telemetry backend outage and bounded recovery

> [!NOTE]
> Covers: S4-05, AC4-06, S4-V01, T-15; research TC4-11, TC4-12. Type: operational + manual API.

- Preconditions and synthetic data: stack running; a free future weekday slot for a booking (`$startsAtAlt2`); telemetry services `collector`, `prometheus`, `tempo`, and `grafana`; a known trace context for the post-recovery request.
- [ ] **Step 1:** Stop all telemetry services while leaving SQL Server, Redis, and the API running:
    ```powershell
    docker compose -p scheduler-t4-manual stop collector prometheus tempo grafana
    ```
    - **Expected:** telemetry services stop; the API, SQL Server, and Redis remain running; the API does not restart.
- [ ] **Step 2:** Check API behavior during the outage:
    ```powershell
    Invoke-WebRequest http://127.0.0.1:8080/health/live | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:8080/health/ready | Select-Object StatusCode
    Invoke-RestMethod -Uri $catalog | Select-Object -First 1
    ```
    - **Expected:** liveness and readiness `200`; readiness reports only `database` and `redis` (no telemetry check); catalog `200`. Exporters run asynchronously and may drop telemetry after bounded queues/timeouts; no request waits on telemetry.
- [ ] **Step 3:** Create and retrieve a booking with telemetry still stopped:
    ```powershell
    $outageTrace = New-TraceContext
    $outageBody = @"
    {"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$startsAtAlt2"}
    "@
    $outageResponse = Invoke-WebRequest -Uri $appointments -Method Post -ContentType 'application/json' -Headers @{ traceparent = $outageTrace.Traceparent } -Body $outageBody
    $outageCreated = $outageResponse.Content | ConvertFrom-Json
    Invoke-RestMethod -Uri "$appointments/$($outageCreated.id)" | Format-List
    ```
    - **Expected:** `201` and retrieval `200`; the booking persists; no `500` due to telemetry; readiness remains `200`; no telemetry health dependency exists.
- [ ] **Step 4:** Restart telemetry services and wait for readiness:
    ```powershell
    docker compose -p scheduler-t4-manual start collector prometheus tempo grafana
    Invoke-WebRequest http://127.0.0.1:13133/ | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:9090/-/ready | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3200/ready | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:3000/api/health | Select-Object StatusCode
    ```
    - **Expected:** all telemetry services return `200` after startup; no manual provisioning is needed.
- [ ] **Step 5:** Generate new catalog and booking traffic with a known trace context and poll the backends within the documented bounded window:
    ```powershell
    $recoveryTrace = New-TraceContext
    Invoke-WebRequest -Uri $catalog -Headers @{ traceparent = $recoveryTrace.Traceparent } | Select-Object StatusCode
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Seconds 2
        try { $traceStatus = (Invoke-WebRequest -Uri "$tempo/api/v2/traces/$($recoveryTrace.TraceId)" -ErrorAction Stop).StatusCode } catch { $traceStatus = 0 }
    } while ($traceStatus -ne 200 -and (Get-Date) -lt $deadline)
    $traceStatus
    Query-Prometheus 'scheduler_http_requests_total'
    ```
    - **Expected:** within the configured/observed delivery window (the plan default is 30 seconds), the new trace is queryable in Tempo and the request metric is present in Prometheus. Telemetry that was dropped during the outage is not backfilled; only new traffic appears. If the trace is absent after 30 seconds, record the elapsed time, backend health, and troubleshooting checks; do not treat a sampling-related absence as delivery success.
- Cleanup: ensure all telemetry services are running; `Remove-T4Appointment -AppointmentId $outageCreated.id`.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M10: Documentation, startup instructions, and no-database/no-cache-contract regression

> [!NOTE]
> Covers: S4-04, S4-06, AC4-08, P-01/P-02 constraints; research TC4-18. Type: documentation + regression inspection.

- Preconditions and synthetic data: repository root; the stack started in TC-M01; the current ticket 4 working-tree change.
- [ ] **Step 1:** Compare the documented access and startup instructions with the observed stack:
    - **Expected:** `README.md`, `docs/setup-guide.md`, and [[deploy-notes/4-deploy-notes|the deploy notes]] describe the actual API `8080`, Grafana `3000`, Prometheus `9090`, Tempo `3200`, and Collector `13133` endpoints; the dashboard UID `scheduler-overview`; the read-only provisioning behavior; retention/resource limits; and the outage/recovery procedure. No instruction requires a nonexistent script or manual dashboard creation.
- [ ] **Step 2:** Confirm no database, migration, procedure, permission, or cache-contract change was introduced by ticket 4:
    ```powershell
    git status --porcelain
    git diff --stat
    git diff --cached --stat
    ```
    - **Expected:** no changes under `src/ServiceScheduler.Api/Migrations/`; no migration, seed, procedure, runtime grant, or permission change; `Scheduler:Cache:ServiceCatalog:AbsoluteTtl` remains `00:10:00`; the cache key pattern remains `scheduler:v1:dealership:{id}:service-types`; cache budgets remain 250 ms per operation and 500/750 ms cumulative. Ticket 4 changes are staged in the working tree, so inspect both the working-tree and cached diffs. Telemetry is instrumentation and asynchronous export only.
- [ ] **Step 3:** Recheck readiness with SQL/Redis healthy and telemetry running after TC-M08; optionally stop the telemetry services again first:
    - **Expected:** readiness reports `database` and `redis` only and returns `200`; telemetry is not a readiness dependency; liveness remains dependency-free.
- [ ] **Step 4:** Open the Swagger UI and OpenAPI document:
    ```powershell
    Invoke-WebRequest http://127.0.0.1:8080/openapi/v1.json | Select-Object StatusCode
    ```
    - **Expected:** `200`; the five business operations are present; Problem Details documents `code` and the W3C `traceId` semantics; the ticket 4 change adds no new business route or request field.
- Cleanup: none; this case is read-only.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M11: SQL dependency classification and bounded dependency-error telemetry (optional)

> [!NOTE]
> Covers: S4-03, S4-05, AC4-05, AC4-06, S4-V01 (SQL portion); research TC4-19. Type: operational + manual API. Run last because it stops SQL Server for the whole API.

- Preconditions and synthetic data: stack healthy; a free future weekday slot; the `$body` built in TC-M03 (or rebuild it with `$startsAt`); the API and telemetry services running. Do not run this case while other work depends on the local SQL Server.
- [ ] **Step 1:** Stop SQL Server:
    ```powershell
    docker compose -p scheduler-t4-manual stop sqlserver
    ```
    - **Expected:** SQL Server stops; the API, Redis, and telemetry services remain running; the API does not restart.
- [ ] **Step 2:** Check liveness, readiness, and a catalog request:
    ```powershell
    $sqlTrace = New-TraceContext
    Invoke-WebRequest http://127.0.0.1:8080/health/live | Select-Object StatusCode
    Invoke-WebRequest http://127.0.0.1:8080/health/ready | Select-Object StatusCode
    curl.exe -s -i "$catalog" -H "traceparent: $($sqlTrace.Traceparent)"
    ```
    - **Expected:** liveness `200`; readiness `503` with `database: Unhealthy`; the catalog returns `503` `dependency_unavailable` Problem Details with the 32-hex `traceId` equal to `$sqlTrace.TraceId` (if an Activity exists), not a `409` capacity result and not a `500`.
- [ ] **Step 3:** Attempt a booking at a free slot:
    ```powershell
    curl.exe -s -i -X POST $appointments -H 'Content-Type: application/json' -H "traceparent: $((New-TraceContext).Traceparent)" -d $body
    Query-Prometheus 'scheduler_http_requests_total{outcome="dependency_unavailable"}'
    ```
    - **Expected:** `503 dependency_unavailable`; no appointment is stored; after the metric export interval (up to 5 s), the request metric records `outcome="dependency_unavailable"`. Per the recorded nuance, the controller never returns a booking result, so `scheduler.booking.outcomes` does not record this escaped dependency path; the request metric and dependency event represent it. Do not retry indefinitely or grant direct writes.
- [ ] **Step 4:** Restart SQL Server and verify recovery:
    ```powershell
    docker compose -p scheduler-t4-manual start sqlserver
    $recoveredTrace = New-TraceContext
    curl.exe -s -i -X POST $appointments -H 'Content-Type: application/json' -H "traceparent: $($recoveredTrace.Traceparent)" -d $body
    ```
    - **Expected:** SQL Server becomes healthy; readiness returns to `200` after warm-up; the booking returns `201` (the failed request created no row, so the same slot is free; choose another free slot if it was used by another case); record the created appointment ID for cleanup. Telemetry resumes through the Collector without API restart.
- Cleanup: ensure SQL Server is running; delete the successful booking with `Remove-T4Appointment -AppointmentId <created-id>` if one was created.
- Execution status: Not run.
- Observed result / evidence: —

## Execution record

| Date / time and zone | Revision | Case / actual action | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| — | `92a81ef` + staged ticket 4 working tree | Checklist generated 2026-11-02; no manual case executed. | All cases are Not run; automated and direct Compose evidence is recorded in [[test/4-test\|the ticket 4 test report]]. | Not run | This file |

No manual case is marked Passed by checklist generation. Record the actual date/time, revision, environment, action, observed result, and evidence after a human executes each case. Preserve prior records when updating the file.

## Issues and follow-up

- Human visual Grafana inspection: TC-M01, TC-M02, TC-M03, TC-M04, TC-M05, TC-M06, TC-M07, and TC-M08 include visual or live-stack inspection that automated tests do not replace. None has been executed; ticket acceptance is not claimed.
- Automated evidence: [[test/4-test|the ticket 4 test report]] records 188 unit and 175 integration tests passed (including 53 telemetry unit tests and 16 telemetry integration tests), build/format success, pinned Collector/Prometheus/Tempo/Grafana startup and provisioning, delivery through the Collector, span coverage, metric contract checks, sensitive-data exclusion, Redis degradation, telemetry outage/recovery, and SQL dependency classification. The review reproduced 188 unit and 175 integration tests on 2026-11-02. These results supplement but do not replace manual execution.
- Documented nuances to preserve during execution: a capacity conflict filtered by the overlap precheck may have HTTP/SQL spans but no `booking.allocate` span; an unreachable SQL runtime is represented by `scheduler.http.requests{outcome="dependency_unavailable"}` because the controller records `scheduler.booking.outcomes` only for returned results; the cache hit ratio is transport-level and treats a corrupt cached payload as a `get` hit before fallback; health traffic is excluded from request logs/metrics/spans and the Collector drops health latency datapoints; dropped telemetry is not backfilled; the 30-second delivery window is an observation default, not a measured guarantee.
- Automated-only boundaries: client-cancellation classification (`client_cancelled`/499), unmapped procedure-attempt `unexpected` recording, and direct SQL locking/retry behavior are covered by automated tests or the recorded operational run; no manual case injects those failures.
- Review non-blocking observation: ASP.NET Core and SqlClient spans use `RecordException = true`, and the executed data-contract tests inspect span attributes rather than span events. During TC-M06, TC-M07, or TC-M11, if an error trace is inspected, confirm exception events do not contain sensitive payloads; record a follow-up rather than treating attribute-only checks as complete event coverage.
- If Docker, the pinned images, or a browser for Grafana is unavailable, mark the affected operational or visual cases Blocked with the reason and cite the automated evidence. Do not mark them Passed. If a required automated check were stale or failed, implementation would need rerunning before this checklist could be used as acceptance evidence.
- Cleanup: no API delete exists; use `Remove-T4Appointment` only for this checklist's synthetic rows through the setup identity, or use a fresh isolated project/volume. Do not grant direct appointment writes to `scheduler_app`, do not flush the whole Redis database, and do not record passwords, connection strings, request bodies, VINs, or customer contact data in the execution record.
