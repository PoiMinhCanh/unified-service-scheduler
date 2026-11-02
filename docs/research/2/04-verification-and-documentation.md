---
title: "2 - Service Discovery and Availability: Verification and Documentation"
tags: [research, service-scheduler, testing, documentation]
ticket: 2
status: verified
updated: 2026-11-01
---

# 2 - Verification and documentation

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research overview: [[research/2/00-overview|Ticket 2 research overview]]
- Requirements: S2-01-S2-06; AC2-01-AC2-07; T-02-T-13; project requirements D-01-D-04 and P-04.
- Evidence baseline: HEAD `3f8ab1e` (2026-11-01), clean working tree.
- Implementation status: the pre-implementation test and fixture design below is historical. Ticket 2 implementation and executed test records landed on 2026-11-01; see [[test/2-test|the test report]].

## Verification principles

- Ticket 2 verification is read-path and cache behavior; booking confirmation, retrieval, the booking procedure, and booking concurrency remain ticket 3.
- Use real SQL Server through Testcontainers for availability, conflict, dealership scope, SQL outage, and runtime-read checks. In-memory/SQLite cannot establish the SQL predicates or provider behavior.
- Use real Redis through Testcontainers for cache hit/miss/expiry/invalidation, outage/stall, and health checks.
- Use the runtime identity (`scheduler_app`) for API reads and the privileged setup identity only for test data setup. Never add runtime writes or grants to make a test pass.
- Use a fixed clock (`2026-11-02T00:00:00Z`), the seeded 28-day shift horizon, and synthetic data. Do not depend on wall-clock dates or a public booking endpoint.
- Record expected behavior separately from observed results. New ticket cases are Not run until executed during implementation; research-phase probes (generated SQL, package/image, cache timing) establish design feasibility only and do not execute ticket acceptance cases.
- Do not add a test-only controller, runtime grant, or write path to induce failures. Use invalid isolated synthetic data (for example a temporary invalid-time-zone dealership) or a test factory pointing at unreachable dependencies, and clean up afterward.

## Test architecture

### Existing fixtures and their limits

| Fixture / file | Existing behavior | Ticket 2 change |
| --- | --- | --- |
| `SqlServerContainerFixture` | One SQL Server 2022 container per collection; one uniquely named database per class; setup runs migrations, runtime principal, and fixed-clock shifts; setup/runtime connection builders. | Reuse; add test data helpers for appointments/resources/opening hours. |
| `SqlServerTestBase` | Gives a class its initialized database and setup/runtime connection strings. | Reuse; add a combined base for SQL+Redis classes. |
| `SqlServerCollection` | `DisableParallelization = true`. | Keep for SQL-only ticket 2 tests; add a combined SQL+Redis collection for cache tests. |
| `SchedulerApiFactory` | Development host; in-memory configuration; default unreachable SQL runtime connection. | Add default unreachable cache connection, cache-config overrides, optional `TimeProvider` replacement, and optional test-service configuration. |
| `RuntimePermissionTests` | Proves runtime reads and denied appointment writes/DDL. | Keep; ticket 2 adds no grants. |
| `MigrationAndSeedTests` / `ConstraintTests` | Fixed-clock seed/shift assertions and appointment/constraint probe patterns. | Reuse insert/parameter patterns for appointment and resource probes; do not change production seeds. |

### Proposed fixtures and helpers

