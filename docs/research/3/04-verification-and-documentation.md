---
title: "3 - Appointment Booking and Retrieval: Verification and Documentation"
tags: [research, service-scheduler, booking, testing, verification, documentation]
ticket: 3
status: verified
updated: 2026-11-01
---

# 3 - Verification and documentation

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research overview: [[research/3/00-overview|Ticket 3 research overview]]
- Requirements: all S3-01-S3-06; AC3-01-AC3-13; T-01-T-16; S3-V01-S3-V06; project requirements A-01-A-03, P-01, P-04, D-01-D-04.
- Acceptance labels: `AC3-01`-`AC3-13` are research-local mappings to the ticket's 13 unlabeled acceptance bullets, defined in [[research/3/00-overview|Overview 0.2]]; they are not new ticket IDs.
- Evidence baseline: HEAD `90a6284` plus the staged 2026-11-01 ticket update; inspected test projects/fixtures, existing ticket 1/2 test patterns, testing guide, and setup/Compose commands. No ticket 3 test has been executed.

> [!IMPORTANT]
> This part defines planned coverage and evidence rules. Every ticket 3 scenario is **Not run** until implementation writes and executes it. Research-phase probes are not ticket tests.

## Test architecture

| Layer | Project / fixture | Responsibility |
| --- | --- | --- |
| Pure rules and policy | `tests/ServiceScheduler.UnitTests/Booking/` | Booking options validation, candidate ordering/skip/limit, allocator retry/deadlock/budget/cancellation mapping, parser/coverage reuse. |
| API contract | `tests/ServiceScheduler.IntegrationTests/Booking/` with `SchedulerApiFactory` | POST/GET DTOs, Location, validation, Problem Details, OpenAPI, ownership/scope, retrieval. |
| Real SQL procedure | `tests/ServiceScheduler.IntegrationTests/Booking/` with `SqlServerContainerFixture`/`SqlServerTestBase`/`SchedulingTestData` | Procedure execution, direct-call parity, conflicts, atomicity, permissions, migration/upgrade, concurrency. |
| Real Redis compatibility | Existing `SqlServerAndRedisCollection`/Redis fixture | Booking/retrieval during cache outage; stale catalog duration independence. |
| API restart | Two `SchedulerApiFactory` instances sharing one SQL database | Persisted retrieval after host restart. |
| Clean-checkout/operational | README/setup commands | Booking/retrieval checks against the Compose stack. |

Existing test infrastructure facts:

- `SchedulerApiFactory` supports in-memory configuration, an optional `TimeProvider`, and service overrides; the default runtime connection is unreachable, so tests that need SQL inject the real database connection.
- `SqlServerContainerFixture` starts one pinned SQL Server 2022 CU23 container per collection, creates one uniquely named database per test class, runs the same `DatabaseSetupRunner`, and exposes setup/runtime connection strings; `FixedClockUtc` is `2026-11-02T00:00:00Z`.
- `SqlServerTestBase` provides `CreateFactory`, `TestData`, and setup/runtime connections; `SchedulingTestData` inserts/removes synthetic probe rows through the privileged setup identity.
- Tests in a class share one database and run sequentially; each class must clean up its own rows. Concurrency/direct-call classes should have a dedicated database to avoid resource probes from other tests.
- The integration project uses xUnit v3, WebApplicationFactory, Testcontainers.MsSql/Redis, `Microsoft.Extensions.TimeProvider.Testing`, and real SQL Server/Redis; EF in-memory/SQLite must not substitute for locking/procedure/permission tests.

## Unit test coverage

| Test file | Cases | Requirements |
| --- | --- | --- |
| `Booking/BookingOptionsValidatorTests.cs` | Defaults valid; non-positive/out-of-range attempts/retries/delay/budget/command timeout rejected; command timeout >= budget rejected; boundary values. | S3-03, S3-04 |
| `Booking/BookingCandidatePlannerTests.cs` | Stable technician-then-bay order; skip a conflicted technician/bay; pair-only skip for ineligible/coverage; max attempts; exhaustion; empty lists; no duplicate pairs. | S3-03, S3-04, S3-V03 |
| `Booking/BookingAllocatorTests.cs` | First-pair success; 51001/51002 -> next pair; 51004/51005 -> pair retry; all exhausted -> capacity; 51003 -> validation; 51006 -> server error; 1205 retry twice then success; 1205 exhausted -> dependency; 1222/-2/connectivity -> dependency with one call; budget expiry -> dependency; request cancellation propagates; unknown exception propagates; no blind replay. Use a fake `IAppointmentBookingProcedure`. | S3-03, S3-04, S3-V04 |
| Existing `Availability/StartsAtParserTests.cs`, `AvailabilityIntervalTests.cs`, `OpeningCoverageTests.cs`, `ShiftCoverageTests.cs`, `HalfOpenOverlapTests.cs` | Reuse for POST parsing/time/coverage/boundary semantics; extend only if booking-specific pure logic is added. | S3-01, S3-02, T-10 |
| Existing `Errors/SqlFailureClassifierTests.cs` | Confirm 1205/1222/-2 classification remains; add procedure-number non-classification assertions if needed. | S3-04 |

