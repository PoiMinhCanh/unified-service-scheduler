---
title: "2 - Manual Tests"
tags: [test, manual, service-scheduler]
ticket: 2
status: not-run
---

# 2 - Manual tests

## Scope and prerequisites

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research, plan, and primary test report: [[research/2/00-overview|Ticket 2 research]], [[plans/2/README|implementation plan]], [[test/2-test|ticket 2 test report]].
- Reviewed revision and implementation scope: checkout HEAD `3f8ab1e` plus the ticket 2 working-tree changes; the 2026-11-01 scope amendment requires interactive Swagger documentation and automated API tests and retires the standalone demo script.
- Requirement IDs: S2-01-S2-06; AC2-01-AC2-07; T-02-T-04, T-10-T-13; project A-02 and P-02/P-04.
- Exclusions: booking confirmation/retrieval, the booking procedure, and booking concurrency (ticket 3); Serilog/OpenTelemetry/Prometheus/Tempo/Grafana (ticket 4); authentication, admin CRUD, cancellation/rescheduling, idempotency, vehicle-overlap protection, cache stampede protection, and a custom frontend.
- Environment and API base URL: local Docker Compose stack; API `http://127.0.0.1:8080`; Swagger UI `http://127.0.0.1:8080/swagger/index.html`; OpenAPI `http://127.0.0.1:8080/openapi/v1.json`; no HTTPS and no login in the initial local demo.
- Command shell: the examples use Windows PowerShell and `curl.exe`; run them from the repository root.
- Dependency versions and migration/seed baseline: .NET SDK `10.0.403`; Docker `25.0.3`; Compose `v2.24.6-desktop.1`; SQL Server 2022 CU23; Redis `7.4.12-alpine`; migration `20261031092016_InitialCreate`; no ticket 2 migration. Setup seeds weekday shifts for a 28-day horizon from the setup time.
- Dealership time zones: Saigon `Asia/Ho_Chi_Minh` (UTC+7, no DST); London `Europe/London`.
- Synthetic seed identifiers:

| Entity | Identifier | Notes |
| --- | --- | --- |
| Saigon dealership | `a1000000-0000-0000-0000-000000000001` | `Asia/Ho_Chi_Minh`; weekday 09:00-17:00 opening |
| London dealership | `a1000000-0000-0000-0000-000000000002` | `Europe/London`; weekday 09:00-17:00 opening |
| Saigon Oil Change | `51000000-0000-0000-0000-000000000001` | 60 minutes |
| Saigon Brake Service | `51000000-0000-0000-0000-000000000002` | 120 minutes |
| London Annual Service | `51000000-0000-0000-0000-000000000003` | 90 minutes |

- No appointment is seeded; availability is advisory and creates no reservation.
- Future-time preparation: run this PowerShell snippet and substitute the printed values. The next weekday is at least two days ahead and within the 28-day seeded horizon when the stack is current.

```powershell
$date = (Get-Date).Date.AddDays(2)
while ($date.DayOfWeek -in @('Saturday','Sunday')) { $date = $date.AddDays(1) }
$saigonStart = ('{0:yyyy-MM-dd}T10:00:00+07:00' -f $date).Replace('+','%2B')
$lateStart = ('{0:yyyy-MM-dd}T18:00:00+07:00' -f $date).Replace('+','%2B')
$utcStart = '{0:yyyy-MM-dd}T03:00:00Z' -f $date
$saturday = (Get-Date).Date.AddDays(1)
while ($saturday.DayOfWeek -ne 'Saturday') { $saturday = $saturday.AddDays(1) }
$weekendStart = ('{0:yyyy-MM-dd}T10:00:00+07:00' -f $saturday).Replace('+','%2B')
$offsetFree = '{0:yyyy-MM-dd}T10:00:00' -f $date
$availability = 'http://127.0.0.1:8080/api/dealerships/a1000000-0000-0000-0000-000000000001/availability'
$saigonCatalog = 'http://127.0.0.1:8080/api/dealerships/a1000000-0000-0000-0000-000000000001/service-types'
$oilChange = '51000000-0000-0000-0000-000000000001'
$brakeService = '51000000-0000-0000-0000-000000000002'
$key = 'scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types'
Write-Output "Saigon weekday: $saigonStart"
Write-Output "Saigon late: $lateStart"
Write-Output "UTC equivalent: $utcStart"
Write-Output "Saigon Saturday: $weekendStart"
Write-Output "Offset-free: $offsetFree"
```