| Proposed file | Responsibility |
| --- | --- |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/RedisContainerFixture.cs` | Starts one pinned Redis container per collection; exposes a cache connection builder with a per-class `defaultDatabase` index; supports `PauseAsync`/`UnpauseAsync` for stalls. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/SqlServerAndRedisCollection.cs` | `ICollectionFixture<SqlServerContainerFixture>` and `ICollectionFixture<RedisContainerFixture>`; `DisableParallelization = true`. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/CacheSqlServerTestBase.cs` | Gives a class the SQL database, runtime/setup connections, Redis connection, and cache key helpers. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulingTestData.cs` | Parameterized SQL helpers to insert/delete temporary appointments, dealerships, service types, technicians, qualifications, shifts, and opening hours through the setup identity; includes appointment snapshot/UTC constraints. |
| `tests/ServiceScheduler.IntegrationTests/Fixtures/CacheKeys.cs` | Builds the exact v1 cache key and a unique test-prefix/key-deletion helper without changing production keys. |
| `tests/ServiceScheduler.UnitTests/Availability/*` | Pure parser/interval/opening/shift/overlap tests. |
| `tests/ServiceScheduler.UnitTests/Caching/ServiceCatalogCacheTests.cs` | Fake `IDistributedCache` tests for budget, cancellation, corrupt payload, and unexpected exceptions. |
| `tests/ServiceScheduler.UnitTests/Errors/*` | SQL error-number classification and Problem Code tests. |
| `tests/ServiceScheduler.IntegrationTests/Availability/*` | API/SQL availability, scope, conflict, DST, and duration tests. |
| `tests/ServiceScheduler.IntegrationTests/Catalog/*` | Discovery/catalog contracts and cache lifecycle tests. |
| `tests/ServiceScheduler.IntegrationTests/Resilience/*` | Redis outage/stall, SQL outage, simultaneous failure, and recovery tests. |
| `tests/ServiceScheduler.IntegrationTests/Health/*` | Cache degradation/readiness body and startup checks. |

### Isolation and determinism

- SQL: one database per test class; setup creates the schema/permissions/shifts; each test removes its own probe rows in `finally` or rolls back its probe transaction.
- Redis: one container per collection; each class uses a distinct `defaultDatabase` index; tests delete only their own keys. The combined collection is non-parallel.
- Clock: `FakeTimeProvider(2026-11-02T00:00:00Z)` replaces the API's `TimeProvider.System`; request starts use dates within the seeded horizon (for example `2026-11-03` Saigon local).
- Cache TTL: override `Scheduler:Cache:ServiceCatalog:AbsoluteTtl` to one second only for expiry tests; poll with a bounded deadline and no arbitrary fixed sleep.
- Stall: `RedisContainerFixture.PauseAsync()`/`UnpauseAsync()` with `try/finally`; measure elapsed time against the documented 500 ms cache budget plus HTTP/SQL overhead, not an exact millisecond assertion.
- Data volume: keep synthetic probes small; use generated GUIDs and unique names. If an index/execution-plan check needs volume, use a dedicated test with a documented row count and no performance guarantee.

## Requirement and scenario matrix

