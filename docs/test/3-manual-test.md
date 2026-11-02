---
title: "3 - Manual Tests"
tags: [test, manual, service-scheduler, booking, concurrency]
ticket: 3
status: not-run
updated: 2026-11-02
---

# 3 - Manual tests

## Scope and prerequisites

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research, plan, and primary test report: [[research/3/00-overview|Ticket 3 research overview]], [[plans/3/README|Ticket 3 implementation plan]], [[test/3-test|ticket 3 test report]].
- Review: [[review/3-review|Ticket 3 review]] (2026-11-02; findings R-01-R-04 fixed and re-verified; see Issues and follow-up).
- Reviewed revision and implementation scope: implementation commit `a666927` (HEAD, `main`); the ticket 3 change is `git diff 90a6284..a666927`; the working tree additionally contains staged documentation/review updates. The review reproduced the automated suites at this revision.
- Requirement IDs: S3-01 through S3-06; acceptance labels AC3-01 through AC3-13; verification cases T-01 through T-11, T-13, T-14, and T-16; ticket-local cases S3-V01 through S3-V06; project requirements A-01, A-02, A-03, P-01, P-04, and D-01 through D-04.
- Exclusions: authentication/customer access control, cancellation/rescheduling, idempotent POST, vehicle-overlap protection, service-catalog cache changes, telemetry infrastructure, external integrations, and a custom UI. Redis is not a booking lock.
- Environment and API base URL: local Docker Compose stack; API `http://127.0.0.1:8080`; Swagger UI `http://127.0.0.1:8080/swagger/index.html`; OpenAPI `http://127.0.0.1:8080/openapi/v1.json`; no HTTPS and no login in the initial local demo.
- Command shell: the examples use Windows PowerShell and `curl.exe`; run them from the repository root. The concurrency snippet uses `Start-Job`, which works in PowerShell 5.1 and 7.
- Dependency versions and migration/seed baseline: .NET SDK `10.0.403`; Docker `25.0.3`; Compose `v2.24.6-desktop.1`; SQL Server 2022 CU23; Redis `7.4.12-alpine`; migrations `20261031092016_InitialCreate` and `20261101161530_AddBookingProcedureAndTimeZoneMappings`. Setup seeds weekday shifts for a 28-day horizon from the setup time.
- Dealership time zones: Saigon `Asia/Ho_Chi_Minh` (UTC+7, no DST); London `Europe/London`. Both have weekday opening hours 09:00-17:00 local and one active seeded technician/bay.
- Synthetic seed identifiers:

| Entity | Identifier | Notes |
| --- | --- | --- |
| Saigon dealership | `a1000000-0000-0000-0000-000000000001` | `Asia/Ho_Chi_Minh`; weekday 09:00-17:00 opening |
| London dealership | `a1000000-0000-0000-0000-000000000002` | `Europe/London`; weekday 09:00-17:00 opening |
| Customer An Nguyen | `c1000000-0000-0000-0000-000000000001` | Owns the An Nguyen vehicle |
| Customer Oliver Smith | `c1000000-0000-0000-0000-000000000002` | Owns the Oliver Smith vehicle |
| An Nguyen vehicle | `b1000000-0000-0000-0000-000000000001` | Honda Accord, synthetic VIN |
| Oliver Smith vehicle | `b1000000-0000-0000-0000-000000000002` | Ford Focus, synthetic VIN |
| Saigon Oil Change | `51000000-0000-0000-0000-000000000001` | 60 minutes; active Saigon technician qualified |
| Saigon Brake Service | `51000000-0000-0000-0000-000000000002` | 120 minutes; active Saigon technician qualified |
| London Annual Service | `51000000-0000-0000-0000-000000000003` | 90 minutes; active London technician qualified |
| Saigon active technician | `71000000-0000-0000-0000-000000000001` | Minh Tran |
| Saigon active bay | `81000000-0000-0000-0000-000000000001` | Bay 1 |
| London active technician | `71000000-0000-0000-0000-000000000003` | James Wilson |
| London active bay | `81000000-0000-0000-0000-000000000003` | Bay 1 |

- No appointment is seeded. Each dealership has exactly one active technician and one active bay, so the API has one eligible pair per dealership; this is the deliberate single-pair capacity used by the concurrency case.
- Future-time preparation: run this snippet and substitute the printed values. The primary date is at least two days ahead and is the next weekday; `$date2` is the next weekday after that. Both are within the 28-day seeded horizon when the stack was set up recently. The snippet also derives the weekend and boundary values used below.

