---
title: "2 - Service Discovery and Availability: Test Report"
tags: [test, service-scheduler, api, availability, redis]
ticket: 2
status: executed
updated: 2026-11-01
---

# 2 - Service Discovery and Availability: Test Report

## Scope and prerequisites

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/00-overview|Ticket 2 research overview]] with Parts 1-4; plan: [[plans/2/README|Ticket 2 implementation plan]], verified 2026-11-01.
- Revision: checkout HEAD `3f8ab1e` plus the ticket 2 working-tree changes recorded on 2026-11-01.
- Requirement coverage: S2-01-S2-06, AC2-01-AC2-07, T-02-T-04, T-10-T-13; project A-02, D-01-D-04, P-02, P-04.
- Exclusions: booking confirmation/retrieval, the booking procedure and concurrency (ticket 3); Serilog/OpenTelemetry/Prometheus/Tempo/Grafana (ticket 4); authentication, admin CRUD, cancellation/rescheduling, idempotency, vehicle-overlap protection, cache stampede protection, and a custom frontend.
- Environment: Windows 11 host; .NET SDK `10.0.403`; Docker server `25.0.3`; Compose `v2.24.6-desktop.1`; SQL Server image `mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04@sha256:0ec7739e1c5ec2f57861facbe1f2b74f1d3e147c7c97edf91eeea920c5944d9c`; Redis image `redis:7.4.12-alpine@sha256:8da19c7399dca900c6822a1cf30f9d2285585e2c4b457ff4095b0f4500054d3f`; `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13`; `StackExchange.Redis` `2.7.27`; `Testcontainers.MsSql`/`Testcontainers.Redis` `4.14.0`.
- Migration/seed baseline: `20261031092016_InitialCreate`; static seeds plus the fixed-clock 28-day weekday shift horizon; no ticket 2 migration.
- Test projects and commands: `tests/ServiceScheduler.UnitTests` and `tests/ServiceScheduler.IntegrationTests`, run from the repository root with the commands in the execution record below.
- Isolation: one SQL Server container per SQL collection, one database per test class, one Redis container per combined collection, a distinct Redis `defaultDatabase` index per cache class, fixed API clock `2026-11-02T00:00:00Z`, synthetic probe rows inserted through the privileged setup identity, and test-owned cleanup. Integration tests are not parallelized.
- Configuration used: `ConnectionStrings:Runtime`, `ConnectionStrings:Cache`, and `Scheduler:Cache:*` as documented; cache tests override the catalog TTL to one second only for expiry.

## Requirement coverage

| Requirement | Test IDs | Level | Primary test file / evidence | Status |
| --- | --- | --- | --- | --- |
| S2-01, AC2-01 | TC2-01-TC2-08, TC2-33 | API + SQL + Redis + OpenAPI/operational | `Catalog/*`, `OpenApi/OpenApiTests` (TC2-35 retired) | Passed |
| S2-02, AC2-02, AC2-03, T-10 | TC2-12-TC2-14, TC2-22, TC2-23 | Unit + API + SQL + Redis | `Availability/AvailabilityTimeTests`, `AvailabilityDurationTests`, `AvailabilityDstTests`; unit rule tests | Passed |
| S2-03, AC2-02, T-02-T-04, T-11 | TC2-15-TC2-21, TC2-24, TC2-25 | Unit + API + real SQL Server | `Availability/AvailabilityEligibilityTests`, `CoverageTests`, `ConflictTests`, `ReadOnlyTests`, `ScopeTests`, `SqlTests` | Passed |
| S2-04, AC2-06, T-13 | TC2-26-TC2-29, TC2-34 | Unit + API + SQL + Redis | `Resilience/*`, `Availability/AvailabilityFailureTests`, `Startup/ProblemDetailsTests`; classifier unit tests | Passed |
| S2-05, AC2-04, AC2-05, T-12, T-13 | TC2-02, TC2-05-TC2-11, TC2-30-TC2-32 | Unit + API + real Redis + SQL | `Catalog/CatalogCacheTests`, `Resilience/RedisOutageTests`, `Health/CacheHealthTests`, `Startup/CacheConfigurationTests`, `Caching/BoundedCacheTests` | Passed |
| S2-06, AC2-07 | TC2-33, documentation review | API + operational + documentation | OpenAPI tests, README/setup/architecture/database/cache/deploy records (TC2-35 retired) | Passed for automated/operational scope; manual checklist pending |