| Test ID | Requirement / case | Level | Setup and action | Expected outcome | Proposed file |
| --- | --- | --- | --- | --- | --- |
| TC2-01 | S2-01, AC2-01 | API | GET `/api/dealerships` | `200`; array of `{id,name,timeZoneId}` ordered by name/id; seeded IANA zones; no cache. | `Catalog/DealershipDiscoveryTests.cs` |
| TC2-02 | S2-01, S2-05 | API + Redis | GET catalog after emptying the key | `200`; ordered `{id,name,durationMinutes}`; exact key populated with v1 JSON; fields/order equal SQL. | `Catalog/CatalogCacheTests.cs` |
| TC2-03 | S2-01, AC2-01 | API + SQL | Insert a temporary dealership with no services; GET catalog | `200 []`; cache key contains `[]`; unknown dealership `404`; no key for unknown. | `Catalog/CatalogContractTests.cs` |
| TC2-04 | S2-01, S2-04 | API | Malformed dealership UUID; missing/invalid catalog route | `400 validation_error` with `traceId`; valid unknown UUID `404 not_found`. | `Catalog/CatalogContractTests.cs` |
| TC2-05 | S2-05, AC2-04 | API + Redis + SQL | Put a recognizable payload in the key (or point SQL at an unreachable endpoint); GET catalog twice | Hit returns the cached payload/order without SQL; miss test proves SQL read/population. | `Catalog/CatalogCacheTests.cs` |
| TC2-06 | S2-05, AC2-04, T-12 | API + Redis + SQL | One-second TTL; request; bounded poll until expired; update SQL catalog; request | Key expires; second request reads SQL fresh and repopulates. | `Catalog/CatalogCacheTests.cs` |
| TC2-07 | S2-05, AC2-04, T-12 | API + Redis + SQL | Request; update SQL catalog name; `RemoveAsync`/`DEL`; request | New SQL value returned; key repopulated; explicit invalidation works. | `Catalog/CatalogCacheTests.cs` |
| TC2-08 | S2-01, S2-05, T-11 | API + Redis | Request Saigon and London catalogs; update one dealership's SQL catalog | Distinct dealership keys; one update does not affect the other; both responses dealership scoped. | `Catalog/CatalogCacheTests.cs` |
| TC2-09 | S2-05, AC2-05, T-13 | API + Redis + SQL | Cache failure (closed Redis port) with real SQL; catalog and availability requests | Catalog `200` via SQL within the documented budget; availability correct; warning logged; readiness `200` (cache Degraded). | `Resilience/RedisOutageTests.cs` |
| TC2-10 | S2-05, AC2-05, T-13 | API + Redis + SQL | Pause Redis; issue concurrent catalog requests; measure; unpause; request again | Each request returns within the documented budget via SQL; no unbounded backlog; after recovery the key populates and a later hit works. | `Resilience/RedisOutageTests.cs` |
| TC2-11 | S2-05 | Unit | Fake cache with corrupt JSON, timeout, cancellation, and unexpected exception | Corrupt JSON -> SQL fallback/repair; timeout -> recoverable; request cancellation propagates; unexpected exception propagates. | `UnitTests/Caching/ServiceCatalogCacheTests.cs` |
| TC2-12 | S2-02, AC2-02, T-10 | API + SQL | Valid future start with `Z` and equivalent `+07:00` (`%2B`) | Same normalized `startsAt`/`endsAt`; response timestamps end with `Z`; `evaluatedAt` captured once. | `Availability/AvailabilityTimeTests.cs` |
| TC2-13 | S2-02, AC2-02, T-10 | API | Offset-free, unparseable, date-only, past, equal-to-clock starts | `400 validation_error` with `traceId`; no availability result. | `Availability/AvailabilityTimeTests.cs` |
| TC2-14 | S2-02, AC2-02, T-10 | API + SQL | Start with a computed end beyond the supported timestamp range | `400 validation_error`; no write. | `Availability/AvailabilityTimeTests.cs` |
| TC2-15 | S2-02, S2-03, AC2-02, T-02 | Unit + API + SQL | Seeded active/qualified technician and active bay; no appointments | `200 available:true`; `endsAt = startsAt + SQL duration`. | `Availability/AvailabilityEligibilityTests.cs` |
| TC2-16 | S2-03, AC2-02, T-02 | Unit + API + SQL | Unqualified active technician; block the seeded qualified technician | `available:false`; unqualified candidate never selected. | `Availability/AvailabilityEligibilityTests.cs` |
| TC2-17 | S2-03, AC2-02, T-02 | Unit + API + SQL | Inactive technician/bay; block active resources | `available:false`; inactive resources never selected. | `Availability/AvailabilityEligibilityTests.cs` |
| TC2-18 | S2-02, S2-03, AC2-02, T-03 | Unit + API + SQL | Partial shift and partial opening fixtures | `available:false`; free at the start but not for the full interval; adjacent rows are not combined. | `Availability/AvailabilityCoverageTests.cs` |
| TC2-19 | S2-03, AC2-02, T-04 | API + SQL | Technician-only conflict, bay-only conflict, free pair with different resources | Technician/bay conflicts evaluated independently; free pair `available:true`. | `Availability/AvailabilityConflictTests.cs` |
| TC2-20 | S2-03, AC2-02, T-04 | Unit + API + SQL | Adjacent, partial-overlap, containment, identical, reverse-containment intervals | Adjacent `available:true`; all overlaps `available:false`; no reservation created. | `Availability/AvailabilityConflictTests.cs` |
| TC2-21 | S2-03, AC2-02, T-04 | API + SQL | Count appointments and inspect Redis before/after availability | Counts/keys unchanged; availability is read-only and uncached. | `Availability/AvailabilityReadOnlyTests.cs` |
| TC2-22 | S2-02, AC2-03 | API + Redis + SQL | Put a stale catalog duration in Redis; call availability | `endsAt` uses the SQL duration; availability never reads the catalog cache. | `Availability/AvailabilityDurationTests.cs` |
| TC2-23 | S2-02, AC2-02, T-10 | Unit + API + SQL | London spring-forward, nonexistent boundary, and fall-back repeated-hour fixtures | Documented wall-clock policy holds; exact end-at-closing/shift-end accepted; outside window false. | `Availability/AvailabilityDstTests.cs` |
| TC2-24 | S2-03, AC2-02, T-11 | API + SQL | Unknown dealership/service, cross-dealership service, foreign-dealership resources | `404 not_found` for missing/out-of-scope references; foreign resources never satisfy; queries dealership scoped. | `Availability/AvailabilityScopeTests.cs` |
| TC2-25 | S2-03, AC2-02 | API + SQL | Capture generated SQL for technician/bay/opening queries | Contains dealership predicates, `Status = Confirmed`, `StartUtc < requested.EndUtc`, `EndUtc > requested.StartUtc`, and `NOT EXISTS`; index mapping documented. | `Availability/AvailabilitySqlTests.cs` (or query-builder unit test) |
| TC2-26 | S2-04, AC2-06 | API + SQL | Unreachable SQL on discovery, availability, and catalog miss | `503 dependency_unavailable` with `traceId`; no `available:false` false result; no cache write. | `Resilience/SqlOutageTests.cs` |
| TC2-27 | S2-04, AC2-06 | API + Redis + SQL | Populate cache; point SQL at unreachable endpoint; request catalog/discovery/availability/readiness | Catalog hit `200`; discovery/availability/readiness `503`; hit does not attempt SQL. | `Resilience/SqlOutageTests.cs` |
| TC2-28 | S2-05, AC2-05, AC2-06 | API + Redis + SQL | SQL and Redis both unavailable; then recover each independently | Liveness `200`; readiness `503` while SQL is down; catalog miss `503`; recovery restores readiness `200` and catalog/cache behavior. | `Resilience/CombinedOutageTests.cs` |
| TC2-29 | S2-04 | API + SQL | Insert a temporary dealership with an invalid IANA `TimeZoneId` and a dealership-scoped service type; request availability | Sanitized `500 internal_error` with `traceId`; no stack/password/connection string; no false availability. | `Availability/AvailabilityFailureTests.cs` |
| TC2-30 | S2-05, AC2-05 | API + Redis + SQL | Readiness with SQL healthy/Redis healthy, SQL healthy/Redis down, SQL down/Redis healthy, both down; liveness in each | `200` Healthy or Degraded as specified; `503` only when SQL is Unhealthy; live always `200`; body shows checks without secrets. | `Health/CacheHealthTests.cs` |
| TC2-31 | S2-05, AC2-05 | API + Redis | Start the test host with a valid but unreachable Redis endpoint | Host starts; `/health/live` `200`; SQL-backed catalog/availability still work; cache Degraded. | `Health/CacheHealthTests.cs` / `Startup/CacheConfigurationTests.cs` |
| TC2-32 | S2-05 | API | Missing/malformed `ConnectionStrings:Cache` | Startup fails with a clear configuration error naming the key; no credential value. | `Startup/CacheConfigurationTests.cs` |
| TC2-33 | S2-04, S2-06, AC2-01 | API | GET `/openapi/v1.json`; inspect paths, DTO schemas, status codes, ordering descriptions, Problem Details extensions | Three operations and success/error DTOs documented; ordering/explicit-offset text present; existing Swagger UI test passes. | `OpenApi/OpenApiTests.cs` |
| TC2-34 | S2-04 | API | Missing/malformed parameters and unknown route | `400`/`404` Problem Details with `code` and `traceId`; no stack/password; existing unknown-route assertion extended. | `Startup/ProblemDetailsTests.cs` |
| TC2-35 | S2-06, AC2-01 | Retired | Standalone demo-script execution removed by the 2026-11-01 scope amendment. | No script deliverable or execution prerequisite; automated API tests and TC2-33 remain required. | — |