```powershell
$base = 'http://127.0.0.1:8080'
$saigonId = 'a1000000-0000-0000-0000-000000000001'
$londonId = 'a1000000-0000-0000-0000-000000000002'
$anId = 'c1000000-0000-0000-0000-000000000001'
$oliverId = 'c1000000-0000-0000-0000-000000000002'
$anVehicleId = 'b1000000-0000-0000-0000-000000000001'
$oilChange = '51000000-0000-0000-0000-000000000001'
$brakeService = '51000000-0000-0000-0000-000000000002'
$londonService = '51000000-0000-0000-0000-000000000003'
$saigonAppointments = "$base/api/dealerships/$saigonId/appointments"
$londonAppointments = "$base/api/dealerships/$londonId/appointments"
$availability = "$base/api/dealerships/$saigonId/availability"

$date = (Get-Date).Date.AddDays(2)
while ($date.DayOfWeek -in @('Saturday','Sunday')) { $date = $date.AddDays(1) }
$date2 = $date.AddDays(1)
while ($date2.DayOfWeek -in @('Saturday','Sunday')) { $date2 = $date2.AddDays(1) }
$saturday = (Get-Date).Date.AddDays(1)
while ($saturday.DayOfWeek -ne 'Saturday') { $saturday = $saturday.AddDays(1) }

$saigonStart       = '{0:yyyy-MM-dd}T10:00:00+07:00' -f $date
$saigonStartQuery  = $saigonStart.Replace('+','%2B')
$saigonStartUtc    = '{0:yyyy-MM-dd}T03:00:00Z' -f $date
$saigonAdjacent    = '{0:yyyy-MM-dd}T11:00:00+07:00' -f $date
$saigonOverlap     = '{0:yyyy-MM-dd}T10:30:00+07:00' -f $date
$saigonContained   = '{0:yyyy-MM-dd}T10:15:00+07:00' -f $date
$saigonBoundary    = '{0:yyyy-MM-dd}T16:00:00+07:00' -f $date
$saigonLate        = '{0:yyyy-MM-dd}T16:30:00+07:00' -f $date
$saigonOffset14    = '{0:yyyy-MM-dd}T14:00:00+07:00' -f $date
$saigonOffset14Utc = '{0:yyyy-MM-dd}T07:00:00Z' -f $date
$saigonConcurrent  = '{0:yyyy-MM-dd}T13:00:00+07:00' -f $date
$saigonDuration    = '{0:yyyy-MM-dd}T10:00:00+07:00' -f $date2
$saigonDurationProbe = '{0:yyyy-MM-dd}T11:30:00+07:00' -f $date2
$saigonRedisStart  = '{0:yyyy-MM-dd}T15:00:00+07:00' -f $date2
$saigonSqlStart    = '{0:yyyy-MM-dd}T13:00:00+07:00' -f $date2
$weekendStart      = '{0:yyyy-MM-dd}T10:00:00+07:00' -f $saturday
$offsetFree        = '{0:yyyy-MM-dd}T10:00:00' -f $date
$pastStart         = '2020-01-01T00:00:00Z'
$londonStart       = '{0:yyyy-MM-dd}T10:00:00Z' -f $date

Write-Output "Saigon start: $saigonStart"
Write-Output "Saigon availability query: $saigonStartQuery"
Write-Output "Saigon UTC equivalent: $saigonStartUtc"
Write-Output "Saigon adjacent: $saigonAdjacent"
Write-Output "Saigon overlap: $saigonOverlap"
Write-Output "Saigon contained: $saigonContained"
Write-Output "Saigon boundary: $saigonBoundary"
Write-Output "Saigon late: $saigonLate"
Write-Output "Saigon offset-equivalent: $saigonOffset14 / $saigonOffset14Utc"
Write-Output "Saigon concurrent: $saigonConcurrent"
Write-Output "Saigon duration date: $saigonDuration / probe $saigonDurationProbe"
Write-Output "Saigon Redis outage: $saigonRedisStart"
Write-Output "Saigon SQL outage: $saigonSqlStart"
Write-Output "Saigon Saturday: $weekendStart"
Write-Output "Offset-free: $offsetFree"
Write-Output "London start: $londonStart"

function Get-AppointmentCount {
    docker compose -p scheduler-t3-manual exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d ServiceScheduler -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.Appointments;"'
}
```