- If the printed weekday falls outside the seeded 28-day horizon, rerun setup with `docker compose down` and then `docker compose up -d --wait`; both commands keep the named SQL volume, and setup re-seeds the future shifts.
- Required setup: `Copy-Item .env.example .env`, replace the placeholder passwords, then `docker compose up -d --wait`; wait for `/health/ready` to return `200`. Do not record credentials.
- First readiness after an API restart can be `Unhealthy` while the SQL check warms; retry once before treating it as a failure.
- Cleanup: leave SQL/Redis running or run `docker compose down` (keeps the named SQL volume). A cache key expires after the 10-minute TTL or can be removed with `redis-cli DEL`.

## Requirement coverage

| Requirement ID | Case IDs | Relevant automated evidence / remaining coverage |
| --- | --- | --- |
| S2-01, AC2-01 | TC-M01, TC-M02, TC-M11 | TC2-01-TC2-08 and TC2-33 in [[test/2-test\|the test report]]; manual Swagger checks supplement them. |
| S2-02, AC2-02, AC2-03, T-10 | TC-M03, TC-M04, TC-M05 | TC2-12-TC2-14, TC2-22, TC2-23; DST is automated-only. |
| S2-03, AC2-02, T-02-T-04, T-11 | TC-M03, TC-M05, TC-M06, TC-M07 | TC2-15-TC2-21, TC2-24, TC2-25; real SQL conflict/scope tests are automated. |
| S2-04, AC2-06 | TC-M06, TC-M07, TC-M10 | TC2-26-TC2-29, TC2-34; sanitized `500` is automated-only. |
| S2-05, AC2-04, AC2-05, T-12, T-13 | TC-M08, TC-M09, TC-M10, TC-M13 | TC2-02, TC2-05-TC2-11, TC2-30-TC2-32; real Redis outage/stall tests are automated. |
| S2-06, AC2-07 | TC-M11 | TC2-33; TC2-35 is retired. |
| AC2-03, T-04 (read-only) | TC-M12 | TC2-21; the SQL appointment-count proof is automated. |

## Test cases

### TC-M01: Dealership discovery contract and ordering

> [!NOTE]
> Covers: S2-01, AC2-01. Type: manual API.

- Preconditions and synthetic data: stack running; seeded dealerships.
- [ ] **Step 1:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships"`
    - **Expected:** `200`; `content-type: application/json`; JSON array.
- [ ] **Step 2:** Inspect the array.
    - **Expected:** two objects with `id`, `name`, and `timeZoneId`; ordered by name: London Riverside Motors (`a1000000-0000-0000-0000-000000000002`, `Europe/London`) then Saigon Central Motors (`a1000000-0000-0000-0000-000000000001`, `Asia/Ho_Chi_Minh`); no extra fields.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M02: Dealership-scoped catalog contract and ordering

> [!NOTE]
> Covers: S2-01, AC2-01, T-11. Type: manual API.

- Preconditions and synthetic data: stack running; seeded catalogs.
- [ ] **Step 1:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/a1000000-0000-0000-0000-000000000001/service-types"`
    - **Expected:** `200`; array of `{id, name, durationMinutes}`; Brake Service (`51000000-0000-0000-0000-000000000002`, 120) then Oil Change (`51000000-0000-0000-0000-000000000001`, 60); no Annual Service.
- [ ] **Step 2:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/a1000000-0000-0000-0000-000000000002/service-types"`
    - **Expected:** `200`; Annual Service (`51000000-0000-0000-0000-000000000003`, 90) only; no Saigon services.
- [ ] **Step 3:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/00000000-0000-0000-0000-000000000000/service-types"`
    - **Expected:** `404`; Problem Details with `code=not_found` and a non-empty `traceId`.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M03: Availability success with SQL duration

> [!NOTE]
> Covers: S2-02, S2-03, AC2-02, AC2-03, T-02, T-03. Type: manual API.

