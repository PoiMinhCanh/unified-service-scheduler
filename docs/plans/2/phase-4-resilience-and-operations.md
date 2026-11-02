---
title: "2 - Phase 4: Resilience, Health, and Local Operations"
tags: [plans, service-scheduler, redis, health, operations]
ticket: 2
phase: 4
status: planned
---

# Phase 4: Resilience, health, and local operations

## Requirements and scope

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/03-catalog-cache-and-resilience|catalog cache and resilience]], [[research/2/04-verification-and-documentation|verification and documentation]].
- Requirement IDs: S2-04, S2-05; AC2-05, AC2-06; T-13; project P-02; test IDs TC2-09, TC2-10, TC2-26-TC2-28, TC2-30-TC2-32.
- Intended outcome: prove bounded Redis fallback and SQL failure behavior against real dependencies, expose Redis degradation without weakening readiness, add the Redis Compose service and cache configuration, and record operational stop/start, recovery, and invalidation checks.
- Exclusions: new endpoints or scheduling rules (Phases 2/3); OpenAPI transformers and Swagger/manual API instructions (Phase 5); final documentation consolidation (Phase 6); no Redis healthcheck gate, background jobs, or distributed locks.

## Dependencies

- Phase 1: Redis registration, bounded wrapper, cache health check, JSON health writer, error handler, extended factory.
- Phase 2: catalog endpoint and cache-aside behavior.
- Phase 3: availability endpoint.
- Docker engine with the pinned Redis image and the existing SQL Server image.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S2-05; AC2-05; TC2-30 | `src/ServiceScheduler.Api/Program.cs`, `Infrastructure/Health/HealthResponseWriter.cs`, `tests/ServiceScheduler.IntegrationTests/Health/CacheHealthTests.cs` | Complete readiness JSON output and register the Redis `ready`-tagged check with a 2-second registration timeout; test SQL healthy/Redis healthy, SQL healthy/Redis down, SQL down/Redis healthy, and both down. | Healthy/Degraded render `200` with check names/statuses only; SQL Unhealthy renders `503`; liveness is `200` in every combination; no secrets in the body. | Planned |
| 2 | S2-05; AC2-05; TC2-09 | `tests/ServiceScheduler.IntegrationTests/Resilience/RedisOutageTests.cs` | Use a closed Redis endpoint with real SQL; request catalog and availability; assert bounded SQL fallback, correct availability, warning logging, and readiness `200` Degraded. | Catalog returns `200` from SQL within the documented budget; availability is correct; readiness reports cache degradation without failing. | Planned |
| 3 | S2-05; AC2-05; TC2-10 | `tests/ServiceScheduler.IntegrationTests/Resilience/RedisOutageTests.cs`, `Fixtures/RedisContainerFixture.cs` | `PauseAsync` the Redis container in `try/finally`; issue concurrent catalog requests; measure elapsed time against the 500 ms configured budget plus HTTP/SQL overhead; `UnpauseAsync`; verify recovery and later cache hit. | Every request returns within the bounded budget via SQL; no unbounded backlog; after recovery the key populates and a later request hits it. | Planned |
| 4 | S2-04; AC2-06; TC2-26, TC2-27 | `tests/ServiceScheduler.IntegrationTests/Resilience/SqlOutageTests.cs` | Point the factory at an unreachable SQL endpoint; request discovery, availability, and a catalog miss; separately prepopulate Redis and request catalog with SQL unavailable; assert statuses and no cache writes. | Discovery/availability/catalog miss return `503 dependency_unavailable` with `traceId` and never a false availability result; catalog hit returns `200`; no key is written on failures. | Planned |
| 5 | S2-05; AC2-05, AC2-06; TC2-28 | `tests/ServiceScheduler.IntegrationTests/Resilience/CombinedOutageTests.cs` | Exercise SQL and Redis unavailable together, then recover each independently; assert liveness, readiness, catalog, and availability behavior. | Liveness `200`; readiness `503` while SQL is down; catalog miss `503`; recovery restores readiness `200` and correct catalog/cache behavior. | Planned |
| 6 | S2-05; AC2-05; TC2-31, TC2-32 | `tests/ServiceScheduler.IntegrationTests/Health/CacheHealthTests.cs`, `Startup/CacheConfigurationTests.cs` | Complete the start-with-unreachable-Redis scenario with SQL-backed catalog/availability; confirm missing/malformed cache configuration still fails startup clearly. | Host starts; liveness `200`; SQL-backed requests work; cache is Degraded; configuration failures name the key without exposing values. | Planned |
| 7 | S2-05; AC2-05 | `compose.yaml`, `.env.example`, `src/ServiceScheduler.Api/appsettings.json` | Add a pinned `redis:7.4.12-alpine@sha256:8da19c...` service on `linux/amd64`, loopback port, `restart: unless-stopped`, no healthcheck; add `ConnectionStrings__Cache` to the API environment and `.env.example`; add non-secret `Scheduler:Cache` defaults; keep API `depends_on` limited to `db-setup`. | `docker compose config` validates; `docker compose up -d --wait` is not gated by Redis health; the API can start and serve SQL-backed requests with Redis down. | Planned |
| 8 | S2-05; AC2-05; T-13 | Operational checks against local Compose | Run `docker compose exec redis redis-cli ping`, GET catalog twice, stop Redis, check liveness/readiness/catalog/availability, start Redis, check recovery, and `DEL` the key then request the catalog. | PONG; miss then hit; Redis-down requests fall back within budget; readiness `200` Degraded with SQL healthy; recovery repopulates; explicit invalidation causes a fresh SQL read. | Planned |
| 9 | S2-06; P-04 | `docs/test/2-test.md`, `docs/deploy-notes/2-deploy-notes.md` (initial operational record) | Append Phase 4 commands/results, dependency versions, image digests, and any blocked checks; start deploy notes with Redis/config/invalidation/recovery facts observed. | Report and deploy notes record actual observations and preserve prior evidence; unexecuted checks remain explicit. | Planned |