- `Get-AppointmentCount` uses the privileged setup identity inside the SQL container for read-only counts; it requires the stack to be running. Do not record the password or use the setup identity to add direct appointment writes. If the helper is unavailable, use the availability endpoint as the non-count check.
- If availability is `false` for the intended weekday, the chosen date may be outside the seeded 28-day horizon or the slot may already be booked by a previous run. Rerun setup with `docker compose down` then `docker compose up -d --wait` (both keep the named SQL volume and reseed future shifts), or choose a later weekday and regenerate the variables.
- Required setup: use an isolated Compose project to avoid the pre-existing volume/password mismatch recorded in the ticket 3 test report. Copy `.env.example` to `.env`, replace both placeholder passwords in all four places (the two variables and the `Password=` segment of the two connection strings), then run `docker compose -p scheduler-t3-manual up -d --build --wait`. Wait for `/health/ready` to return `200`. Do not record credentials.
- The API loads `.env` only through Compose interpolation; direct `dotnet run` needs exported environment variables. Use `127.0.0.1` for host-to-container SQL and Redis connections.
- First readiness after an API restart can be `Unhealthy` while the SQL check warms; retry once before treating it as a failure.
- Cleanup: appointments persist in the named SQL volume, and there is no API delete operation. Use a fresh isolated project/volume for a clean rerun, or delete only this run's synthetic rows with the privileged setup identity. Do not grant direct appointment writes to the runtime identity. At the end, `docker compose -p scheduler-t3-manual down` keeps the volume; use `down -v` only for the explicitly isolated manual project after confirming no data is needed.
- Script limitation (resolved 2026-11-02): `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, and `docs/README.md` previously referenced booking smoke/concurrency harnesses under `scripts/`. The references now point to Swagger UI and the automated tests; `scripts/` remains absent, so use the commands in this checklist or the automated integration tests.

> [!NOTE]
> The ticket 3 review's R-01-R-04 findings were fixed and re-verified on 2026-11-02: the allocator now enforces a request-wide deadlock-retry limit and a pre-call budget check, setup rejects runtime database/object ownership, and the documentation points to existing manual and automated checks. The manual checklist remains unexecuted; ticket acceptance is not claimed.

## Requirement coverage

| Requirement ID | Case IDs | Relevant automated evidence / remaining coverage |
| --- | --- | --- |
| S3-01 | TC-M01, TC-M02, TC-M03, TC-M04 | `AppointmentApiTests`, `AppointmentValidationTests`, `AppointmentDstTests`, `OpenApiTests`, `ProblemDetailsTests`; manual checks supplement the contract. |
| S3-02 | TC-M02, TC-M03, TC-M04, TC-M05, TC-M12 | `BookingProcedureTests`, `AppointmentEligibilityTests`, `AppointmentIntervalTests`, `AppointmentDurationTests`, `AppointmentDstTests`; resource-only conflicts and DST remain automated-only. |
| S3-03 | TC-M05, TC-M06, TC-M07, TC-M11, TC-M13 | `BookingProcedureTests`, `BookingConcurrencyTests`, `BookingSetupTests`, `BookingMigrationTests`, `BookingMigrationUpgradeTests`; alternative pair and migration upgrade remain automated-only. |
| S3-04 | TC-M07, TC-M11 | `BookingAllocatorTests`, `BookingFailureBoundaryTests`; bounded retry, budget, cancellation, and unknown-commit behavior remain automated-only. Review findings R-01/R-02 are open. |
| S3-05 | TC-M02, TC-M04, TC-M08, TC-M09 | `AppointmentApiTests` restart, `AppointmentDurationTests`, `BookingOutageTests`; manual retrieval/outage checks supplement. |
| S3-06 | TC-M01, TC-M08, TC-M09, TC-M11, TC-M12, TC-M13 | Migration/setup/permission/outage/operational/clean-directory evidence in [[test/3-test\|the test report]]; manual setup and permission inspection supplement. Review findings R-03 and R-04 were resolved 2026-11-02. |
| AC3-01 | TC-M02, TC-M08 | Persistence and restart retrieval are automated and manually supplemented. |
| AC3-02 | TC-M03, TC-M04, TC-M05, TC-M12 | Validation, eligibility, and duration checks. |
| AC3-03 | TC-M05, TC-M06 | Adjacent/overlap boundaries and repeat POST. |
| AC3-04 | TC-M07 | Twenty concurrent POSTs with one eligible pair. |
| AC3-05 | TC-M11, TC-M13 | Runtime permission inspection; direct procedure parity and denied DML remain automated-only. |
| AC3-06 | TC-M07, TC-M09 | Concurrency smoke and outage/timeout behavior; exact lock/command/deadlock tests remain automated-only. |
| AC3-07 | TC-M08, TC-M09 | Redis outage booking/retrieval and SQL outage/readiness. |
| AC3-08 | TC-M02 through TC-M07, TC-M09, TC-M12 | Synthetic seeded IDs and generated future times are used throughout. |
| AC3-09 | TC-M01, TC-M13 | Setup, health, and repeated startup; R-03 limits the documented harness portion. |
| AC3-10 | TC-M01, TC-M02, TC-M03, TC-M04 | DTO, Location, UTC, Problem Details, OpenAPI, and Swagger checks. |
| AC3-11 | TC-M12 | Duration snapshot and stale-catalog probe; automated `AppointmentDurationTests` is the primary evidence. |
| AC3-12 | TC-M07, TC-M11 | Concurrency and permission smoke; bounded failure policy remains automated-only. Review findings R-01/R-02 are open. |
| AC3-13 | TC-M11, TC-M13 | Repeated setup and permission inspection; upgrade/down/recovery remain automated-only. Review finding R-04 is open. |

| Verification case | Manual case(s) | Automated evidence / status |
| --- | --- | --- |
| T-01 Valid future booking | TC-M02 | `AppointmentApiTests`; Passed automated. |
| T-02 Qualification or inactive resource | TC-M05 (coverage context) | `AppointmentEligibilityTests`, `BookingProcedureTests`; manual activation/qualification mutation is not provided; automated-only. |
| T-03 Partial shift/opening coverage | TC-M05 | `AppointmentEligibilityTests`, `BookingProcedureTests`; manual late-start check. |
| T-04 Interval boundaries | TC-M05, TC-M06 | `AppointmentIntervalTests`, `BookingProcedureTests`; manual adjacent/overlap/identical checks. |
| T-05 Technician conflict only | — | Automated-only: `BookingProcedureTests` (`51001`), `AppointmentIntervalTests`; not isolatable with the seeded single active pair without direct SQL fixtures. |
| T-06 Bay conflict only | — | Automated-only: `BookingProcedureTests` (`51002`), `AppointmentIntervalTests`; not isolatable with the seeded single active pair without direct SQL fixtures. |
| T-07 Concurrent competition | TC-M07 | `BookingConcurrencyTests`; manual `Start-Job`/parallel smoke. |
| T-08 Database write boundary | TC-M11 | Permission-row inspection is manual; direct runtime procedure calls and denied DML remain automated (`RuntimePermissionTests`, `BookingProcedureTests`). |
| T-09 Atomic failure | — | Automated-only: `BookingProcedureTests`, `BookingFailureBoundaryTests`; failures are injected with controlled locks/fakes. |
| T-10 Time handling | TC-M03 | `AppointmentDstTests`, `AppointmentValidationTests`; offset/DST parity is automated; manual offset-free/past checks. |
| T-11 Relationship and scope | TC-M04, TC-M02 | `AppointmentValidationTests`, `AppointmentApiTests`; manual ownership/scope/retrieval checks. |
| T-13 Booking during Redis outage | TC-M09 | `BookingOutageTests`; manual stop/start Redis. |
| T-14 Restart and database outage | TC-M08, TC-M09 | `AppointmentApiTests` restart, `BookingOutageTests`; manual restart/SQL stop. |
| T-16 Booking clean checkout | TC-M01, TC-M13 | Clean-directory/fresh-volume operational evidence in [[test/3-test\|the test report]]; a separate git clone was not run because implementation was uncommitted at execution time. |
| S3-V01 API contract | TC-M01, TC-M02, TC-M03, TC-M04 | `AppointmentApiTests`, `AppointmentValidationTests`, `OpenApiTests`, `ProblemDetailsTests`. |
| S3-V02 Duration and read compatibility | TC-M12 | `AppointmentDurationTests`; optional SQL/Redis manual probe. |
| S3-V03 Alternative candidates | — | Automated-only: `BookingAllocatorTests`, `AppointmentIntervalTests`; requires additional synthetic resources. |
| S3-V04 Failure boundaries | — | Automated-only: `BookingAllocatorTests`, `BookingFailureBoundaryTests`; unknown commit is a documented limitation. |
| S3-V05 Direct-call validation parity | TC-M03, TC-M04 | Direct procedure calls and DST parity remain automated (`BookingProcedureTests`, `AppointmentDstTests`); API validation is manual. |
| S3-V06 Setup and recovery | TC-M11, TC-M13 | `BookingSetupTests`, `BookingMigrationTests`, `BookingMigrationUpgradeTests`, `RuntimePermissionTests`; upgrade/down remains automated-only. |

## Test cases

### TC-M01: Setup, health, OpenAPI, and Swagger

> [!NOTE]
> Covers: S3-06, AC3-09, AC3-10, T-16 (manual portion), D-03, D-04. Type: operational + manual API.

- Preconditions and synthetic data: Docker running; `.env` copied and both placeholder passwords replaced; isolated Compose project `scheduler-t3-manual`; no login required.
- [ ] **Step 1:** `docker compose -p scheduler-t3-manual config`
    - **Expected:** exit code `0`; the rendered configuration resolves `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, `ConnectionStrings__Setup`, `ConnectionStrings__Runtime`, and `ConnectionStrings__Cache`; no interpolation error.
- [ ] **Step 2:** `docker compose -p scheduler-t3-manual up -d --build --wait`
    - **Expected:** exit code `0`; SQL Server and Redis healthy; one-shot `db-setup` exits successfully; API healthy; no container restart loop.
- [ ] **Step 3:** `docker compose -p scheduler-t3-manual logs db-setup`
    - **Expected:** both migrations are applied or already present; mapping/procedure validation succeeds; runtime provisioning succeeds; future shifts are seeded; no error or sanitized configuration dump.
- [ ] **Step 4:** `curl.exe -s -i http://127.0.0.1:8080/health/live`
    - **Expected:** `200`; liveness reports healthy; no database dependency required for liveness.