The allocator fake returns procedure result statuses/error numbers; it does not prove SQL locking. Real SQL behavior is covered by the integration tests below.

## Integration test coverage

### API contract, validation, retrieval

| Test class / file | Scenarios | Requirements |
| --- | --- | --- |
| `Booking/AppointmentApiTests.cs` | Valid POST `201`, Location follow, exact response fields, POST-then-GET equality, UTC `Z`, stored duration/end/status/createdAt; GET missing/other dealership `404`; restart retrieval; repeated POST as a new attempt. | S3-01, S3-05, T-01, T-11, T-14, S3-V01, AC3-01 |
| `Booking/AppointmentValidationTests.cs` | Missing/malformed body/IDs; offset-free/date-only/unparseable `startsAt`; past/equal start; overflow; missing dealership/customer/vehicle/service; vehicle/customer mismatch; cross-dealership service; malformed route GUID; 400/404 and `code`/`traceId`; count unchanged. | S3-01, S3-02, T-10, T-11, S3-V01, AC3-02 |
| `Booking/AppointmentEligibilityTests.cs` | Unqualified/inactive technician/bay; partial shift/opening; no active pair; valid request -> `409 capacity_unavailable`; no row. | S3-02, T-02, T-03, S3-V01 |
| `Booking/AppointmentIntervalTests.cs` | Adjacent succeeds; partial overlap/containment/identical interval conflicts; technician-only and bay-only conflicts. | S3-02, T-04-T-06, AC3-03 |
| `Booking/AppointmentDstTests.cs` | London spring-forward/fall-back fixtures from ticket 2; local opening boundary; equivalent explicit offsets; SQL procedure direct-call parity. | S3-02, T-10, S3-V05 |
| `Booking/AppointmentDurationTests.cs` | Seed/prime a stale catalog value; POST uses SQL duration; change service duration; GET keeps snapshot/end; availability sees the committed appointment. | S3-02, S3-05, S3-V02, AC3-11 |

### Procedure, concurrency, permissions, setup

| Test class / file | Scenarios | Requirements |
| --- | --- | --- |
| `Booking/BookingProcedureTests.cs` | Direct runtime `SqlCommand` calls without API prechecks: valid insert; output ID/end/duration/created; 51001 technician-only; 51002 bay-only; 51003 ownership/scope/past/invalid; 51004 inactive/unqualified; 51005 shift/opening; offset normalization; snapshot checks; no partial row on failure; locks released after failure. | S3-02-S3-04, T-08, T-09, S3-V05, AC3-05, AC3-06 |
| `Booking/BookingConcurrencyTests.cs` | One eligible pair, empty `Appointments`, 20 coordinated POSTs across two factories; exact one `201`, nineteen `409`, one row; no `503`/`500`; final stored row. Optional repeated run for stability. | S3-03, T-07, AC3-04, AC3-06 |
| `Booking/BookingFailureBoundaryTests.cs` | Hold technician row with `UPDLOCK, HOLDLOCK`; direct procedure lock timeout `1222` -> no row; direct command timeout `-2` -> no row; direct cancellation -> no row and a later valid call succeeds; fake/allocator mapping of lock/dependency errors to `503`; allocator no-retry for dependency/unknown commit; unexpected SQL error -> `500`, not `409`. | S3-03, S3-04, S3-V04, AC3-06, AC3-12 |
| `Security/RuntimePermissionTests.cs` (extend) | Runtime `scheduler_app`: schema SELECT, object procedure `EXECUTE`; direct insert/update/delete denied with permission errors; no schema EXECUTE, DDL, or roles; exact `sys.database_permissions` rows; procedure execution succeeds. | S3-03, S3-06, T-08, AC3-05 |
| `Booking/BookingSetupTests.cs` | Fresh setup; repeated setup idempotent; unexpected extra permission/role -> setup fails; procedure missing -> clear failure; mapping invalid -> setup fails. | S3-06, S3-V06 |
| `Booking/BookingMigrationUpgradeTests.cs` | Apply `20261031092016_InitialCreate`, insert a synthetic appointment, apply ticket 3 migration, rerun setup; appointment preserved; mapping/FK/procedure/grant state; down migration drops objects but preserves appointments; manual revoke recovery. | S3-06, S3-V06, AC3-13 |
| `Booking/BookingOutageTests.cs` | Redis unreachable/paused: booking/retrieval succeed; SQL unreachable: POST `503`, readiness `503`, no row; restart retrieval. | S3-05, S3-06, T-13, T-14, AC3-07 |
| `OpenApi/OpenApiTests.cs` (extend) | POST/GET paths, `CreateAppointmentRequest`/`AppointmentResponse` schemas, status sets, `capacity_unavailable`, Location description. | S3-01, S3-06, S3-V01 |
| `Startup/ProblemDetailsTests.cs` (extend) | Booking 400/404/409/503/500 with `code`/`traceId`, no stack/credentials. | S3-01, S3-04, S3-05, S3-V01 |