## Test cases

| Test ID | Requirement | Level | Test file / actual check | Expected | Observed | Status |
| --- | --- | --- | --- | --- | --- | --- |
| TC2-01 | S2-01, AC2-01 | API + SQL | `Catalog/DealershipDiscoveryTests` | `200`; `{id,name,timeZoneId}` ordered by name/id; seeded IANA zones. | London then Saigon; expected fields and zones. | Passed |
| TC2-02 | S2-01, S2-05 | API + Redis + SQL | `Catalog/CatalogCacheTests.MissPopulates...` | Miss returns ordered catalog and populates the exact v1 key. | Two services in name/ID order; key contained the same v1 payload. | Passed |
| TC2-03 | S2-01, AC2-01 | API + Redis + SQL | `Catalog/CatalogContractTests.EmptyCatalog...` | Existing empty dealership `200 []` and cached; unknown dealership `404` and no key. | Observed `200 []`, cached `[]`, `404 not_found`, no key. | Passed |
| TC2-04 | S2-01, S2-04, T-11 | API | `Catalog/CatalogContractTests` malformed/unknown cases | Malformed UUID `400 validation_error`; valid unknown `404 not_found`; trace IDs present. | Observed exactly those statuses/codes and trace IDs. | Passed |
| TC2-05 | S2-05, AC2-04 | API + Redis + SQL | `Catalog/CatalogCacheTests.HitReturnsTheCachedPayload...` | Cached payload/order returned without SQL. | `200` with the cached-only service while runtime SQL pointed at an unreachable endpoint. | Passed |
| TC2-06 | S2-05, AC2-04, T-12 | API + Redis + SQL | `Catalog/CatalogCacheTests.Expiry...` | One-second TTL expires; next request reads SQL and repopulates. | Bounded polling observed key removal; renamed SQL value returned and key repopulated. | Passed |
| TC2-07 | S2-05, AC2-04, T-12 | API + Redis + SQL | `Catalog/CatalogCacheTests.ExplicitInvalidation...` | `DEL`/remove causes a fresh SQL read and repopulation. | New SQL name returned; key present afterward. | Passed |
| TC2-08 | S2-01, S2-05, T-11 | API + Redis + SQL | `Catalog/CatalogCacheTests.DealershipCacheKeysAreIsolated` | Dealership-scoped keys; one update does not affect the other. | Distinct keys; Saigon refresh did not change London's cached catalog. | Passed |
| TC2-09 | S2-05, AC2-05, T-13 | API + SQL + Redis | `Resilience/RedisOutageTests.ClosedRedisEndpoint...` | Catalog `200` via SQL within budget; availability correct; warning logged; readiness `200` Degraded. | Observed `200` fallback, correct availability, captured warning, readiness Degraded/200; the tightened assertion requires the catalog fallback to complete in under 3 seconds. | Passed |
| TC2-10 | S2-05, AC2-05, T-13 | API + SQL + Redis | `Resilience/RedisOutageTests.PausedRedis...` | Concurrent requests bounded; no unbounded backlog; recovery restores a hit. | A single paused-cache request returned under 3 seconds; five concurrent requests returned `200` under 10 seconds; after `UnpauseAsync` the key repopulated and a later request hit it. | Passed |
| TC2-11 | S2-05 | Unit + API + Redis | `UnitTests/Caching/BoundedCacheTests`, `Catalog/CatalogCacheTests.CorruptCachedPayload...` | Hit/miss, budget expiry, cancellation, Redis failure, unexpected exception, write/remove failure, corrupt payload repair. | 9 unit tests passed, including cancellation propagation and non-swallowed `InvalidOperationException`; the API returned SQL data and repaired the key after invalid cached JSON. | Passed |
| TC2-12 | S2-02, AC2-02, T-10 | API + SQL | `Availability/AvailabilityTimeTests.EquivalentExplicitOffsets...` | `Z` and `+07:00` normalize to the same UTC interval; `Z` timestamps; captured clock. | Same `startsAt`/`endsAt`; all timestamps ended with `Z`; `evaluatedAt=2026-11-02T00:00:00Z`. | Passed |
| TC2-13 | S2-02, AC2-02, T-10 | API | `AvailabilityTimeTests.InvalidOrNonFutureStarts...` | Offset-free/unparseable/date-only/past/equal `400 validation_error` with trace ID. | All five cases returned `400` and `validation_error`. | Passed |
| TC2-14 | S2-02, AC2-02 | API + SQL | `AvailabilityTimeTests.ComputedEndOverflow...` | Computed end beyond range `400 validation_error`. | Far-future start returned `400 validation_error`. | Passed |
| TC2-15 | S2-02, S2-03, AC2-02, T-02 | API + SQL | `AvailabilityEligibilityTests.SeededQualified...` | Qualified active pair `available:true`; end from SQL duration. | `available:true`; `endsAt` exactly one hour after start. | Passed |
| TC2-16 | S2-03, AC2-02, T-02 | API + SQL | `AvailabilityEligibilityTests.Unqualified...` | Unqualified technician never satisfies availability. | `available:false` with seeded technician blocked. | Passed |
| TC2-17 | S2-03, AC2-02, T-02 | API + SQL | `AvailabilityEligibilityTests.InactiveTechnicianWithACoveringShift...`, `InactiveBayNeverSatisfiesAvailability` | Inactive technician/bay never satisfy availability; both outcomes use the six documented fields and no resource IDs. | The inactive technician had a covering shift and the inactive bay was the only remaining bay; both returned `available:false` with the documented fields and no resource IDs. | Passed |
| TC2-18 | S2-02, S2-03, AC2-02, T-03 | API + SQL | `AvailabilityCoverageTests` | Partial shift/opening, a shift starting after the request start, and adjacent opening rows do not cover the interval. | All four scenarios returned `available:false`. | Passed |
| TC2-19 | S2-03, AC2-02, T-04 | API + SQL | `AvailabilityConflictTests` | Technician-only and bay-only conflicts; free different pair; adjacent allowed. | Each scenario matched the expected independent result. | Passed |
| TC2-20 | S2-03, AC2-02, T-04 | API + SQL | `AvailabilityConflictTests.OverlappingAppointments...` | Partial overlap, containment, identical, and reverse-containment are unavailable; no reservation created. | All five overlap cases returned `available:false`. | Passed |
| TC2-21 | S2-03, AC2-02, AC2-03, T-04 | API + SQL + Redis | `AvailabilityReadOnlyTests` | Appointment count unchanged; no cache key created. | Counts equal; catalog key absent after availability. | Passed |
| TC2-22 | S2-02, AC2-03 | API + Redis + SQL | `AvailabilityDurationTests` | Stale cached duration ignored; `endsAt` uses SQL duration. | `endsAt` was start + 60 minutes while the cached catalog advertised 999. | Passed |
| TC2-23 | S2-02, AC2-02, T-10 | API + SQL | `AvailabilityDstTests` | Spring-forward gap, nonexistent boundary, fall-back repeated hour, and outside-window policy. | Spring/nonexistent/fall cases available; outside-window case `available:false`. | Passed |
| TC2-24 | S2-03, AC2-02, T-11 | API + SQL | `AvailabilityScopeTests` | Unknown/cross-dealership references `404 not_found`; foreign resources never satisfy. | Three `404 not_found` cases; blocked Saigon resources with free London resources returned `available:false`. | Passed |
| TC2-25 | S2-03, AC2-02 | Unit/SQL query-shape | `Availability/AvailabilitySqlTests` | Generated SQL contains dealership/active/service predicates, shift-bound and half-open comparison operators, `Status = Confirmed`, `NOT EXISTS`, and stable ordering. | All four query shapes asserted the predicates and operators; a shift starting after the request start returned `available:false`; index mapping documented in [[database/schema\|Schema]]. Execution plan not captured (see limitations). | Passed with limitation |
| TC2-26 | S2-04, AC2-06 | API + SQL | `Resilience/SqlOutageTests.SqlDependentReads...` | Discovery/availability/catalog miss `503 dependency_unavailable`; no false availability; no cache write. | Observed `503`/`dependency_unavailable` for all three; no key written; readiness `503`; liveness `200`. | Passed |
| TC2-27 | S2-04, AC2-06 | API + Redis + SQL | `SqlOutageTests.CatalogHitReturnsCachedData...` | Catalog hit `200` while SQL down; discovery/availability/readiness fail. | Cached catalog `200`; discovery/availability `503`; readiness `503`. | Passed |
| TC2-28 | S2-05, AC2-05, AC2-06 | API + Redis + SQL | `Resilience/CombinedOutageTests` plus Compose stop/start checks | Both dependencies down fail safely; recovery restores service. | Automated outage host returned live `200`, ready `503`, catalog `503`; a recovered host served `200`. Compose separately verified Redis stop/start and SQL stop/start recovery in one host. | Passed with limitation |
| TC2-29 | S2-04 | API + SQL | `Availability/AvailabilityFailureTests` | Invalid-zone data returns sanitized `500 internal_error` with no secrets. | `500` with `internal_error`, trace ID, and no stack/password/connection text. | Passed |
| TC2-30 | S2-05, AC2-05 | API + Redis + SQL | `Health/CacheHealthTests` | Readiness body/liveness across SQL/Redis combinations. | Healthy, Degraded, Unhealthy, and both-down cases matched; live always `200`. | Passed |
| TC2-31 | S2-05, AC2-05 | API + Redis + SQL | `CacheHealthTests.RedisDown...`, `RedisOutageTests` | Host starts with unreachable Redis; SQL-backed catalog/availability work; live `200`; cache Degraded. | Observed exactly; Compose also started with Redis down and served SQL-backed requests. | Passed |
| TC2-32 | S2-05 | API | `Startup/CacheConfigurationTests` | Missing/malformed cache configuration fails startup naming the key. | Both cases failed startup with `ConnectionStrings:Cache`; no credential value in the error. | Passed |
| TC2-33 | S2-04, S2-06, AC2-01 | API | `OpenApi/OpenApiTests.TicketTwoOperations...` | Three operations, DTOs, statuses, ordering, explicit-offset text, required parameters, Problem Details extensions. | All assertions passed; the runtime document marked `dealershipId`, `serviceTypeId`, and `startsAt` required, contained the three response DTO schemas, and documented `ProblemDetails.code`/`traceId`. | Passed |
| TC2-34 | S2-04 | API | `Startup/ProblemDetailsTests` | Missing/malformed parameters and unknown route use stable `code`/`traceId`; no secrets. | All cases returned the documented `400`/`404` Problem Details. | Passed |
| TC2-35 | S2-06, AC2-01 | Retired scope | Standalone PowerShell demo intentionally removed | No script deliverable under the 2026-11-01 amendment. | Previous script execution records are historical only and do not establish current coverage. Automated API cases and TC2-33 remain required. | Retired |

