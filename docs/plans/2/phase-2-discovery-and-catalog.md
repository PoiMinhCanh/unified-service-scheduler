---
title: "2 - Phase 2: Discovery and Service Catalog"
tags: [plans, service-scheduler, api, redis, catalog]
ticket: 2
phase: 2
status: planned
---

# Phase 2: Discovery and service catalog

## Requirements and scope

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/01-api-contracts-and-validation|API contracts and validation]], [[research/2/03-catalog-cache-and-resilience|catalog cache and resilience]], [[research/2/04-verification-and-documentation|verification and documentation]].
- Requirement IDs: S2-01, S2-05; AC2-01 (endpoint/DTO portion), AC2-04; T-11 (catalog portion), T-12; project D-02 and P-02; test IDs TC2-01-TC2-08.
- Intended outcome: deliver `GET /api/dealerships` and `GET /api/dealerships/{dealershipId}/service-types`, including ordered previews of seeded data, dealership-scoped catalog reads, the bounded Redis cache-aside contract, empty/unknown/dependency behavior, and the real SQL Server + Redis test fixtures.
- Exclusions: availability endpoint and scheduling rules (Phase 3); OpenAPI operation/schema transformers and document assertions (Phase 5); compose Redis service (Phase 4); schema/seed/runtime-grant changes.

## Dependencies

- Phase 1: cache registration/options, bounded cache wrapper, error contract, `TimeProvider`, health check, extended `SchedulerApiFactory`, and test report.
- Existing SQL Server fixture and seed data from ticket 1.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S2-01; AC2-01; D-02 | `src/ServiceScheduler.Api/Features/Dealerships/DealershipsController.cs`, `Features/Dealerships/Contracts/DealershipResponse.cs` | Add `GET /api/dealerships`, projecting `{ id, name, timeZoneId }` with `AsNoTracking`, ordered by `Name` then `Id`; add `[ProducesResponseType]` for `200` and `ProblemDetails` statuses. | API returns seeded dealerships in documented order; malformed routing fails safely; SQL failure flows through the shared error contract. | Planned |
| 2 | S2-01, S2-05; P-02 | `src/ServiceScheduler.Api/Features/ServiceCatalog/Contracts/ServiceTypeResponse.cs`, `ServiceCatalogCacheKeys.cs` | Define the catalog DTO and the exact v1 key builder using `Guid.ToString("D")`; centralize the key so implementation, tests, and docs share it. | Key builder unit/API evidence shows `scheduler:v1:dealership:{lowercase-guid}:service-types`; payload serialization uses web-default camelCase. | Planned |
| 3 | S2-01, S2-04, S2-05; AC2-04 | `src/ServiceScheduler.Api/Features/ServiceCatalog/ServiceCatalogService.cs` | Implement cache-aside: bounded read; hit deserialization/return without SQL; miss checks dealership existence, reads ordered `ServiceTypes`, returns `404` without caching for unknown dealerships, returns classified `503` without caching for SQL failure; bounded set after successful reads; corrupt JSON logs an error/removes/treats as miss; cache read/write degradation is logged as a warning through `ILogger`; request cancellation propagates without a degradation log. | Miss populates; hit avoids SQL; empty catalogs cache as `[]`; 404/dependency failures are never cached; cache failures degrade to SQL without failing the request; degradation is observable in logs without secrets. | Planned |
| 4 | S2-01, S2-04; AC2-01 | `src/ServiceScheduler.Api/Features/ServiceCatalog/ServiceCatalogController.cs` | Add `GET /api/dealerships/{dealershipId}/service-types` with raw `Guid` binding (no `:guid` constraint), `ProducesResponseType` attributes, and documented ordering; map missing dealership/service to the shared `404` contract. | Valid requests return ordered DTOs or `[]`; malformed dealership ID returns `400 validation_error`; unknown dealership returns `404 not_found`; cache hit returns `200` even when SQL is unavailable. | Planned |
| 5 | S2-05; AC2-04; T-12 | `tests/ServiceScheduler.IntegrationTests/Fixtures/RedisContainerFixture.cs`, `SqlServerAndRedisCollection.cs`, `CacheSqlServerTestBase.cs`, `Fixtures/CacheKeys.cs` | Add one pinned Redis container per combined collection (`redis:7.4.12-alpine@sha256:8da19c...`), per-class `defaultDatabase`, paused/unpaused helpers, non-parallel combined collection, and key cleanup helpers. | Fixtures start with real SQL Server and Redis; classes are isolated; no production key format changes. | Planned |
| 6 | S2-01, S2-05; TC2-01-TC2-08 | `tests/ServiceScheduler.IntegrationTests/Catalog/DealershipDiscoveryTests.cs`, `CatalogContractTests.cs`, `CatalogCacheTests.cs` | Implement discovery, catalog contract, and cache lifecycle tests against real dependencies, including empty catalogs, unknown dealerships, key contents, miss/hit, expiry with bounded polling, explicit invalidation, per-dealership keys, and hit-with-SQL-unreachable. | TC2-01-TC2-08 pass in the combined collection; observed cache keys/payloads and SQL reads are recorded. | Planned |
| 7 | S2-06; P-04 | `docs/test/2-test.md` | Append Phase 2 revision, commands, observed outcomes, and unexecuted checks. | Report preserves Phase 1 evidence and records actual Phase 2 results. | Planned |