- Preconditions and synthetic data: `$saigonStart` printed by the preparation snippet (next weekday 10:00 Saigon local, URL-encoded `%2B07:00`).
- [ ] **Step 1:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`
    - **Expected:** `200`; `available: true`; `dealershipId` and `serviceTypeId` echoed; `startsAt`, `endsAt`, and `evaluatedAt` end with `Z`; `endsAt` is exactly 60 minutes after `startsAt`; `evaluatedAt` is the current UTC instant; no technician or bay identifiers.
- [ ] **Step 2:** `curl.exe -s -i "${availability}?serviceTypeId=${brakeService}&startsAt=${saigonStart}"`
    - **Expected:** `200`; `endsAt` is exactly 120 minutes after `startsAt`; the same six fields are present.
- Cleanup: none; the request is read-only.
- Execution status: Not run.
- Observed result / evidence: —
- Note: if the result is `available:false`, the chosen start may be outside the seeded shift/opening horizon or the resources may be unavailable; choose another future weekday and confirm setup ran recently.

### TC-M04: Equivalent explicit-offset and UTC instants

> [!NOTE]
> Covers: S2-02, AC2-02, T-10. Type: manual API.

- Preconditions and synthetic data: `$saigonStart` and `$utcStart` identify the same instant (`10:00+07:00` = `03:00Z`).
- [ ] **Step 1:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`
    - **Expected:** `200`; the response `startsAt` is normalized to UTC and ends with `Z` (for example `...T03:00:00Z`).
- [ ] **Step 2:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${utcStart}"`
    - **Expected:** the same `startsAt`, `endsAt`, and `available` value as Step 1; `evaluatedAt` may differ only by the real clock advancing.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M05: Valid request with no eligible resources

> [!NOTE]
> Covers: S2-02, S2-03, AC2-02, A-02, T-03. Type: manual API.

- Preconditions and synthetic data: `$weekendStart` is the next Saturday at 10:00 Saigon local; the seeder adds weekday shifts only.
- [ ] **Step 1:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${weekendStart}"`
    - **Expected:** `200`; `available:false`; the same `dealershipId`, `serviceTypeId`, `startsAt`, `endsAt`, and `evaluatedAt` fields; `endsAt` = start + 60 minutes; no error and no reservation.
- [ ] **Step 2:** Optional: `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${lateStart}"`
    - **Expected:** `200` `available:false` because the interval is outside the 09:00-17:00 opening hours.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M06: Validation errors (`400`)

> [!NOTE]
> Covers: S2-02, S2-04, AC2-06, T-10. Type: manual API.

- Preconditions and synthetic data: `$offsetFree` and `$saigonStart` from the preparation snippet.
- [ ] **Step 1:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${offsetFree}"` (no `Z` or explicit offset).
    - **Expected:** `400`; Problem Details with `code=validation_error`; non-empty `traceId`; no stack trace, password, or connection string.
- [ ] **Step 2:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=2020-01-01T00:00:00Z"`
    - **Expected:** `400` `validation_error`.
- [ ] **Step 3:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/not-a-guid/service-types"`
    - **Expected:** `400` `validation_error`; not a route-selected `404`.
- [ ] **Step 4:** `curl.exe -s -i "${availability}?startsAt=${saigonStart}"` (missing `serviceTypeId`).
    - **Expected:** `400` `validation_error`.
- [ ] **Step 5:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=not-a-timestamp"` and `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=2030-01-01"`.
    - **Expected:** both `400` `validation_error`.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —
- Note: use `curl.exe` or PowerShell error handling to read the status and body; `Invoke-RestMethod` throws on `4xx`.

### TC-M07: Not-found and dealership-scope errors (`404`)

> [!NOTE]
> Covers: S2-01, S2-03, S2-04, T-11. Type: manual API.

- Preconditions and synthetic data: seeded Saigon/London IDs; unknown UUID `00000000-0000-0000-0000-000000000000`.
- [ ] **Step 1:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/00000000-0000-0000-0000-000000000000/service-types"` and `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships/00000000-0000-0000-0000-000000000000/availability?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`.
    - **Expected:** both `404`; `code=not_found`; non-empty `traceId`.
- [ ] **Step 2:** `curl.exe -s -i "${availability}?serviceTypeId=00000000-0000-0000-0000-000000000000&startsAt=${saigonStart}"`
    - **Expected:** `404` `not_found`.
- [ ] **Step 3:** `curl.exe -s -i "${availability}?serviceTypeId=51000000-0000-0000-0000-000000000003&startsAt=${saigonStart}"`
    - **Expected:** `404` `not_found`; no availability result.