## Requirement scenario matrix

Status is **Not run** for every row until implementation records actual evidence.

| Case | Test target | Setup / action | Expected | Requirements | Status |
| --- | --- | --- | --- | --- | --- |
| T-01 | `AppointmentApiTests` | Fixed clock; seeded Saigon customer/vehicle/service; future start inside shift; POST; follow Location; GET. | `201`; complete DTO; location follows; GET matches; duration/end snapshot correct. | A-01, A-02, A-03 | Not run |
| T-02 | Unit + `AppointmentEligibilityTests` | Inactive/unqualified technician/bay probes; block other pairs. | Valid request -> `409 capacity_unavailable`; no row. | A-02 | Not run |
| T-03 | Unit + `AppointmentEligibilityTests` | Partial shift/opening probes. | Resources free at start but not later cannot book; `409`. | A-02 | Not run |
| T-04 | Unit + `AppointmentIntervalTests` | Existing appointment boundaries; adjacent/partial/contained/identical requests. | Adjacent succeeds; overlaps conflict for either resource. | A-02, P-01 | Not run |
| T-05 | `AppointmentIntervalTests` / `BookingProcedureTests` | Same technician, different bay; overlapping direct/API call. | `51001`; `409` at API; no row. | P-01 | Not run |
| T-06 | `AppointmentIntervalTests` / `BookingProcedureTests` | Different technician, same bay; overlapping direct/API call. | `51002`; `409` at API; no row. | P-01 | Not run |
| T-07 | `BookingConcurrencyTests` | One eligible pair; empty table; 20 tasks across two factories released together. | Exactly one `201`, nineteen `409`, one stored row; no `503`/`500`. | P-01 | Not run |
| T-08 | `BookingProcedureTests` + `RuntimePermissionTests` | Independent runtime connection; direct procedure calls; direct DML. | Tech-only/bay-only overlaps rejected; valid execution succeeds; direct insert/update/delete denied. | P-01 | Not run |
| T-09 | `BookingProcedureTests` | Conflict/validation failure; count rows; then valid call. | No partial/confirmed row; later valid call succeeds (locks released). | A-03, P-01 | Not run |
| T-10 | Unit + `AppointmentValidationTests` + `AppointmentDstTests` | Past/offset-free; equivalent `Z`/`+07:00`; London DST fixtures. | 400 for invalid; equivalent instants normalize; local opening boundaries match policy. | A-01, A-02 | Not run |
| T-11 | `AppointmentValidationTests` + `BookingProcedureTests` | Vehicle/customer mismatch; cross-dealership service/resource; retrieval through another dealership. | 400/404 as documented; direct procedure 51003/51004; retrieval `404`. | A-01, A-03 | Not run |
| T-13 | `BookingOutageTests` | Stop/pause Redis; POST/GET. | Booking/retrieval unaffected; SQL authoritative. | P-01 | Not run |
| T-14 | `BookingOutageTests` + restart | Restart API on same SQL database; SQL outage. | Appointment survives; SQL outage POST `503`, readiness `503`, no confirmation. | A-03, P-04 | Not run |
| T-16 | README/setup | Clean checkout; compose setup; booking/retrieval checks. | Documented commands work; checks use seeded IDs and generated future slots. | D-02-D-04, P-04 | Not run |
| S3-V01 | `AppointmentApiTests`, `AppointmentValidationTests`, `OpenApiTests`, `ProblemDetailsTests` | Contract/OpenAPI/error matrix. | Exact fields; Location; UTC `Z`; 400/404/409/503/500 codes/trace IDs; schemas. | S3-01, S3-04, S3-05 | Not run |
| S3-V02 | `AppointmentDurationTests` | Prime stale catalog; change SQL duration; POST/GET/availability. | Booking duration from SQL; GET keeps snapshot; availability sees committed row. | S3-02, S3-05 | Not run |
| S3-V03 | `BookingAllocatorTests` + `AppointmentIntervalTests` | Fake first-pair conflict then success; blocked first candidate; exhaustion. | Second pair succeeds; exhaustion -> `409`; documented limit. | S3-03, S3-04 | Not run |
| S3-V04 | `BookingAllocatorTests` + `BookingFailureBoundaryTests` | Deadlock fake; held-lock lock timeout; command timeout; cancellation; dependency/unknown-commit no-retry; unexpected error. | Bounded retries/timeouts; cancellation releases resources; no blind replay; unexpected not `409`. | S3-03, S3-04 | Not run |
| S3-V05 | `BookingProcedureTests` + `AppointmentDstTests` | Direct runtime procedure calls without API checks, including offset/DST fixtures. | Ownership/scope/duration/eligibility/coverage/future/conflicts enforced; errors 51001-51006 as applicable. | S3-02, S3-03 | Not run |
| S3-V06 | `BookingSetupTests`, `BookingMigrationUpgradeTests`, `RuntimePermissionTests` | Fresh/repeated setup; ticket 2 upgrade; excess privilege; down migration. | Data retained; exact grants/denies; excessive privileges rejected; recovery documented. | S3-03, S3-06 | Not run |