## Verification

| Test ID / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build | `dotnet build --no-restore` after the new controllers/services. | Build succeeds with no new warnings. | Not run |
| TC2-01 (S2-01, AC2-01) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Catalog.DealershipDiscovery"` | `200`; `{id,name,timeZoneId}` ordered by name/id; seeded IANA zones; no cache key created. | Not run |
| TC2-02 (S2-01, S2-05) | Same project, `--filter "FullyQualifiedName~Catalog.CatalogCache"` | Miss returns ordered catalog and populates the exact v1 key with matching fields/order. | Not run |
| TC2-03 (S2-01, AC2-01) | Same filter, empty-catalog case | Temporary dealership with no services returns `200 []` and caches `[]`; unknown dealership returns `404` and creates no key. | Not run |
| TC2-04 (S2-01, S2-04, T-11) | Same filter, malformed/unknown dealership cases | Malformed UUID `400 validation_error` with `traceId`; valid unknown UUID `404 not_found`. | Not run |
| TC2-05 (S2-05, AC2-04) | Same filter, hit/miss cases | Hit returns the cached payload without SQL; separate miss proves SQL read/population. | Not run |
| TC2-06 (S2-05, AC2-04, T-12) | Same filter, one-second TTL with bounded polling | Key expires; next request reads SQL fresh and repopulates. | Not run |
| TC2-07 (S2-05, AC2-04, T-12) | Same filter, explicit invalidation after a SQL name change | Fresh SQL value is returned and the key is repopulated. | Not run |
| TC2-08 (S2-01, S2-05, T-11) | Same filter, Saigon and London catalogs | Two dealership-scoped keys; updating one does not affect the other. | Not run |
| Unit cache behavior (TC2-11 support) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Caching"` | Additional catalog service tests cover corrupt payload, cache budget, cancellation, and unexpected exceptions. | Not run |

## Exit criteria

- Discovery and catalog endpoints return the documented DTOs, ordering, and error statuses.
- Real Redis + real SQL Server cache lifecycle tests (TC2-01-TC2-08) pass and are recorded.
- Cache hits demonstrably avoid the catalog SQL query; empty/unknown/corrupt/failure cases behave as specified.
- No availability endpoint, compose service, OpenAPI transformer, schema change, or runtime grant is claimed.
- `docs/test/2-test.md` contains actual Phase 2 evidence and explicit remaining checks.

## Impacts and remaining decisions

- Database/migrations: none; read-only queries against existing tables.
- Cache: the production key/payload/TTL contract is implemented and verified; the cache inventory documentation update belongs to Phase 6.
- Configuration: uses the Phase 1 `ConnectionStrings:Cache` and `Scheduler:Cache:*` settings; no new names.
- Deployment: compose Redis and setup-guide instructions remain Phase 4/6.
- Remaining decisions: none blocking. Cache hit deserialization may reuse the response DTO; it must preserve the fixed payload version/order.