## Verification

| Test ID / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build | `dotnet build --no-restore`. | Build succeeds with no new warnings. | Not run |
| TC2-09 (S2-05, AC2-05, T-13) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Resilience.RedisOutage"` | Closed Redis endpoint: catalog `200` via SQL within budget; availability correct; warning logged; readiness `200` Degraded. | Not run |
| TC2-10 (S2-05, AC2-05, T-13) | Same filter, paused Redis case | Concurrent requests return within the bounded budget; recovery restores cache population and hits. | Not run |
| TC2-26, TC2-27 (S2-04, AC2-06) | Same project, `--filter "FullyQualifiedName~Resilience.SqlOutage"` | SQL-dependent reads return `503 code=dependency_unavailable`; no false availability; catalog hit `200`; no cache writes on failure. | Not run |
| TC2-28 (S2-05, AC2-06) | Same project, `--filter "FullyQualifiedName~Resilience.CombinedOutage"` | Liveness `200`; readiness `503` while SQL is down; recovery restores `200` and correct behavior. | Not run |
| TC2-30 (S2-05, AC2-05) | Same project, `--filter "FullyQualifiedName~Health.CacheHealth"` | Readiness body shows correct check states; Degraded stays `200`; SQL Unhealthy is `503`; liveness always `200`. | Not run |
| TC2-31 (S2-05, AC2-05) | Same filter, startup with unreachable Redis | Host starts; SQL-backed catalog/availability work; cache Degraded. | Not run |
| TC2-32 (S2-05) | Same project, `--filter "FullyQualifiedName~Startup.CacheConfiguration"` | Missing/malformed cache configuration fails startup with a clear key-naming error and no credential value. | Not run |
| Compose validation | `docker compose config`. | Configuration resolves with Redis and `ConnectionStrings__Cache`; API still depends only on `db-setup`. | Not run |
| Compose operational | `docker compose up -d --wait`; `docker compose exec redis redis-cli ping`; stop/start Redis; catalog/readiness checks. | Stack starts; PONG; documented degradation and recovery behavior; `--wait` is not blocked by Redis. | Not run |

## Exit criteria

- Redis outage/stall and SQL outage tests pass with real dependencies and bounded response times.
- Readiness reports `Degraded` for Redis-only failure and `503` only when SQL is unhealthy; liveness stays dependency-free.
- Compose Redis service validates and the API starts without a Redis health gate.
- Operational stop/start/recovery/invalidation checks are recorded in the test report and initial deploy notes.
- No endpoint behavior, scheduling rule, cache key format, or migration is changed in this phase.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: resilience behavior and health degradation verified; inventory documentation remains Phase 6.
- Configuration: `ConnectionStrings:Cache` and non-secret `Scheduler:Cache:*` defaults are operational in Compose and host runs.
- Deployment: Redis service/config/recovery facts are captured for the final deploy notes; no persistent cache volume is added.
- Remaining decisions: none blocking. If host port 6379 is occupied, publish a different loopback port and document it; the Compose API keeps `redis:6379`.