## Test data design

### Seeded identifiers and fixed clock

Use the stable synthetic seeds:

| Item | ID |
| --- | --- |
| Saigon dealership | `a1000000-0000-0000-0000-000000000001` |
| London dealership | `a1000000-0000-0000-0000-000000000002` |
| Customer An Nguyen | `c1000000-0000-0000-0000-000000000001` |
| Vehicle An Nguyen | `b1000000-0000-0000-0000-000000000001` |
| Saigon Oil Change (60) | `51000000-0000-0000-0000-000000000001` |
| Saigon Brake Service (120) | `51000000-0000-0000-0000-000000000002` |
| London Annual Service (90) | `51000000-0000-0000-0000-000000000003` |
| Saigon active technician | `71000000-0000-0000-0000-000000000001` |
| Saigon active bay | `81000000-0000-0000-0000-000000000001` |

The fixed test clock `2026-11-02T00:00:00Z` and the setup-seeded 28-day weekday 09:00-17:00 shift horizon are the baseline. Saigon normal requests use `2026-11-03T03:00:00Z` (10:00 local, 60-minute service ends 11:00 local) inside the seeded shift/opening. London DST tests use the ticket 2 fixtures and add Sunday opening/shift rows.

### Probe rows

- Use `SchedulingTestData`/setup connection to add a second active technician, bay, qualification, shift, opening row, customer/vehicle, or appointment as needed.
- Use `Guid.NewGuid()` for probe IDs and delete only the test's own rows. Appointment probes must satisfy the duration/end/status/UTC checks.
- Keep the concurrency class's database free of extra active/qualified resources so exactly one pair exists.
- For technician-only conflict, add a second active bay and use the seeded technician; for bay-only conflict, add a second active qualified technician and use the seeded bay.
- For alternative candidates, add a second pair with names that sort after/before as needed; use the fake procedure boundary for deterministic first-pair conflict then success.
- For setup/upgrade tests, use a dedicated database and `IMigrator` to target `20261031092016_InitialCreate`; insert a synthetic appointment; then migrate to latest and rerun setup.
- For lock timeout/cancellation, hold the seeded technician row with `UPDLOCK, HOLDLOCK` in a setup connection; release in `finally`.
- For SQL outage, use an unreachable runtime connection (existing pattern) or a dedicated stopped-container test; do not stop a shared SQL container during other tests.
- For Redis outage, use the existing Redis fixture's pause/stop pattern; booking/retrieval must not consult Redis.