## Execution record

| Date and zone | Revision | Actual command / check | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| 2026-11-01 (local) | HEAD `3f8ab1e` + working tree | `dotnet restore` | Restore completed; projects up to date after package additions. | Passed | Command output |
| 2026-11-01 | same | `dotnet build --no-restore` | Build succeeded; 0 warnings, 0 errors. | Passed | Command output |
| 2026-11-01 | same | `dotnet format --verify-no-changes` | Passed after `dotnet format` normalized import ordering in existing files. | Passed | Command output |
| 2026-11-01 | same | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | 98 passed, 0 failed, 0 skipped. | Passed | Final run |
| 2026-11-01 | same | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | 92 passed, 0 failed, 0 skipped after review fixes; real SQL Server and Redis Testcontainers. | Passed | Post-review run |
| 2026-11-01 | post-review | `dotnet build --no-restore`; `dotnet format --verify-no-changes` | Build succeeded with 0 warnings/errors; format verification passed. | Passed | Post-review run |
| 2026-11-01 | post-review | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | 98 passed, 0 failed, 0 skipped. | Passed | Post-review run |
| 2026-11-01 | post-review | `GET /openapi/v1.json` from the rebuilt Compose API | `dealershipId`, `serviceTypeId`, and `startsAt` all reported `required: true`; `DealershipResponse`, `ServiceTypeResponse`, `AvailabilityResponse`, and `ProblemDetails` schemas present. | Passed | Runtime document check |
| 2026-11-01 | post-review | Compose `stop/start redis`; readiness/catalog/availability | Readiness `200` Degraded while Redis down; catalog and availability `200`; readiness `Healthy` after restart; catalog `200`. | Passed | Post-review Compose rerun |
| 2026-11-01 | post-review | Compose `stop/up sqlserver`; discovery/availability/catalog/readiness/liveness | Discovery and availability `503 dependency_unavailable`; catalog hit `200`; readiness `503`; liveness `200`; readiness `Healthy` and discovery `200` after restart. | Passed | Post-review Compose rerun |
| 2026-11-01 | post-review | `powershell -File scripts/ticket2-demo.ps1 -BaseUrl http://127.0.0.1:8080` | Script discovered IDs, computed `2026-11-02T03:00:00Z`, verified the equivalent `+07:00` instant, printed `not_found`/`validation_error` examples, exited `0`. | Historical (retired scope) | Post-review script run |
| 2026-11-01 | same | `docker compose --env-file .env.example config` | Configuration valid; Redis service and `ConnectionStrings__Cache` resolved. | Passed | Compose config |
| 2026-11-01 | same | `docker compose --env-file .env.example build`, `up -d --wait` | Images built after aligning the Docker SDK pin; setup exited successfully; API started. | Passed | Compose output |
| 2026-11-01 | same | `docker compose exec -T redis redis-cli ping` | `PONG`. | Passed | Command output |
| 2026-11-01 | same | GET `/health/live`, `/health/ready` | Warm readiness `{"status":"Healthy","checks":[database,redis]}`; liveness `200`. First readiness immediately after API restart was `Unhealthy` while the SQL check warmed, then `Healthy`. | Passed with cold-start note | HTTP bodies |
| 2026-11-01 | same | GET catalog twice; inspect Redis key | Ordered catalog; key `scheduler:v1:dealership:a1000000-0000-0000-0000-000000000001:service-types` present. | Passed | HTTP/Redis output |
| 2026-11-01 | same | `docker compose stop redis`; GET readiness/catalog/availability/live | Readiness `200` Degraded (`redis: Degraded`); catalog `200` from SQL (first 2.15 s, warm 0.047 s); availability `200`; liveness `200`. | Passed | HTTP output |
| 2026-11-01 | same | `docker compose start redis`; GET readiness/catalog | Readiness `Healthy`; catalog `200`; key present. | Passed | HTTP output |
| 2026-11-01 | same | `redis-cli DEL <key>`; GET catalog; `EXISTS` | `DEL=1`, `EXISTS=0`; next catalog `200`; `EXISTS=1`. | Passed | Redis/HTTP output |
| 2026-11-01 | same | `docker compose stop sqlserver`; GET discovery/availability/catalog hit/readiness/live | Discovery and availability `503 dependency_unavailable`; catalog hit `200`; readiness `503 Unhealthy (database)`; liveness `200`. | Passed | HTTP bodies |
| 2026-11-01 | same | `docker compose up -d --wait sqlserver`; GET readiness/discovery | Readiness `Healthy`/`200`; discovery `200`. | Passed | HTTP bodies |
| 2026-11-01 | same | `powershell -File scripts/ticket2-demo.ps1 -BaseUrl http://127.0.0.1:8080` | Discovered seeded IDs, computed a future weekday start, verified the equivalent offset, printed error examples, exited `0`. | Historical (retired scope) | Script output |
| 2026-11-01 | same | `docker compose down` | Containers/network removed; named SQL volume retained. | Passed | Compose output |