### Default baseline behavior to preserve

| Existing check | Requirement | Expected after ticket 2 |
| --- | --- | --- |
| `HealthEndpointTests.LivenessIsAvailableWhenDatabaseIsUnreachable` | Foundation readiness | Live still `200` with SQL/Redis unreachable. |
| `HealthEndpointTests.ReadinessFailsWhenDatabaseIsUnreachable` | S2-05/AC2-06 | Ready still `503` when SQL is unreachable, regardless of Redis. |
| `InitializedReadinessTests.ReadinessReportsHealthyForAnInitializedDatabaseUsingTheRuntimeIdentity` | S2-05 | Ready `200` with initialized SQL; Redis unreachable may make it Degraded but still `200`. |
| `RuntimePermissionTests` | Foundation permission | Runtime still reads; direct appointment writes/DDL still denied. |
| `ConstraintTests` / `MigrationAndSeedTests` | Foundation schema | No schema/seed regression; no ticket 2 migration. |
| `OpenApiTests` / `ProblemDetailsTests` | S2-04/S2-06 | Existing assertions still pass; extend with new contract checks. |

## Commands and evidence

### Planned build/test commands

Run from the repository root with the pinned SDK and Docker running:

```powershell
dotnet restore
dotnet build --no-restore
dotnet format --verify-no-changes
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
```