### DST fixtures

| Case | Setup | Request | Expected |
| --- | --- | --- | --- |
| London spring forward | Sunday 2027-03-28 opening `02:00-04:00`; active London technician shift `2027-03-28T01:00Z`-`03:00Z`; active bay; annual service 90 min | `2027-03-28T01:30:00Z` | Local `02:30-04:00`; opening/shift end boundary allowed; `201`/procedure success. |
| London fall back | Sunday 2027-10-31 opening `00:00-04:00`; shift `2027-10-31T00:00Z`-`02:00Z`; annual service 90 min | `2027-10-31T00:30:00Z` | Local start `01:30+01:00`, end `02:00+00:00`; shift/opening coverage; success. |
| Fall-back outside | Same fall-back setup | `2027-10-31T03:00:00Z` | Local end after opening/shift end; `409`/`51005`. |
| Equivalent offset | Normal Saigon slot | `2026-11-03T10:00:00+07:00` and `2026-11-03T03:00:00Z` | Same instant; both accepted; stored UTC; same end. |

The procedure uses the IANA-to-Windows mapping; setup parity checks and these tests cover the .NET/SQL gap.

## Failure injection and limitations

| Behavior | Test approach | Limitation |
| --- | --- | --- |
| Candidate conflict / alternative pair | Fake `IAppointmentBookingProcedure` returns 51001/51002 for the first pair and success for the second; SQL test blocks the first ordered candidate. | SQL test may see the candidate query exclude a pre-existing block rather than a race; the fake covers the race policy deterministically. |
| Deadlock `1205` | Fake procedure boundary returns 1205 twice then success; returns 1205 past the retry budget -> 503. Optionally induce a real deadlock with opposing lock order. | A real deadlock is timing-dependent; the fake is the deterministic policy test. |
| Lock timeout `1222` | Hold the technician row with `UPDLOCK, HOLDLOCK`; call the procedure directly with a runtime connection; wait for the 5 s procedure lock timeout. Test the API `503` mapping with a fake procedure result. | Adds ~5 s to the suite; ensure cleanup in `finally`. The API precheck would block on the held row, so it is not the injection point. |
| Command timeout `-2` | Direct procedure call with `SqlCommand.CommandTimeout=1s` while a lock is held; test the API `503` mapping with a fake procedure result. | Test-specific command timeout; does not change production defaults. |
| Cancellation | Direct procedure call with a cancellation token while the technician row is held; cancel, release, assert no row, then issue a valid call. Test API token propagation with a fake boundary that waits on the token. | Cancellation after commit can leave a row; the test covers pre-commit cancellation. |
| Unknown commit | Fake boundary returns a non-1205 dependency error; assert exactly one call and 503; inspect code. | A true lost commit acknowledgment requires network fault injection not available locally; document as unexecuted limitation. |
| Unexpected SQL error | Fake/real procedure returns an unclassified SQL error; assert 500, not 409. | Use a controlled invalid mapping/constraint probe or fake boundary. |
| Budget expiry | Fake procedure delays; advance `FakeTimeProvider` or use a short test budget; expect 503 and no further calls. | Ensure the budget timer uses the injected clock; otherwise use a short real budget with a bounded test. |
| 20-way concurrency | Two factories, one pair, barrier; final DB count. | Timing-sensitive; use generous lock/command timeouts and assert no `503` in the expected run. |

Do not use arbitrary sleeps for coordination; use `TaskCompletionSource`/barriers and bounded polling. A failed/blocked required check prevents implementation completion; record it rather than weakening the assertion.

## Commands

Planned commands from the repository root (verify actual paths after implementation):

```powershell
dotnet restore
dotnet build --no-restore
dotnet format --verify-no-changes
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Booking"
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Booking"
```

Compose/operational commands after setup:

```powershell
docker compose config
docker compose up -d --wait
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
docker compose stop redis
docker compose start redis
docker compose logs db-setup
docker compose logs api
```

Migration inspection:

```powershell
dotnet tool restore
dotnet ef migrations script --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
dotnet ef migrations list --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
```

Commands are planned until the files exist and are executed; record actual commands and results in `docs/test/3-test.md`.

## Evidence and documentation