- [ ] **Step 4:** Optional: `curl.exe -s -i "http://127.0.0.1:8080/api/unknown"`.
    - **Expected:** `404` `not_found` Problem Details.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M08: Catalog cache miss, hit, and explicit invalidation

> [!NOTE]
> Covers: S2-05, AC2-04, T-12. Type: manual API + Redis.

- Preconditions and synthetic data: stack running; key `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types`.
- [ ] **Step 1:** `docker compose exec -T redis redis-cli DEL "$key"`; then `docker compose exec -T redis redis-cli EXISTS "$key"`.
    - **Expected:** `DEL` returns `1` or `0`; `EXISTS` returns `0`.
- [ ] **Step 2:** `curl.exe -s -i "$saigonCatalog"`; then `docker compose exec -T redis redis-cli EXISTS "$key"`.
    - **Expected:** `200` ordered catalog; `EXISTS` returns `1`.
- [ ] **Step 3:** `docker compose exec -T redis redis-cli GET "$key"`
    - **Expected:** JSON array of `{id, name, durationMinutes}` in the documented order; the key contains the `v1` segment.
- [ ] **Step 4:** `docker compose exec -T redis redis-cli DEL "$key"`; `EXISTS` returns `0`; `curl.exe -s -i "$saigonCatalog"`; `EXISTS` returns `1`.
    - **Expected:** explicit invalidation forces a fresh SQL read and repopulation.
- Cleanup: optional `DEL`; the key otherwise expires after the 10-minute TTL.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M09: Redis outage fallback and recovery

> [!NOTE]
> Covers: S2-05, AC2-05, AC2-06, T-13. Type: operational.

- Preconditions and synthetic data: stack running; `$saigonStart`.
- [ ] **Step 1:** `docker compose stop redis`
    - **Expected:** Redis stops; the API remains running.
- [ ] **Step 2:** `curl.exe -s -i http://127.0.0.1:8080/health/ready` and `curl.exe -s -i http://127.0.0.1:8080/health/live`
    - **Expected:** readiness `200` with `redis` reported `Degraded` and `database` `Healthy`; liveness `200`. Retry once if the first readiness call warms the SQL check.
- [ ] **Step 3:** `curl.exe -s -i "${saigonCatalog}"` and `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`
    - **Expected:** both `200`; the catalog falls back to SQL; availability remains correct; no Redis error is surfaced.
- [ ] **Step 4:** `docker compose start redis`; `curl.exe -s -i http://127.0.0.1:8080/health/ready`; `curl.exe -s -i "${saigonCatalog}"`
    - **Expected:** readiness returns `Healthy`; the catalog `200`; the key repopulates.
- Cleanup: ensure Redis is started before leaving the case.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M10: SQL outage behavior and recovery

> [!NOTE]
> Covers: S2-04, S2-05, AC2-05, AC2-06, T-13. Type: operational.

- Preconditions and synthetic data: populate the Saigon catalog key first (TC-M08); `$saigonStart`.
- [ ] **Step 1:** `docker compose stop sqlserver`
    - **Expected:** SQL Server stops; the API remains running.
- [ ] **Step 2:** `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships"`; `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`; `curl.exe -s -i "${saigonCatalog}"`
    - **Expected:** discovery and availability return `503` `dependency_unavailable`; the catalog returns `200` from the cached value; availability never reports a false `true`.
- [ ] **Step 3:** `curl.exe -s -i http://127.0.0.1:8080/health/ready` and `curl.exe -s -i http://127.0.0.1:8080/health/live`
    - **Expected:** readiness `503` with `database` `Unhealthy`; liveness `200`.
- [ ] **Step 4:** `docker compose up -d --wait sqlserver`; `curl.exe -s -i http://127.0.0.1:8080/health/ready`; `curl.exe -s -i "http://127.0.0.1:8080/api/dealerships"`
    - **Expected:** readiness returns to `200`; discovery returns `200`; the catalog remains `200`.
- Cleanup: ensure SQL Server is running before leaving the case.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M11: Swagger UI and OpenAPI documentation

> [!NOTE]
> Covers: S2-06, AC2-01, AC2-07. Type: manual API/documentation.

- Preconditions and synthetic data: `$saigonStart`; seeded dealership/service IDs.
- [ ] **Step 1:** `curl.exe -s http://127.0.0.1:8080/openapi/v1.json`
    - **Expected:** the three ticket 2 paths; `DealershipResponse`, `ServiceTypeResponse`, `AvailabilityResponse`, and `ProblemDetails` schemas; `dealershipId`, `serviceTypeId`, and `startsAt` marked required; ordering and explicit-offset descriptions; documented `400`/`404`/`503`/`500` responses.