## Deviations and implementation notes

- **SQL error 258:** Runtime verification of an unreachable SQL endpoint produced `SqlException.Number=258` (connection-pool wait timeout), which the research's narrow classifier list did not include. `SqlFailureClassifier` now includes `258`, and the unit test covers it. This is required for the documented `503 dependency_unavailable` behavior; authentication (`18456`), permission (`229`), and programming errors remain unclassified `500`.
- **Cache health budget:** The health check uses a 1-second internal read budget inside the 2-second health-check registration timeout; request operations keep the configured 250 ms per-operation budget, 500 ms normal read+populate budget, and 750 ms corrupt-payload repair maximum. This prevents a healthy but cold Redis multiplexer from producing a false `Degraded` on the first check.
- **Missing `serviceTypeId`:** A missing non-nullable `Guid` query parameter binds as `Guid.Empty` rather than producing a model-state error. The availability controller validates it explicitly and returns `400 validation_error`.
- **Docker SDK pin:** `global.json` requires SDK `10.0.403`, but the ticket 1 Dockerfile referenced SDK `10.0.401`, so Compose could not build. The build stage now uses `mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b`.
- **Formatting:** `dotnet format` normalized pre-existing import ordering in ticket 1 files; no behavior changed.
- **Demo script compatibility:** The script runs under Windows PowerShell 5.1 and maps the two seeded IANA zones to Windows time-zone IDs because .NET Framework cannot resolve IANA IDs. Its error-body extraction also handles the PowerShell 7 `HttpContent` shape, but only PowerShell 5.1 was available for verification.
- **Seed shift horizon:** The demo computes the next dealership-local weekday at 10:00 and fails with a remediation message if the seeded 28-day horizon does not cover it.
- **Cache test helper:** Tests use the production `ServiceCatalogCacheKeys.ForDealership` builder and a per-class `FLUSHDB` instead of a duplicate test-only key fixture. This exercises the production key format directly and preserves class isolation.