- [ ] **Step 5:** `curl.exe -s -i http://127.0.0.1:8080/health/ready` (retry once if the first call is `503` while SQL warms)
    - **Expected:** `200`; JSON readiness body reports `database: Healthy` and `redis: Healthy`. If Redis is intentionally unavailable, `redis: Degraded` is acceptable with HTTP `200`; `database: Unhealthy` is not.
- [ ] **Step 6:** `curl.exe -s http://127.0.0.1:8080/openapi/v1.json`
    - **Expected:** five operations: `GET /api/dealerships`, `GET /api/dealerships/{dealershipId}/service-types`, `GET /api/dealerships/{dealershipId}/availability`, `POST /api/dealerships/{dealershipId}/appointments`, and `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}`. The POST documents `201` with `Location`, `400`, `404`, `409`, `503`, `500`; the `409` uses `capacity_unavailable`; Problem Details includes `code` and `traceId`.
- [ ] **Step 7:** Open `http://127.0.0.1:8080/swagger/index.html`.
    - **Expected:** the five operations are visible; the appointment POST/GET request and response schemas match the documented camelCase fields; Swagger "Try it out" can execute discovery and availability. The former `scripts/` harnesses are not part of this checkout; use the HTTP commands in this checklist or the automated integration tests.
- Cleanup: keep the stack running for the remaining cases; at the end run `docker compose -p scheduler-t3-manual down` (keeps the named volume).
- Execution status: Not run.
- Observed result / evidence: —

### TC-M02: Valid booking, Location, complete response, and retrieval

> [!NOTE]
> Covers: S3-01, S3-02, S3-05, AC3-01, AC3-10, T-01, S3-V01. Type: manual API.

- Preconditions and synthetic data: TC-M01 passed; `$saigonStart`, `$saigonStartQuery`, `$saigonStartUtc`, `$anId`, `$anVehicleId`, `$oilChange`, `$saigonAppointments`, `$availability`, and seed IDs from the preparation snippet. Use a fresh slot or a fresh isolated volume.
- [ ] **Step 1:** `curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonStartQuery}"`
    - **Expected:** `200`; `available: true`; `dealershipId` and `serviceTypeId` echo the request; `startsAt` is `...T03:00:00Z`; `endsAt` is `...T04:00:00Z` (exactly 60 minutes later); `evaluatedAt` ends with `Z`; no technician or bay identifier is returned.
- [ ] **Step 2:** Build the body and POST it with `Invoke-WebRequest`:

    ```powershell
    $body = @"
{"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$saigonStart"}
"@
    $response = Invoke-WebRequest -Uri $saigonAppointments -Method Post -ContentType 'application/json' -Body $body
    $response.StatusCode
    $response.Headers.Location
    $created = $response.Content | ConvertFrom-Json
    $created
    ```

    - **Expected:** `201 Created`; a non-empty `Location` header pointing to `/api/dealerships/a1000000-0000-0000-0000-000000000001/appointments/{id}`; body fields `id`, `dealershipId`, `customerId`, `vehicleId`, `serviceTypeId`, `technicianId`, `serviceBayId`, `startsAt`, `endsAt`, `durationMinutes`, `status`, `createdAt`. `technicianId` and `serviceBayId` are non-empty seeded UUIDs; `startsAt` is the UTC `...T03:00:00Z` form; `endsAt` is `...T04:00:00Z`; `durationMinutes` is `60`; `status` is `Confirmed`; `createdAt` ends with `Z`.
- [ ] **Step 3:** Follow the Location with `Invoke-RestMethod` (prepend `http://127.0.0.1:8080` if the header is relative):

    ```powershell
    $retrieved = Invoke-RestMethod -Uri $response.Headers.Location
    $retrieved
    ```

    - **Expected:** `200`; all twelve persisted fields equal the POST response, including the same appointment ID, start/end, duration snapshot, status, and creation time.
- [ ] **Step 4:** `curl.exe -s -i "$base/api/dealerships/$londonId/appointments/$($created.id)"`
    - **Expected:** `404 not_found` Problem Details with a non-empty `traceId`; no appointment body. Retrieval is dealership scoped.
- [ ] **Step 5:** `curl.exe -s -i "$base/api/dealerships/$saigonId/appointments/00000000-0000-0000-0000-000000000000"`
    - **Expected:** `404 not_found` with a non-empty `traceId`.
- [ ] **Step 6 (optional):** Book the London Annual Service to exercise the other dealership and time zone:

    ```powershell
    $londonBody = @"
{"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$londonService","startsAt":"$londonStart"}
"@
    $londonResponse = Invoke-WebRequest -Uri $londonAppointments -Method Post -ContentType 'application/json' -Body $londonBody
    $londonResponse.StatusCode
    $londonCreated = $londonResponse.Content | ConvertFrom-Json
    $londonCreated
    ```

    - **Expected:** `201`; `durationMinutes` is `90`; `endsAt` is exactly 90 minutes after `startsAt`; the response uses the London dealership and its active technician/bay. At the checklist date, London is on GMT; if executed during British Summer Time, `10:00Z` is 11:00 local and still within 09:00-17:00.
- Cleanup: appointments persist. Record the IDs; use a fresh isolated project/volume for a clean rerun, or delete only this run's synthetic rows with the privileged setup identity. Do not grant direct appointment writes to the runtime identity.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M03: Time normalization, offset-free, past, and malformed starts

> [!NOTE]
> Covers: S3-01, S3-02, AC3-02, AC3-10, T-10, S3-V01. Type: manual API.

- Preconditions and synthetic data: `$saigonOffset14` (`14:00+07:00`), `$saigonOffset14Utc` (`07:00Z`), `$offsetFree`, `$pastStart`, `$date`; availability URL and Saigon IDs from the preparation snippet.
- [ ] **Step 1:** Request availability twice, once with `$saigonOffset14` (URL-encode `+` as `%2B`) and once with `$saigonOffset14Utc`:

    ```powershell
    $offsetQuery = $saigonOffset14.Replace('+','%2B')
    curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${offsetQuery}"
    curl.exe -s -i "${availability}?serviceTypeId=${oilChange}&startsAt=${saigonOffset14Utc}"
    ```

    - **Expected:** both `200`; the same `available` value, `startsAt`, and `endsAt`; `startsAt` is normalized to `...T07:00:00Z`; `endsAt` is `...T08:00:00Z`; `evaluatedAt` may differ only by the real clock advancing.