Useful filtered runs during implementation (planned until the test files exist):

```powershell
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Availability"
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Catalog|FullyQualifiedName~Availability|FullyQualifiedName~Resilience"
```

The integration project starts real SQL Server and Redis Testcontainers; Docker must be running. Record the actual dependency versions, image digests, test counts, and any skipped/blocked checks.

### Planned Compose/operational commands

```powershell
docker compose config
docker compose up -d --wait
Invoke-WebRequest http://127.0.0.1:8080/health/live
Invoke-WebRequest http://127.0.0.1:8080/health/ready
Invoke-RestMethod http://127.0.0.1:8080/health/ready | ConvertTo-Json -Depth 5
docker compose exec redis redis-cli ping
docker compose stop redis
# Check liveness/readiness, catalog fallback, and availability while Redis is down.
docker compose start redis
# Check recovery and the readiness JSON.
docker compose exec redis redis-cli DEL "scheduler:v1:dealership:<lowercase-guid>:service-types"
# Open http://127.0.0.1:8080/swagger/index.html and exercise the three operations manually.
```

Also record a SQL outage check (stop the isolated SQL container or use the unreachable-endpoint test factory) and a catalog-hit check while SQL is down. Do not modify shared environments; use the isolated local demo only.

### Evidence record

Create `docs/test/2-test.md` during implementation using [[test/README|Testing guidance]], [[test/backend-test-template|Backend test template]], and [[test/integration-test-template|Integration test template]]. For each executed case record:

- requirement IDs and test ID;
- revision, environment, SDK/package/container versions, migrations/seeds/identities;
- actual command and working directory;
- expected versus observed result, including HTTP status/body, final SQL state, and Redis key state;
- status Passed/Failed/Not run/Blocked with reasons;
- cleanup and any limitation.

Do not mark AC2-01-AC2-07 or T-02-T-13 Passed from a generated checklist, a successful build, or the research document. The test phase later creates `docs/test/2-manual-test.md`; new manual cases remain Not run/Blocked until a human executes them.

## Documentation and deployment impact

| Record | Required change | Requirement |
| --- | --- | --- |
| `README.md` | Update current status, implemented vs planned, three endpoints, Redis/service-catalog caching, configuration names, Swagger/manual API instructions, health behavior, and verified commands. | S2-06, AC2-07 |
| `docs/architecture.md` | Record ticket 2 implemented behavior: discovery/catalog/availability flows, availability rules, DST wall-clock policy, cache budget/fallback, health degradation, no migration. | D-01, S2-06 |
| `docs/setup-guide.md` | Add Redis prerequisite/service, `ConnectionStrings:Cache` and `Scheduler:Cache:*` names/defaults, Compose startup, cache health/readiness, invalidation commands, troubleshooting, and actual commands. | D-03, S2-06 |
| `docs/database/schema.md` | State no schema change; record the availability query predicates/index mapping and generated-SQL verification; no new objects. | S2-03, S2-06 |
| `docs/database/overview.md` | Note ticket 2 availability/catalog reads use the existing relationships/indexes; no schema change. | S2-06 |
| `docs/database/migration.md` | Record that ticket 2 adds no migration and explain why no existing-index migration is required (or record a new migration if inspection proves one). | S2-03, S2-06 |
| `docs/database/cache.md` | Replace the planned cache note with the ticket 2 inventory: key, source, payload/version, TTL, invalidation, deployment, verification, limitation. | S2-05, S2-06 |
| `docs/README.md` | Add ticket 2 research/plan/test/deploy links and update statuses as phases complete. | S2-06 |
| `docs/deploy-notes/2-deploy-notes.md` | New: Redis service/config, no migration, cache namespace/invalidation, readiness/smoke checks, rollback/recovery, execution record. | S2-05, AC2-07 |
| `docs/test/2-test.md` | New during implementation: requirement coverage, commands, observed results, evidence. | S2-06, AC2-07 |
| `docs/test/2-manual-test.md` | Later test phase: manual API/operational checklist; new cases Not run/Blocked. | S2-06 |
| `scripts/ticket2-demo.ps1` | Retired by the 2026-11-01 scope amendment; no script deliverable is required. Automated API integration tests and Swagger UI provide the request coverage. | AC2-01, S2-06 |
| `.env.example`, `compose.yaml`, `appsettings.json` | Add cache configuration and Redis service; no startup gate. | S2-05 |
| `Directory.Packages.props`, project files | Add/pin Redis and Testcontainers.Redis packages. | S2-05 |