## Review follow-up

The optional review in [[review/2-review|the ticket 2 review]] recorded eight findings (three Medium, five Low) and no Critical/High. All were fixed and verified on 2026-11-01:

| Finding | Correction | Verification |
| --- | --- | --- |
| R-01 OpenAPI required parameters | The operation transformer now marks `dealershipId`, `serviceTypeId`, and `startsAt` required; the test asserts the flags and the response DTO schemas. | Runtime `/openapi/v1.json` check and `OpenApiTests` passed. |
| R-02 T-02 test isolation | The tests now use a wrong-service qualification, an inactive technician with a covering shift, and an inactive-bay-only scenario. | `AvailabilityEligibilityTests` passed in the 92-test integration run. |
| R-03 Cache budget assertions | The unit test bounds the injected 20 ms budget to 15-750 ms; the closed-port fallback must finish under 3 seconds; a single paused request under 3 seconds and five concurrent requests under 10 seconds. | Unit and integration suites passed. |
| R-04 Repair-path budget | The cache documentation now states the 750 ms corrupt-payload repair maximum (250 ms get + remove + set) alongside the 500 ms normal path. | Documentation review. |
| R-05 SQL-shape evidence | `AvailabilitySqlTests` asserts dealership/active/service predicates, shift-bound and half-open operators; a shift-start behavioral case was added. | SQL tests and `AvailabilityCoverageTests` passed. |
| R-06 Unused helper specification | The pure helpers are documented as specification mirrors of the SQL predicates, with the SQL queries cross-referencing them. | Comments reviewed; SQL tests passed. |
| R-07 Expiring README example | The README uses a future-start placeholder and the self-discovering script; the OpenAPI description no longer embeds a dated example. | Documentation review; script rerun passed. |
| R-08 PowerShell 7 path | The script now handles `$_.ErrorDetails.Message`, `HttpContent.ReadAsStringAsync()`, and the stream fallback; the README documents the verified Windows PowerShell 5.1 path only. | Demo script rerun under Windows PowerShell 5.1 exited `0`; PowerShell 7 remains unverified. |

## Issues and follow-up

- No required automated or operational check failed. All 190 executed tests passed after review fixes (98 unit + 92 integration).
- **Not run:** Human execution of the manual checklist at [[test/2-manual-test|Ticket 2 manual tests]] (generated 2026-11-01; all cases Not run), optional `/orchestrator-ticket review 2`, SQL execution-plan capture on representative volume, and any CI pipeline (none exists; not required by ticket 2).
- **Limitation:** The automated combined-outage test demonstrates failure on one host and service on a recovered host; the single-host stop/start recovery evidence for Redis and SQL comes from the Compose operational checks above.
- **Cold-start note:** The first readiness call immediately after an API restart can be `Unhealthy` while the existing SQL `DbContext` health check warms within its 5-second timeout; subsequent calls are `Healthy`. This is ticket 1 behavior and did not occur while Redis was the only degraded dependency.
- **Acceptance status:** Automated/operational evidence supports AC2-01-AC2-07 and T-02-T-13, but ticket acceptance is not marked complete until the generated manual checklist is executed by a human and any review findings are resolved.
- **Cleanup:** Integration fixtures drop their databases and discard their containers; Compose containers/network were removed with `docker compose down`; the named SQL volume was intentionally retained.