- [ ] **Step 2:** POST a booking using `$saigonOffset14` (same body pattern as TC-M02, substituting `startsAt`).
    - **Expected:** `201`; the response `startsAt` is `...T07:00:00Z` and `endsAt` is `...T08:00:00Z`; `durationMinutes` is `60`; the input offset is not echoed.
- [ ] **Step 3:** Send a POST with `startsAt` set to `$offsetFree` (no `Z` or explicit offset).
    - **Expected:** `400 validation_error`; non-empty `traceId`; no appointment created for that interval.
- [ ] **Step 4:** Send POSTs with `startsAt` set to `$pastStart`, `2020-01-01T00:00:00Z`-style past values, `not-a-date`, and a date-only value such as `2030-01-01`.
    - **Expected:** each returns `400 validation_error` with `traceId`; no unintended write.
- [ ] **Step 5:** Send a POST whose `startsAt` is the current UTC instant (for example `(Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')`).
    - **Expected:** `400 validation_error`; the start must be strictly future.
- [ ] **Step 6:** Recheck availability for a still-free weekday slot.
    - **Expected:** `200 available: true`; the rejected requests created no reservation. Optionally, compare `Get-AppointmentCount` before and after; the only increment is the successful Step 2 booking.
- Cleanup: the Step 2 appointment persists; record it and use a fresh volume for repeated runs.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M04: Ownership, dealership scope, and malformed input

> [!NOTE]
> Covers: S3-01, S3-02, S3-05, AC3-02, AC3-10, T-11, S3-V01, S3-V05 (API portion). Type: manual API.

- Preconditions and synthetic data: `$anId`, `$oliverId`, `$anVehicleId`, `$londonService`, `$oilChange`, `$saigonId`, `$londonId`, `$saigonStart`; use `startsAt = $saigonStart` for each request (validation and not-found checks run before capacity).
- [ ] **Step 1:** POST with `customerId = $oliverId`, `vehicleId = $anVehicleId`, and `startsAt = $saigonStart`.
    - **Expected:** `400 validation_error` with `traceId`; the vehicle belongs to a different customer; no appointment is created.
- [ ] **Step 2:** POST to the Saigon appointment collection with the London Annual Service `serviceTypeId = $londonService` and `startsAt = $saigonStart`.
    - **Expected:** `404 not_found` with `traceId`; the service is outside the requested dealership.
- [ ] **Step 3:** POST with an unknown customer, unknown vehicle, unknown service type, and an unknown dealership UUID, using `startsAt = $saigonStart` where the route is valid.
    - **Expected:** each returns `404 not_found` with `traceId`; no appointment is created.
- [ ] **Step 4:** POST with a malformed UUID such as `"customerId": "not-a-guid"`, an empty JSON object `{}`, and malformed JSON `{`; use `startsAt = $saigonStart` in the malformed-UUID body.
    - **Expected:** each returns `400 validation_error` with `traceId`; no unintended write. The framework model-state response also carries the shared `code`/`traceId`.
- [ ] **Step 5:** `GET /api/dealerships/not-a-guid/appointments` and `GET /api/dealerships/{saigonId}/appointments/not-a-guid`.
    - **Expected:** both return `400 validation_error` with `traceId`; malformed route GUIDs are validation errors, not route-selected `404`s.
- [ ] **Step 6:** Compare `Get-AppointmentCount` before and after the rejected requests, or recheck availability for a free slot.
    - **Expected:** no count increase and no slot becomes unavailable; the rejected requests left no partial booking.
- Cleanup: none; no appointment should be created by this case.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M05: Opening/shift coverage and interval boundaries

> [!NOTE]
> Covers: S3-02, AC3-02, AC3-03, T-03, T-04, S3-V05 (API portion). Type: manual API.

- Preconditions and synthetic data: TC-M02 succeeded at `$saigonStart` (10:00-11:00 Saigon local, 03:00-04:00Z). Values: `$saigonAdjacent` (11:00 local), `$saigonBoundary` (16:00 local), `$saigonLate` (16:30 local), `$saigonOverlap` (10:30), `$saigonContained` (10:15), `$weekendStart`.
- [ ] **Step 1:** POST an Oil Change at `$saigonAdjacent` (11:00-12:00 local).
    - **Expected:** `201`; `startsAt` is `04:00Z`; `endsAt` is `05:00Z`; adjacent to the existing 10:00-11:00 appointment; no conflict.
- [ ] **Step 2:** POST an Oil Change at `$saigonBoundary` (16:00-17:00 local).
    - **Expected:** `201`; the interval ends exactly at closing and shift end and is allowed; `endsAt` is `10:00Z`.
- [ ] **Step 3:** POST an Oil Change at `$weekendStart` (next Saturday 10:00 local).
    - **Expected:** `409 capacity_unavailable` with `traceId`; no eligible technician shift exists on the weekend; no appointment is created.
- [ ] **Step 4:** POST an Oil Change at `$saigonLate` (16:30-17:30 local).
    - **Expected:** `409 capacity_unavailable` with `traceId`; the full interval does not fit the 09:00-17:00 opening and shift; no partial booking.
- [ ] **Step 5:** POST an Oil Change at each of `$saigonOverlap`, `$saigonContained`, `$saigonStart`, and `$saigonAdjacent`.
    - **Expected:** each returns `409 capacity_unavailable` with `traceId`. `$saigonOverlap` partially overlaps both existing bookings; `$saigonContained` is contained in the 10:00-11:00 booking; the two identical starts are the same intervals as the existing bookings. Half-open `[start, end)` semantics make adjacency succeed but any overlap fail.
- [ ] **Step 6:** Compare appointment counts or recheck availability for the conflicting slots.
    - **Expected:** the rejected steps add no rows; availability for 10:00, 10:15, 10:30, and 11:00 is false, while a fresh non-overlapping slot remains available.
- Cleanup: successful appointments persist; record the IDs.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M06: Repeated POST is a new attempt and does not duplicate the appointment

> [!NOTE]
> Covers: S3-03, S3-04, AC3-03, AC3-04 (prelude), T-04. Type: manual API.

- Preconditions and synthetic data: the TC-M02 appointment ID and the exact same POST body and `$saigonStart`.
- [ ] **Step 1:** Record `$beforeRepeat = Get-AppointmentCount`, then repeat the exact TC-M02 POST, then record `$afterRepeat = Get-AppointmentCount`.
    - **Expected:** `409 capacity_unavailable` with `traceId`; the repeat is a new booking attempt, not an idempotent success; `$afterRepeat` equals `$beforeRepeat`.