- [ ] **Step 2:** Open `http://127.0.0.1:8080/swagger/index.html`.
    - **Expected:** the three operations are visible and executable; no deleted demo-script instructions are present.
- [ ] **Step 3:** Use Swagger "Try it out" for discovery, catalog, and availability with the seeded IDs and `$saigonStart`.
    - **Expected:** responses match TC-M01-TC-M03; validation and not-found requests show the documented Problem Details.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —
- Automated evidence: TC2-33; TC2-35 is retired by the 2026-11-01 scope amendment.

### TC-M12: Availability is read-only and does not touch the catalog cache

> [!NOTE]
> Covers: AC2-03, S2-03, T-04. Type: manual API + Redis.

- Preconditions and synthetic data: `$saigonStart`; Saigon catalog key from TC-M08.
- [ ] **Step 1:** `docker compose exec -T redis redis-cli DEL "$key"`; then `docker compose exec -T redis redis-cli EXISTS "$key"`.
    - **Expected:** `EXISTS` returns `0`.
- [ ] **Step 2:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`
    - **Expected:** `200` with the six-field availability DTO.
- [ ] **Step 3:** `docker compose exec -T redis redis-cli EXISTS "$key"`
    - **Expected:** still `0`; availability neither reads nor writes the catalog cache.
- [ ] **Step 4:** Optional, Blocked if unavailable: with the isolated local setup identity and a working `sqlcmd`, compare `SELECT COUNT(*) FROM dbo.Appointments` before and after.
    - **Expected:** the count is unchanged; no reservation is created. If `sqlcmd` or privileged access is unavailable, mark this step Blocked and cite automated TC2-21.
- Cleanup: none.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M13: API starts with Redis unreachable (optional)

> [!NOTE]
> Covers: S2-05, AC2-05, AC2-06. Type: operational.

- Preconditions and synthetic data: stack running; `$saigonStart`.
- [ ] **Step 1:** `docker compose stop redis`; `docker compose restart api`; wait for the API to start.
    - **Expected:** the API starts; `/health/live` returns `200`; `/health/ready` returns `200` with `redis` `Degraded` and `database` `Healthy`; retry once if the SQL check warms.
- [ ] **Step 2:** `curl.exe -s -i "${saigonCatalog}"` and `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStart}"`
    - **Expected:** both `200`; SQL-backed behavior remains correct.
- [ ] **Step 3:** `docker compose start redis`; `curl.exe -s -i http://127.0.0.1:8080/health/ready`
    - **Expected:** readiness returns `Healthy`.
- Cleanup: ensure Redis is started before leaving the case.
- Execution status: Not run.
- Observed result / evidence: —

## Execution record

| Date / time and zone | Revision | Case / actual action | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| — | — | — | No manual cases executed; checklist generated on 2026-11-01. | Not run | This file |

No manual case is marked Passed by checklist generation. Record the actual date/time, revision, environment, action, observed result, and evidence after a human executes each case.

## Issues and follow-up

- Failed or blocked cases: none executed; all cases above are Not run.
- Unexecuted checks and missing prerequisites: all manual cases; a human tester must execute them against the local Compose stack. DST, sanitized `500`, real SQL conflict/concurrency, and Redis stall behavior remain automated-only because they need controlled data or timing.
- Automated evidence: [[test/2-test|Ticket 2 test report]] records 98 unit and 92 integration tests passed, plus Compose Redis/SQL stop-start checks; that evidence supplements but does not replace manual execution.
- Known limitations: SQL execution-plan capture on representative volume remains a recorded test-report limitation; the first readiness call after an API restart can be `Unhealthy` while SQL warms; the ticket's final paragraph retains the executable-example wording noted in [[research/2/00-overview#Verification|the research verification]].
- Documentation follow-up: `docs/deploy-notes/2-deploy-notes.md` still lists the retired demo script in its post-deployment checklist; the 2026-11-01 amendment retires it. Correct this in the owning documentation change; it does not block the manual cases.
- Retest revision and observed results: record the checkout revision, environment, command/action, observation, and evidence for every executed case; preserve this generated baseline when updating the file.