- Record executed unit/API/SQL/Redis/operational results in `docs/test/3-test.md` using [[test/README|Testing guidance]] and the backend/integration templates: revision, environment, dependency versions, command, scenario, expected vs observed, status (Passed/Failed/Blocked/Not run), and evidence links.
- Do not mark an acceptance criterion or manual case Passed from a generated checklist, a build, or a code review. The orchestrator `test` phase later generates `docs/test/3-manual-test.md`; new manual cases remain Not run/Blocked.
- Keep research probes, automated tests, manual checks, and unexecuted checks distinct. A research probe does not satisfy T-xx/S3-Vxx.
- Create `docs/deploy-notes/3-deploy-notes.md` with migration order, grants/denies, repeated setup, ticket 2 upgrade, rollback/revoke, recovery, and verification steps; record actual local execution.
- Update README/architecture/setup/database schema/overview/migration/cache/docs index in the same implementation change; link actual migration ID, procedure/errors, config, and evidence.

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | `tests/ServiceScheduler.UnitTests/` | No booking options/planner/allocator tests. | Add booking unit tests and fake procedure boundary. | S3-03, S3-04 |
| Must Change | `tests/ServiceScheduler.IntegrationTests/Booking/` | No booking API/SQL/concurrency/setup tests. | Add booking integration tests and fixtures. | S3-01-S3-06 |
| Must Change | `Security/RuntimePermissionTests.cs`, `OpenApiTests.cs`, `ProblemDetailsTests.cs` | No booking/procedure/`409` assertions. | Extend. | S3-01-S3-06 |
| Must Change | `docs/test/3-test.md`, `docs/deploy-notes/3-deploy-notes.md`, docs index | No ticket 3 evidence/records. | Create and link with actual results. | S3-06 |
| Must Change | README/architecture/setup/database docs | Booking remains planned. | Record implemented behavior/evidence. | S3-06 |
| No Change | Existing ticket 1/2 tests and fixtures | Valid baseline; reusable patterns. | Keep; extend only where booking needs new hooks. | S3-01-S3-06 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| V3-01 | Risk | 20-way concurrency test may be slow/flaky. | One pair, short transactions, generous 5 s lock/10 s command/20 s budget, two factories, barrier; assert no `503`; retry the suite only if an infrastructure failure is proven, not by weakening assertions. | Resolved in design; execute. |
| V3-02 | Risk | Real deadlock/unknown-commit injection is hard. | Fake procedure boundary for deterministic policy; real lock timeout/cancellation; document the true unknown-commit limitation. | Resolved; limitation documented. |
| V3-03 | Risk | Lock timeout test adds ~5 s. | Accept for one integration test; cleanup in `finally`; no arbitrary sleeps for coordination. | Resolved. |
| V3-04 | Risk | Fixed clock/shift horizon may expire. | Tests use the fixture's fixed clock and seeded horizon. | Resolved; document fixture refresh. |
| V3-05 | Risk | SQL/Redis outage tests can affect shared containers. | Use unreachable configuration or a dedicated fixture/collection; never stop a shared container mid-suite. | Resolved. |
| V3-06 | Risk | PowerShell concurrency compatibility. | Use a PS5.1-compatible job/runspace approach or document PS7; test on the available host. | Resolved in plan; verify. |
| V3-07 | Risk | POST/GET equality could hide a wrong source. | Assert stored values through GET and direct SQL/procedure output; change catalog duration after booking. | Resolved. |
| V3-08 | Risk | Direct-call tests use runtime principal; setup probes use privileged identity. | Keep setup probes/cleanup on setup connection; direct calls/permission denial on runtime connection. | Resolved. |
| V3-09 | Risk | Test report may conflate research probes with executed tests. | Separate sections and statuses; link probe evidence only as design support. | Resolved. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S3-01, S3-05, AC3-01, AC3-10 | API contract, Location, retrieval, restart, OpenAPI, Problem Details. | Unit + API + SQL |
| S3-02, S3-03, AC3-02-AC3-06 | Eligibility, coverage, conflicts, procedure locks/atomicity, direct-call parity, concurrency. | Unit + API + SQL |
| S3-04, AC3-06, AC3-12 | Retry/deadlock/timeout/budget/cancellation/unknown-commit policy. | Unit + API + SQL |
| S3-06, AC3-07-AC3-13 | Setup/permissions/upgrade/recovery, outages, docs, clean checkout. | SQL + Redis + smoke + docs |
| T-01-T-16, S3-V01-S3-V06 | Full scenario matrix above. | As listed |

All checks are **Not run**. The next phase after research verification is planning; implementation then writes and executes these checks and records evidence in `docs/test/3-test.md`.