- [ ] **Step 2:** GET the original appointment by its ID and dealership.
    - **Expected:** `200`; the original appointment is unchanged; no duplicate or replacement row.
- [ ] **Step 3:** Request availability at `$saigonStart`.
    - **Expected:** `200 available: false`; the confirmed appointment still blocks the interval.
- Cleanup: the original appointment persists; leave it for later cases or delete only this run's rows after the checklist.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M07: Concurrent competition with one eligible pair

> [!NOTE]
> Covers: S3-03, S3-04, AC3-04, AC3-06, AC3-08, T-07. Type: manual API + concurrency smoke.

- Preconditions and synthetic data: `$saigonConcurrent` (13:00 Saigon local) is free; one active Saigon technician and one active bay; PowerShell 5.1+; no other concurrent test traffic. The automated `BookingConcurrencyTests` uses coordinated barriers and is the authoritative concurrency evidence.
- [ ] **Step 1:** Build the shared body:

    ```powershell
    $concurrentBody = @"
{"customerId":"$anId","vehicleId":"$anVehicleId","serviceTypeId":"$oilChange","startsAt":"$saigonConcurrent"}
"@
    ```

- [ ] **Step 2:** Record `$beforeConcurrent = Get-AppointmentCount`, then submit 20 POSTs. The `Start-Job` form works in PowerShell 5.1 and 7; it is an approximate concurrency smoke, not a barrier:

    ```powershell
    $jobs = 1..20 | ForEach-Object {
        Start-Job -ScriptBlock {
            param($uri, $body)
            try {
                $r = Invoke-WebRequest -Uri $uri -Method Post -ContentType 'application/json' -Body $body
                201
            } catch {
                if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'error' }
            }
        } -ArgumentList $saigonAppointments, $concurrentBody
    }
    $results = $jobs | Wait-Job | Receive-Job
    $jobs | Remove-Job
    $results | Group-Object | Select-Object Name, Count
    ```

    - **Expected:** exactly one `201` and nineteen `409` results; no `500` or unexpected status. All `409` responses use `capacity_unavailable` with a non-empty `traceId`.
- [ ] **Step 3:** Record `$afterConcurrent = Get-AppointmentCount`; request availability at `$saigonConcurrent`.
    - **Expected:** `$afterConcurrent` is exactly one greater than `$beforeConcurrent`; `200 available: false`; the single confirmed appointment blocks the interval. No partial or duplicate booking exists.
- Cleanup: the single confirmed appointment persists; record its ID.
- Execution status: Not run.
- Observed result / evidence: —
- Note: if every request returns `409` because the slot was already booked by a previous run, choose another free weekday/time and regenerate the body. If any request returns `500` or `503`, record the response and trace ID and treat the case as Failed; do not retry by granting direct writes.

### TC-M08: API restart and container restart preserve retrieval

> [!NOTE]
> Covers: S3-05, AC3-01, AC3-07, T-14 (restart portion). Type: operational.

- Preconditions and synthetic data: a confirmed appointment from TC-M02; stack running; its appointment ID.
- [ ] **Step 1:** `docker compose -p scheduler-t3-manual restart api`; wait for the container to serve requests.
    - **Expected:** liveness `/health/live` returns `200`; readiness may briefly return `503` while SQL warms, then returns `200`.
- [ ] **Step 2:** GET the TC-M02 appointment by dealership and appointment ID.
    - **Expected:** `200`; all stored fields match the pre-restart response; no Redis dependency is needed for retrieval.
- [ ] **Step 3:** `docker compose -p scheduler-t3-manual down` (keeps the named SQL volume); then `docker compose -p scheduler-t3-manual up -d --wait`.
    - **Expected:** `db-setup` exits successfully; the API starts; readiness returns `200` after warm-up; the appointment still returns `200` with the same body. The named volume preserves data across ordinary restarts.
- Cleanup: keep the stack running for the outage cases.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M09: Booking and retrieval during a Redis outage

> [!NOTE]
> Covers: S3-05, S3-06, AC3-07, T-13. Type: operational + manual API.

- Preconditions and synthetic data: stack running; `$saigonRedisStart` (`$date2` 15:00 Saigon local) free; a confirmed appointment from an earlier case; Redis service name `redis`.
- [ ] **Step 1:** `docker compose -p scheduler-t3-manual stop redis`
    - **Expected:** Redis stops; the API, SQL Server, and setup remain available; the API does not restart or fail startup.
- [ ] **Step 2:** `curl.exe -s -i http://127.0.0.1:8080/health/ready` and `curl.exe -s -i http://127.0.0.1:8080/health/live`
    - **Expected:** readiness returns `200` with `database: Healthy` and `redis: Degraded`; liveness returns `200`. Retry readiness once if the SQL check warms.
- [ ] **Step 3:** POST an Oil Change at `$saigonRedisStart`.
    - **Expected:** `201`; the booking succeeds without Redis; response duration is `60`; `endsAt` is exactly 60 minutes later; `Location` is present.
- [ ] **Step 4:** GET the new appointment and an earlier appointment.
    - **Expected:** both return `200` with the stored values; retrieval reads SQL Server and does not require Redis.
- [ ] **Step 5:** `curl.exe -s -i "$base/api/dealerships/$saigonId/service-types"`
    - **Expected:** `200`; the catalog falls back to SQL Server within the bounded cache budget; no Redis error is surfaced to the client.
- [ ] **Step 6:** `docker compose -p scheduler-t3-manual start redis`; then check readiness and the catalog.
    - **Expected:** readiness returns `200` with `redis: Healthy` (allow a short recovery interval); the catalog remains `200`; the cache may repopulate on the next request.
- Cleanup: ensure Redis is started before leaving the case; the new appointment persists.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M10: SQL outage prevents confirmation and fails readiness

> [!NOTE]
> Covers: S3-05, S3-06, AC3-07, T-14 (database-outage portion). Type: operational + manual API.

- Preconditions and synthetic data: stack running; `$saigonSqlStart` (`$date2` 13:00 Saigon local) free; an existing appointment ID; SQL service name `sqlserver`.
- [ ] **Step 1:** `docker compose -p scheduler-t3-manual stop sqlserver`
    - **Expected:** SQL Server stops; the API remains running (liveness stays available); no automatic confirmation is possible.