No CI workflow files exist under `.github/workflows/`; ticket 2 does not require adding a CI pipeline. Do not claim CI execution. Local commands and the test report are the evidence.

## Manual/operational checks

| Check | Action | Expected | Requirement |
| --- | --- | --- | --- |
| Redis healthy | `docker compose exec redis redis-cli ping`; GET catalog twice | `PONG`; first request populates, second uses cache; readiness body Healthy. | S2-05, T-12 |
| Redis down at startup | `docker compose stop redis`; start API/stack | API starts; `/health/live` `200`; `/health/ready` `200` Degraded (SQL healthy); catalog `200` via SQL. | AC2-05 |
| Redis down availability | Stop Redis; request availability | Correct availability; no error from Redis; warning log. | AC2-05 |
| Redis recovery | Start Redis; request catalog; inspect key | Cache repopulates; later request hits; readiness returns Healthy. | AC2-05 |
| SQL down | Stop isolated SQL; request discovery/availability/catalog miss | `503 dependency_unavailable`; no false availability; readiness `503`; liveness `200`. | AC2-06 |
| Catalog hit during SQL down | Populate cache; stop SQL; request catalog | `200` cached catalog; readiness still `503`. | AC2-06 |
| Cache invalidation | `redis-cli DEL` the key; request catalog | Key absent then repopulated from SQL; fresh values. | T-12 |
| OpenAPI/Swagger | GET `/openapi/v1.json`, open Swagger UI | Three operations, DTOs, errors, ordering documented; example requests work after setup. | AC2-01 |
| Standalone demo script | Retired by the 2026-11-01 scope amendment | No deliverable or manual check is required. | — |

Manual execution belongs to the human tester; these rows are planned/Not run in research.

## Scope verification for this concern

### 7.1 Requirement coverage

| Requirement | Research coverage | Planned verification |
| --- | --- | --- |
| S2-01 | Overview; Part 1 routes/DTOs; Part 3 cache contract | TC2-01-TC2-08, TC2-33 |
| S2-02 | Part 1 parsing/time; Part 2 algorithm/DST | TC2-12-TC2-14, TC2-22, TC2-23 |
| S2-03 | Part 2 eligibility/coverage/conflicts/indexes | TC2-15-TC2-21, TC2-24, TC2-25 |
| S2-04 | Part 1 Problem Details; Part 2 failure behavior; Part 3 health | TC2-26-TC2-29, TC2-34 |
| S2-05 | Part 3 cache/budget/health/invalidation | TC2-02, TC2-05-TC2-11, TC2-30-TC2-32 |
| S2-06 | Part 4 docs/commands; Part 1 OpenAPI and Swagger/automated API coverage | TC2-33 and documentation review (TC2-35 retired) |
| AC2-01 | Parts 1, 3, 4 | TC2-01-TC2-08, TC2-33 (TC2-35 retired) |
| AC2-02 | Parts 1, 2, 4 | TC2-12-TC2-25 |
| AC2-03 | Parts 1, 2, 4 | TC2-15-TC2-23 |
| AC2-04 | Parts 3, 4 | TC2-02, TC2-05-TC2-08 |
| AC2-05 | Parts 3, 4 | TC2-09-TC2-11, TC2-30, TC2-31 |
| AC2-06 | Parts 1, 3, 4 | TC2-26-TC2-30 |
| AC2-07 | Part 4 | Documentation review; `docs/test/2-test.md` |
| T-02 | Part 2 | TC2-15-TC2-17 |
| T-03 | Part 2 | TC2-18 |
| T-04 | Part 2 | TC2-19-TC2-21 |
| T-10 | Parts 1, 2 | TC2-12-TC2-14, TC2-23 |
| T-11 | Parts 2, 3 | TC2-04, TC2-08, TC2-24 |
| T-12 | Part 3 | TC2-02, TC2-05-TC2-08 |
| T-13 | Part 3 | TC2-09, TC2-10, TC2-30 |

### 7.2 Checklist

- [x] Every ticket requirement, acceptance criterion, and verification case has a research owner or explicit gap.
- [x] Proposed tests distinguish unit, API, real SQL Server, real Redis, and operational levels.
- [x] Test data, clocks, isolation, cleanup, and failure injection are specified.
- [x] Documentation/deployment impacts are identified with owners and requirement links.
- [x] No ticket test or acceptance check is reported as executed or passing; research-phase probes are labelled separately.
- [x] Research verification ran on 2026-11-01 and was re-verified after the scope amendment on 2026-11-01 (see the overview Verification section); implementation and executed ticket verification are recorded in [[test/2-test|the test report]], not in this research.

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement | Status |
| --- | --- | --- | --- | --- | --- |
| D-01 | Decision | How to combine SQL and Redis fixtures? | Add a combined non-parallel collection with both collection fixtures; keep SQL-only tests separate. | S2-05 | Resolved; combined fixture implementation deferred. |
| D-02 | Decision | How to isolate cache keys across test classes? | Distinct Redis `defaultDatabase` per class; delete class keys; production key format unchanged. | S2-05 | Resolved; test-class isolation implementation deferred. |
| D-03 | Decision | How to test TTL expiry deterministically? | Configure one-second TTL and poll the key with a bounded deadline; no fixed sleep. | T-12 | Resolved; expiry test deferred. |
| D-04 | Decision | How to test a stalled Redis? | `PauseAsync`/`UnpauseAsync` in a `try/finally`; measure bounded response; assert recovery. | T-13 | Resolved and probe-verified 2026-11-01: pause/unpause executed and recovery observed; API stall test deferred. |
| D-05 | Decision | How to test catalog hit during SQL outage? | Populate the cache first, then use a factory with an unreachable SQL endpoint and the same Redis database. | AC2-06 | Resolved; SQL-outage hit test deferred. |
| D-06 | Decision | How to test sanitized 500? | Insert a temporary invalid-time-zone dealership plus a dealership-scoped service type via setup; availability yields unexpected failure; assert no secrets. | S2-04 | Resolved with service-type correction; 500 test deferred. |
| D-07 | Decision | How to verify SQL-side overlap predicates? | Capture generated SQL (query-builder/`ToQueryString`/command logging) and assert predicates/`NOT EXISTS`; map to indexes. | S2-03 | Resolved and partially verified 2026-11-01: generated SQL was captured from the proposed EF queries; the implementation test must assert the same predicates and capture execution-plan/index use. |
| D-08 | Decision | Demo script when the seeded horizon is stale? | Retired with the standalone script by the 2026-11-01 scope amendment; automated tests use the fixed clock and seeded horizon and fail with a clear assertion when no future weekday is available. | S2-06 | Retired; no standalone script is required. |
| D-09 | Risk | Redis test database indices could collide/exhaust. | Expected classes fit 0-15; otherwise allocate a unique prefix or second container; document. | S2-05 | Deferred: implementation test-class inventory. |
| D-10 | Decision | No CI workflow exists. | Local commands + test report are the ticket evidence; adding CI is outside ticket 2. | S2-06 | Documented exclusion. |
| D-11 | Risk | Docker/Testcontainers unavailability blocks real-dependency checks. | Record Blocked with the missing prerequisite; do not substitute in-memory/SQLite evidence. | S2-05, S2-06 | Mitigation documented; Docker 25.0.3 and Testcontainers 4.14.0 were verified on 2026-11-01; the SQL test suite was not rerun. |
| D-12 | Decision | Manual checklist vs automated evidence. | Implementation records automated/operational results in `docs/test/2-test.md`; the test phase generates `docs/test/2-manual-test.md` with Not run cases. | AC2-07 | Process resolved; execution deferred. |

## Handoff

- Next phase after this re-verification: `/orchestrator-ticket verify plan 2` (the plan and implementation already exist; plan verification must be refreshed after this research change).
- Planning should use [[research/2/00-overview|this overview]] and Parts 1-4, define implementation phases/files, and assign each test ID/command to a step with exit evidence.
- Implementation must write and execute the tests, update the documentation in the same change, and record actual results in `docs/test/2-test.md`; research and planning alone do not establish ticket completion.