- [ ] **Step 2:** POST an Oil Change at `$saigonSqlStart`.
    - **Expected:** `503 dependency_unavailable` Problem Details with a non-empty `traceId`; not a `409` capacity conflict, not a `500`, and no stack trace or connection string in the body.
- [ ] **Step 3:** GET an existing appointment; `curl.exe -s -i http://127.0.0.1:8080/health/ready`; `curl.exe -s -i http://127.0.0.1:8080/health/live`.
    - **Expected:** retrieval and readiness return `503`; readiness reports `database: Unhealthy`; liveness returns `200`.
- [ ] **Step 4:** `docker compose -p scheduler-t3-manual up -d --wait sqlserver`; wait for SQL health.
    - **Expected:** the SQL container becomes healthy; the API remains running.
- [ ] **Step 5:** Retry readiness once or twice, then GET the existing appointment and POST a different free slot.
    - **Expected:** readiness returns to `200` with `database: Healthy`; the existing appointment returns `200` with unchanged values; the new booking returns `201`. No confirmation occurred during the outage.
- Cleanup: ensure SQL Server is running before leaving the case.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M11: Runtime permission and ownership inspection

> [!NOTE]
> Covers: S3-03, S3-06, AC3-05, AC3-13, T-08 (inspection portion), S3-V06 (setup portion). Type: operational SQL inspection.

- Preconditions and synthetic data: stack running; the SQL Server container has `MSSQL_SA_PASSWORD` in its environment; the setup identity is used only for inspection. Do not record credentials. Direct runtime procedure calls and direct DML denial are covered by `RuntimePermissionTests` and `BookingProcedureTests`; this case inspects the persisted permission/ownership state.
- [ ] **Step 1:** Query explicit permissions for `scheduler_app`:

    ```powershell
    docker compose -p scheduler-t3-manual exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d ServiceScheduler -Q "SET NOCOUNT ON; SELECT dp.state_desc, dp.permission_name, dp.class_desc, OBJECT_SCHEMA_NAME(dp.major_id) AS schema_name, OBJECT_NAME(dp.major_id) AS object_name FROM sys.database_permissions AS dp JOIN sys.database_principals AS pr ON pr.principal_id = dp.grantee_principal_id WHERE pr.name = ''scheduler_app'' ORDER BY dp.class_desc, object_name, dp.permission_name;"'
    ```

    - **Expected:** exactly the documented rows: `GRANT CONNECT` on `DATABASE`; `GRANT SELECT` on `SCHEMA` `dbo`; `GRANT EXECUTE` on `OBJECT_OR_COLUMN` `dbo.CreateAppointment`; `DENY INSERT`, `DENY UPDATE`, and `DENY DELETE` on `OBJECT_OR_COLUMN` `dbo.Appointments`. No schema-level `EXECUTE`, no direct write grant, no DDL permission, and no unexpected object or database permission.
- [ ] **Step 2:** Query ownership for the database, `dbo` schema, procedure, and appointment table:

    ```powershell
    docker compose -p scheduler-t3-manual exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d ServiceScheduler -Q "SET NOCOUNT ON; SELECT (SELECT SUSER_SNAME(owner_sid) FROM sys.databases WHERE name = DB_NAME()) AS database_owner, (SELECT USER_NAME(principal_id) FROM sys.schemas WHERE name = ''dbo'') AS dbo_schema_owner, (SELECT USER_NAME(principal_id) FROM sys.objects WHERE object_id = OBJECT_ID(''dbo.CreateAppointment'')) AS procedure_owner, (SELECT USER_NAME(principal_id) FROM sys.objects WHERE object_id = OBJECT_ID(''dbo.Appointments'')) AS table_owner;"'
    ```

    - **Expected:** the database owner is the privileged setup principal (for example `sa`); the `dbo` schema, `dbo.CreateAppointment`, and `dbo.Appointments` are owned by `dbo`; `scheduler_app` appears in none of the ownership columns. If `scheduler_app` owns any of them, record Failed and cite review finding R-04; do not work around it by granting direct writes.
- [ ] **Step 3 (optional, Blocked if unavailable):** Use an independent runtime connection to attempt a direct `INSERT`/`UPDATE`/`DELETE` on `dbo.Appointments` and to execute `dbo.CreateAppointment` with a valid pair.
    - **Expected:** direct DML is denied; the procedure execute succeeds for a valid interval; overlapping technician-only and bay-only direct calls are rejected. If a runtime `sqlcmd`/connection is unavailable, mark this step Blocked and cite the automated `RuntimePermissionTests`/`BookingProcedureTests`.
- [ ] **Step 4:** Re-run the permission query after the setup rerun in TC-M13.
    - **Expected:** the same exact rows; no duplicate or additional permissions.
- Cleanup: none; this case is read-only. Do not alter ownership or grants to make a failing check pass.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M12: Duration snapshot and stale catalog (optional)

> [!NOTE]
> Covers: S3-02, S3-05, AC3-11, S3-V02. Type: manual API + SQL + Redis (optional; requires the privileged setup identity and Redis CLI).

- Preconditions and synthetic data: a confirmed 60-minute Oil Change from TC-M02; `$date2` 10:00 free (`$saigonDuration`); Redis catalog cache; privileged setup access for a temporary synthetic duration update. The automated `AppointmentDurationTests` is the primary evidence; mark this case Blocked if the SQL/Redis prerequisites are unavailable. Perform Steps 2-7 within the 10-minute catalog TTL; if the cache key expires before Step 7, mark the stale-cache assertion Blocked and cite `AppointmentDurationTests`.
- [ ] **Step 1:** GET the TC-M02 appointment.
    - **Expected:** `durationMinutes` is `60`; `endsAt` is exactly 60 minutes after `startsAt`; the stored snapshot is returned.
- [ ] **Step 2:** `curl.exe -s -i "$base/api/dealerships/$saigonId/service-types"` to populate the Saigon catalog cache.
    - **Expected:** `200`; Oil Change has `durationMinutes: 60`; the Redis key `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types` exists.
- [ ] **Step 3:** With the privileged setup identity, update the Oil Change duration to `90`:

    ```powershell
    docker compose -p scheduler-t3-manual exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d ServiceScheduler -Q "SET NOCOUNT ON; UPDATE dbo.ServiceTypes SET DurationMinutes = 90 WHERE Id = ''51000000-0000-0000-0000-000000000001'';"'
    ```

    - **Expected:** one row updated; no appointment row changes; the existing appointment's stored end/duration is unaffected because it is a snapshot.
- [ ] **Step 4:** GET the TC-M02 appointment again.
    - **Expected:** still `200` with `durationMinutes: 60` and the original `endsAt`; the catalog change does not alter the persisted appointment.
- [ ] **Step 5:** POST a new Oil Change at `$saigonDuration` (`$date2` 10:00 local).
    - **Expected:** `201`; `durationMinutes` is `90`; `endsAt` is exactly 90 minutes after the start. The booking read authoritative SQL duration, not the stale cached `60`.
- [ ] **Step 6:** Request availability with `serviceTypeId=$oilChange` at `$saigonDurationProbe` (`$date2` 11:30 local).
    - **Expected:** `200`; `endsAt` is exactly 90 minutes after the start; availability uses uncached SQL duration.
- [ ] **Step 7:** Inspect the cached catalog value and then restore state:

    ```powershell
    docker compose -p scheduler-t3-manual exec -T redis redis-cli GET "scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types"
    ```

    - **Expected:** the cached payload still reports `durationMinutes: 60` while booking/availability used `90`; this demonstrates the stale cache did not influence authoritative booking. Then restore the SQL duration to `60`, delete the cache key, and re-fetch the catalog to repopulate it.
- [ ] **Step 8:** Restore the synthetic duration and cache state:

    ```powershell
    docker compose -p scheduler-t3-manual exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d ServiceScheduler -Q "SET NOCOUNT ON; UPDATE dbo.ServiceTypes SET DurationMinutes = 60 WHERE Id = ''51000000-0000-0000-0000-000000000001'';"'
    docker compose -p scheduler-t3-manual exec -T redis redis-cli DEL "scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types"
    curl.exe -s -i "$base/api/dealerships/$saigonId/service-types"
    ```

    - **Expected:** the update restores `60`; `DEL` returns `1` or `0`; the next catalog request returns `200` with `durationMinutes: 60` from SQL and repopulates the cache.
- Cleanup: confirm `DurationMinutes = 60` for the Oil Change; delete `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types`; delete the new appointment only with the privileged setup identity. Do not leave the synthetic duration update in place.
- Execution status: Not run.
- Observed result / evidence: —

### TC-M13: Repeated setup, persistence, and optional fresh project

> [!NOTE]
> Covers: S3-03, S3-06, AC3-09, AC3-13, T-16 (manual portion), S3-V06 (setup portion). Type: operational.

- Preconditions and synthetic data: stack running with at least one confirmed appointment; isolated project `scheduler-t3-manual`; optionally a second isolated project `scheduler-t3-manual-fresh`.
- [ ] **Step 1:** `docker compose -p scheduler-t3-manual down` (keeps the named volume); then `docker compose -p scheduler-t3-manual up -d --wait`.
    - **Expected:** setup reruns; migrations are already applied or report no pending migration; mapping/procedure validation and runtime provisioning succeed; shift seeding is idempotent; `db-setup` exits `0`; API health returns `200`.
- [ ] **Step 2:** GET the existing appointment.
    - **Expected:** `200` with the same stored values; repeated setup preserved appointment data.
- [ ] **Step 3:** Repeat the TC-M11 permission query.
    - **Expected:** the exact same six documented permission rows; no duplicates or extra grants from repeated setup.
- [ ] **Step 4 (optional fresh project):** `docker compose -p scheduler-t3-manual-fresh up -d --build --wait` with its own fresh volume; check health and submit one valid booking.
    - **Expected:** fresh initialization applies both migrations, validates the mapping/procedure, provisions the runtime identity, seeds shifts, and returns a valid `201`; this is the manual clean-start check. Use a separate project/volume; do not reuse the `scheduler-t3-manual` volume.
- [ ] **Step 5 (cleanup):** `docker compose -p scheduler-t3-manual-fresh down -v` only after confirming it is the isolated fresh project and no data is needed.
    - **Expected:** the fresh project's containers and volume are removed; the primary manual project's volume is untouched.
- Cleanup: leave the primary stack running or `docker compose -p scheduler-t3-manual down` (keeps the volume). Do not run `down -v` on a volume that contains data you need.
- Execution status: Not run.
- Observed result / evidence: —

## Execution record

| Date / time and zone | Revision | Case / actual action | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| — | `a666927` | Checklist generated 2026-11-02; no manual case executed. | All cases are Not run; automated suites were reproduced by the review at this revision. | Not run | This file |

No manual case is marked Passed by checklist generation. Record the actual date/time, revision, environment, action, observed result, and evidence after a human executes each case. Preserve prior records when updating the file.

## Issues and follow-up

- Review findings: [[review/3-review|Ticket 3 review]] R-01-R-04 were fixed and re-verified on 2026-11-02. R-01/R-02 now enforce the documented request-wide deadlock-retry and budget bounds in S3-04/AC3-12; R-04 adds runtime database/object ownership rejection for S3-03/S3-06/AC3-05/AC3-13; R-03 updates AC3-09/T-16 documentation to point to existing checks. No automated review finding remains open; manual execution is still required.
- Failed or blocked cases: none executed. Cases T-05/T-06, S3-V03, S3-V04, S3-V05 direct-call parity, and migration upgrade/down are automated-only because they require controlled resources, failure injection, or independent runtime procedure connections. TC-M11 Step 3 and TC-M12 are optional and may be Blocked if the runtime/privileged connection or Redis CLI is unavailable.
- Automated evidence: [[test/3-test|Ticket 3 test report]] records 133 unit and 154 integration tests passed, plus Compose startup, Redis/SQL outage, restart, clean-directory, and permission checks. The review reproduced the 133/154 automated counts at `a666927`; the 2026-11-02 re-review reran build, format, 135 unit, and 157 integration tests after fixing R-01-R-04, and all passed. The operational checks were not rerun during either review. These results supplement but do not replace manual execution.
- Documentation limitation (resolved 2026-11-02): the former `scripts/` harness references in `README.md`, `docs/architecture.md`, and `docs/setup-guide.md` were removed as part of review R-03. Use this checklist's direct HTTP/SQL/Redis commands or the automated integration tests.
- Unexecuted checks: all manual cases; the human manual checklist has not been executed. Swagger UI browser interaction is covered only by checklist steps and automated OpenAPI assertions.
- Retest revision and observed results: record the checkout revision, environment, command/action, observation, and evidence for every executed case; preserve this generated baseline when updating the file.
