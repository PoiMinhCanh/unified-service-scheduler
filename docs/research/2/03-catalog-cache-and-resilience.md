---
title: "2 - Service Discovery and Availability: Catalog Cache and Resilience"
tags: [research, service-scheduler, redis, cache, resilience]
ticket: 2
status: verified
updated: 2026-11-01
---

# 2 - Catalog cache and resilience

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research overview: [[research/2/00-overview|Ticket 2 research overview]]
- Requirements: S2-01, S2-04, S2-05; AC2-01, AC2-04, AC2-05, AC2-06; T-12, T-13; project requirement P-02.
- Evidence baseline: HEAD `3f8ab1e` (2026-11-01), clean working tree.

## Existing absence and baseline

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| No Redis package, registration, cache key, or cache code exists. | `Directory.Packages.props:5-19` has no Redis package; `Program.cs` has no Redis registration; checkout contains no `Infrastructure/Caching/`; no `IDistributedCache` references. | Implement cache from scratch in the ticket. |
| Redis is not part of Compose. | `compose.yaml:3-57` has only SQL Server, `db-setup`, and API. | Add Redis service/config without an API startup gate. |
| Health checks are SQL-only. | `Program.cs:34-43,60-64`; `HealthEndpointTests.cs`, `InitializedReadinessTests.cs`. | Add optional cache degradation while preserving 200/503 semantics. |
| Service catalog source is seeded SQL data. | `SeedData.cs:109-132` has three service types with fixed GUIDs and positive durations; `ServiceTypes` is dealership scoped. | Cache payload comes from `ServiceTypes`; tests can use stable IDs and add probe rows. |
| Ticket 1 cache record is still planned. | `docs/database/cache.md:9-30` describes the future key/TTL shape and required inventory fields. | Replace with the implemented ticket 2 inventory during implementation. |
| No cache stampede/protection mechanism is required. | P-02 and ticket scope; TTL + bounded fallback suffice. | Do not add distributed locks or refresh-ahead. |

## Cache contract

### Key and payload

| Item | Proposed contract |
| --- | --- |
| Key | `scheduler:v1:dealership:{dealershipId:D}:service-types` |
| UUID format | `Guid.ToString("D")`: lowercase, hyphenated, 36 characters (for example `a1000000-0000-0000-0000-000000000001`). Normalize route GUIDs to this format before building the key. |
| Payload version | `v1` in the key. A DTO/serialization change requires `v2` (or a documented key deletion) and an update to `docs/database/cache.md`. |
| Serialization | `System.Text.Json` with web defaults (`JsonSerializerDefaults.Web`), UTF-8, JSON array of `{ id, name, durationMinutes }` with camelCase property names. Examples use lowercase UUID strings. |
| Ordering | Same as the SQL response: service `name` ascending, then `id`; the serialized array preserves that order. |
| Empty catalog | Existing dealership with no services is cached as `[]` and returned as `[]`. |
| Not cached | Unknown-dealership `404`, SQL dependency failures, cache-operation failures, and any response that did not come from a successful SQL read. |
| TTL | Absolute expiration, initially 10 minutes; configurable. No sliding expiration. |
| Data scope | Service catalog only. No customer/vehicle data, no availability, no appointments. |

Example payload:

```json
[
  { "id": "51000000-0000-0000-0000-000000000002", "name": "Brake Service", "durationMinutes": 120 },
  { "id": "51000000-0000-0000-0000-000000000001", "name": "Oil Change", "durationMinutes": 60 }
]
```

### Catalog read flow

| Step | Behavior |
| --- | --- |
| 1. Valid GUID route | Controller binds `dealershipId`; malformed UUID returns `400` (Part 1). |
| 2. Bounded cache read | `IDistributedCache.GetAsync(key)` under a 250 ms caller budget. |
| 3. Hit | Deserialize the v1 payload, return `200` with the cached order/fields. No SQL is attempted, so a hit may return `200` while SQL is unavailable. |
| 4. Miss | Query SQL for dealership existence, then ordered `ServiceTypes` for that dealership. |
| 5. Missing dealership | Return `404 not_found`; do not cache. |
| 6. SQL failure | Classified SQL failure returns `503 dependency_unavailable`; do not cache. |
| 7. Successful SQL read | Attempt a bounded cache write (250 ms) with absolute TTL; return `200` whether or not the write succeeds. A write failure is logged as degradation. |
| 8. Cache read failure/timeout | Fall back to the SQL path; log degradation; still attempt a bounded write after a successful SQL read. Maximum cache wait for this request is 500 ms. |
| 9. Corrupt payload | Log an error, best-effort remove the key, treat as a miss, read SQL, repopulate. Do not hide unrelated programming errors. |
| 10. Request cancellation | Propagate `OperationCanceledException`; do not treat it as a recoverable cache outage or log it as degradation. |

A cache hit for a dealership later removed from SQL is a documented limitation: ticket 2 has no dealership delete/admin API, the catalog is read-only seeded configuration, and the TTL/version-invalidation bounds staleness. Do not add a SQL existence check to every cache hit; that would defeat the cache and make hits depend on SQL.

## Redis configuration and bounded behavior

### Selected packages and image

| Role | Selection | Evidence / status |
| --- | --- | --- |
| Cache implementation | `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13` | Verified 2026-11-01: restored and compiled in a temporary probe; aligns with the existing `10.0.13` Microsoft.Extensions baseline in `Directory.Packages.props:6-18`; resolved `StackExchange.Redis` `2.7.27`, including `ConfigurationOptions.BacklogPolicy = BacklogPolicy.FailFast`. |
| Redis integration tests | `Testcontainers.Redis` `4.14.0` | Verified 2026-11-01: restored and compiled; matches `Testcontainers.MsSql` `4.14.0`; `IContainer.PauseAsync`/`UnpauseAsync` compiled and executed against the pinned Redis image. |
| Redis image | `redis:7.4.12-alpine@sha256:8da19c7399dca900c6822a1cf30f9d2285585e2c4b457ff4095b0f4500054d3f` | Verified 2026-11-01: pulled by digest; `redis-cli ping` returned `PONG`; server reported `v=7.4.12`. |
| Platform | `linux/amd64` in Compose, consistent with the ticket 1 model | Verified 2026-11-01: `docker image inspect` reported `os=linux`, `arch=amd64`, and the pinned repository digest. |

The transitive StackExchange.Redis version supports `ConfigurationOptions.BacklogPolicy` (introduced in the 2.5 line); ticket 2 sets it in code because it is code-only and has no connection-string keyword.

### Configuration names and defaults

| Key | Consumer | Proposed default | Purpose |
| --- | --- | --- | --- |
| `ConnectionStrings:Cache` / `ConnectionStrings__Cache` | API and test host | Local Compose `redis:6379`; host runs use `127.0.0.1:6379` | Redis endpoint. Required in API mode; a syntactically valid but unreachable endpoint must not fail startup. |
| `Scheduler:Cache:ServiceCatalog:AbsoluteTtl` | API | `00:10:00` | Absolute catalog TTL. Tests override to a short value (for example 1 second). |
| `Scheduler:Cache:OperationTimeout` | API | `00:00:00.250` | Caller-side budget for each cache get/set/remove/health operation. |
| `Scheduler:Cache:ConnectTimeout` | API | `00:00:00.500` | Underlying Redis connection timeout. |
| `Scheduler:Cache:CommandTimeout` | API | `00:00:00.500` | Underlying `SyncTimeout`/`AsyncTimeout` for Redis commands. |
| `Scheduler:Cache:ConnectRetry` | API | `1` | Initial connection attempts; avoids long startup/first-request waits. |

The options validator checks that the cache connection string is present and parseable, the TTL is positive, the operation timeout is positive and reasonably bounded (for example at most 5 seconds), and the connect/command timeouts are positive. It never opens a Redis connection; reachability belongs to runtime behavior and the health check.

### Underlying Redis options

| Option | Proposed value | Reason |
| --- | --- | --- |
| `AbortOnConnectFail` | `false` | A multiplexer can be returned while disconnected and recover later; startup/first request must not fail hard. |
| `ConnectTimeout` | 500 ms | Bound initial connection work. |
| `SyncTimeout` | 500 ms | Bound synchronous command waits. |
| `AsyncTimeout` | 500 ms | Bound asynchronous command waits that continue after the caller stops waiting. |
| `ConnectRetry` | 1 | Bound retry work during initial connection. |
| `BacklogPolicy` | `BacklogPolicy.FailFast` | Do not queue commands unboundedly while disconnected; fail fast and let the SQL fallback proceed. |
| `KeepAlive` | Redis default (60 s) | No ticket-specific requirement; keep the default unless configuration inspection shows otherwise. |
| `AllowAdmin` | `false` | Local cache operations only. |
| `DefaultDatabase` | From the connection string; production `0`; tests use a per-class index | Isolates test keys without changing the production key format. |

### Cache-operation budget and maximum cumulative wait

- Each cache get/set/remove/health operation uses a linked `CancellationTokenSource` with `OperationTimeout` (250 ms). If the request token is canceled, the cancellation is propagated as a request cancellation; if only the budget expires, the failure is recoverable and the SQL fallback proceeds.
- `RedisCache` cancellation support is provider-version dependent; the implementation must not rely on the cancellation token alone to stop an in-flight StackExchange.Redis command. The caller uses `WaitAsync` to stop waiting. The underlying command is still bounded by `AsyncTimeout` (500 ms), and disconnected commands fail fast under `BacklogPolicy.FailFast`, so the design does not leave an unbounded retry/task backlog.
- The configured budget is **250 ms per cache operation** and **500 ms cumulative** for a read plus population (250 ms get + 250 ms set), excluding SQL/HTTP time. The 2026-11-01 probe observed warm operations at 254-265 ms, a cold read+populate sequence at 508 ms (configured budget plus scheduling overhead), and some first operations taking up to ~504 ms before the caller timer completed. Treat 500 ms as the configured wait budget, not a strict wall-clock SLA; if a hard cap is required, lower `OperationTimeout`/`ConnectTimeout` or initialize the multiplexer at startup.
- The cache health check uses the same 250 ms operation budget but is separate from request latency; its health-check registration timeout (2 s) must exceed the internal budget so a stalled Redis produces `Degraded`, not a health-check timeout/`Unhealthy`.
- On a cache read timeout, the implementation should observe the abandoned underlying task (for example attach a fault-observing continuation) so a later Redis fault does not surface as an unobserved task exception. Do not retry the cache operation within the request; fall back to SQL.

### Research-phase probe results (2026-11-01)

A temporary probe outside the repository used the selected packages, the pinned Redis image, and a real Testcontainers Redis container. It exercised the underlying `RedisCache` rather than the not-yet-implemented catalog wrapper.

| Probe | Observed result |
| --- | --- |
| Registration with unreachable endpoint | `AddStackExchangeRedisCache` + `IDistributedCache` resolution completed in 17-20 ms without connecting; startup is not blocked. |
| Unreachable endpoint, warm | Caller stopped at 254-257 ms under the 250 ms budget (`TimeoutException`); a later call failed fast with `RedisConnectionException` in 27-107 ms. |
| Cold read+populate against unreachable endpoint | Get 257 ms + set 250 ms = 508 ms cumulative; no unbounded retry was observed. |
| Paused real Redis | Read timed out at 257-265 ms; after `UnpauseAsync`, the next read returned the stored value in 1 ms. |
| Health status mapping | A `Degraded` health check returned HTTP `200` with the default `MapHealthChecks` mapping. |
| Package/image execution | The pinned package versions restored and compiled; the pinned image pulled with digest `sha256:8da19c...`, reported `linux/amd64`, answered `PONG`, and reported server v7.4.12. |

### Recoverable vs unexpected failures

| Failure | Classification | Behavior |
| --- | --- | --- |
| `RedisConnectionException`, `RedisTimeoutException`, other `RedisException` | Recoverable cache degradation | Log warning; catalog falls back to SQL; health check returns `Degraded`. |
| Cache operation budget cancellation (not request cancellation) | Recoverable cache degradation | Same as above; propagate only if the request token is canceled. |
| Request cancellation | Caller cancellation | Rethrow; don't log as cache degradation; don't write a response body. |
| Corrupt cached JSON (`JsonException`) | Cache-data error | Log error; remove key; SQL fallback; repopulate. |
| Other exception (for example `InvalidOperationException` in cache code) | Unexpected fault | Do not swallow; map to sanitized `500` per the API failure contract. |
| Cache set/remove failure after a successful SQL read | Recoverable degradation | Log warning; still return the SQL-derived `200`. |

## Health, startup, and Compose behavior

### Health check and readiness contract

Add a Redis health check that performs a bounded `GetAsync` on a constant health key (no set) and:

- returns `Healthy` when the cache responds (including a null value);
- returns `Degraded` on recoverable Redis exceptions or the cache budget expiring;
- lets unexpected exceptions remain unexpected (the health framework may mark the check `Unhealthy`; that is a programming/configuration fault, not a normal Redis outage).

Register the check with the `ready` tag and a 2-second registration timeout (the existing database check has a 5-second timeout at `Program.cs:37-43`). Keep the default health status-code mapping: Healthy -> `200`, Degraded -> `200`, Unhealthy -> `503`.

Then:

| Condition | `/health/live` | `/health/ready` | `/health/cache` (optional) |
| --- | --- | --- | --- |
| SQL healthy, Redis healthy | `200` | `200`, overall Healthy | N/A unless a separate endpoint is added |
| SQL healthy, Redis down/stalled | `200` | `200`, overall Degraded; body shows `redis: Degraded` | N/A |
| SQL down, Redis healthy/down | `200` | `503`, overall Unhealthy; body shows `database: Unhealthy` and the cache state | N/A |
| Both down | `200` | `503`, overall Unhealthy | N/A |

Prefer reporting cache degradation in the existing `/health/ready` JSON body rather than adding a separate endpoint. The JSON response writer must include only check names/statuses (and optionally durations), never exception messages, endpoints, passwords, or connection strings. Existing readiness tests assert status codes only; extend them to assert the body and the SQL/Redis independence.

### Startup and Compose

- `AddStackExchangeRedisCache` with `ConfigurationOptions` does not require a reachable Redis during startup. With `AbortOnConnectFail=false`, a valid-but-unreachable cache endpoint must not prevent `CreateClient`/host startup. A missing or syntactically invalid `ConnectionStrings:Cache` is a configuration validation failure, not an outage.
- Keep the API's Compose dependency only on successful `db-setup`. Do not make the API depend on Redis health; `docker compose up -d --wait` must not be blocked by an unhealthy Redis. Do not add a Redis healthcheck to Compose, because `--wait` would then wait on it; use a simple Redis service with `restart: unless-stopped` and verify it manually with `redis-cli ping`.
- Add a Redis service with the pinned image, `platform: linux/amd64`, a loopback port for local inspection (for example `127.0.0.1:6379:6379`), and no required persistent volume. The cache is ephemeral. If host port 6379 is already occupied, publish a different loopback port and use it for host-run cache commands; the Compose API keeps the internal `redis:6379` endpoint.
- Add `ConnectionStrings__Cache` to the API service environment and `.env.example`; `db-setup` does not need Redis.
- Document Redis stop/start, `redis-cli ping`, readiness body inspection, catalog fallback, and cache invalidation in the setup guide. Do not expose connection strings in logs or health bodies.

### Testcontainers

- Add `Testcontainers.Redis` to the integration test project and start one real Redis container per test collection with the same pinned image as Compose.
- Use a combined SQL Server + Redis collection for cache tests, with `DisableParallelization = true`; keep the existing SQL-only collection for tests that do not need Redis.
- Give each test class a distinct Redis `DefaultDatabase` index via its cache connection string, and delete the class's keys before/after tests. The documented production key format remains unchanged.
- Use `IContainer.PauseAsync`/`UnpauseAsync` (confirmed in Testcontainers 4.14.0) for a stalled Redis, and an unreachable endpoint (for example `127.0.0.1:1`) for connection failure. Do not use arbitrary sleeps to simulate a stall.
- Extend `SchedulerApiFactory` to accept the cache connection string, replace `TimeProvider` with `FakeTimeProvider(2026-11-02T00:00:00Z)` for availability tests, and optionally configure test services/interceptors.

## Invalidation and freshness

| Trigger | Action |
| --- | --- |
| Service catalog seed/catalog row change | Bump the key version (`v1` -> `v2`) in the same deployment, or explicitly delete affected keys. Update `docs/database/cache.md` and the deploy notes. |
| Explicit invalidation | `redis-cli DEL "scheduler:v1:dealership:<lowercase-guid>:service-types"` against the local Redis; verify the next catalog request reads SQL and repopulates. |
| Missed invalidation | TTL bounds staleness to the configured absolute TTL (10 minutes initially). Document this bound. |
| Corrupt/old payload | Versioned key plus removal/reload; old version keys expire or are deleted. |
| Availability/booking | Never uses cached availability or cached duration for its authoritative read; catalog cache state cannot change booking correctness. |

## Cache inventory for `docs/database/cache.md`

| Field | Ticket 2 record |
| --- | --- |
| Owner | Ticket 2; proposed `Features/ServiceCatalog/` + `Infrastructure/Caching/` implementation files to be recorded when implemented. |
| Key | `scheduler:v1:dealership:{dealershipId:D}:service-types`; lowercase hyphenated GUID. |
| Source | `dbo.ServiceTypes` filtered by `DealershipId`; ordered by `Name`, `Id`; dealership existence read from `dbo.Dealerships`. |
| Payload | v1 JSON array `{ id, name, durationMinutes }` with web-default camelCase; ordering preserved. |
| Freshness | Absolute TTL 10 minutes (configurable); explicit `DEL` or version bump after catalog/seed changes; no sliding expiration. |
| Refresh when | `ServiceTypes` rows, names, durations, or dealership-scoped catalog seed changes; key version/serialization changes. |
| Deployment | Redis service/config; key version or affected-key deletion; TTL freshness bound if invalidation fails; no schema migration. |
| Verification | Miss/hit/expiry/invalidation; empty/unknown catalog; Redis outage/stall bounded fallback; SQL outage hit/miss; stale duration; startup/readiness; cache health/logs. |

## Verification scenarios for T-12 and T-13

| Scenario | Setup | Expected |
| --- | --- | --- |
| Miss populates | Empty Redis database; request catalog | `200`; exact key exists with v1 payload; fields/order match SQL. |
| Hit avoids SQL | Populate the key with a recognizable payload (or make SQL unreachable after a previous request); request catalog | `200` from the cached payload; no SQL dependency; hit with SQL unreachable returns `200`. |
| Expiry | Configure 1-second TTL; request; poll the key with a bounded deadline until it expires | Key disappears; next request reads SQL and repopulates. Use bounded polling, not a fixed sleep. |
| Explicit invalidation | Request; change a SQL catalog name; `RemoveAsync`/`DEL` the key; request | New SQL value returned; key repopulated. |
| Empty catalog | Insert a dealership with no services via setup; request | `200 []`; cache key contains `[]`; unknown dealership returns `404` and no key. |
| Cache hit with SQL down | Populate key; create a factory with unreachable SQL and the same Redis database; request catalog | `200` cached data; discovery/availability/readiness still `503`. |
| SQL miss with Redis/SQL down | Empty cache; unreachable SQL and Redis; request catalog | `503`; no key write; liveness `200`; readiness `503`. |
| Redis connection failure | Real SQL; cache endpoint on a closed port; request catalog and availability | Catalog `200` from SQL within the documented budget; availability correct; readiness `200` (cache Degraded); log warning. |
| Redis stall | Real SQL + Redis; `PauseAsync`; issue catalog requests (including concurrent requests); measure elapsed; `UnpauseAsync` | Each response returns within the bounded budget via SQL; no unbounded backlog; after unpause, the cache populates and a later hit works. |
| Request cancellation | Unit test the cache wrapper with a canceled request token | `OperationCanceledException` propagates; not logged as degradation/fallback. |
| Corrupt payload | Write invalid JSON under the key; request | SQL data returned; key repaired; error logged; no 500. |
| Unexpected cache exception | Fake `IDistributedCache` throws `InvalidOperationException` | Exception is not swallowed; API returns sanitized `500`. |
| Stale duration | Put a catalog payload with a wrong duration in Redis; request availability | Availability uses SQL duration for `endsAt`; catalog hit may still return the stale catalog value. |
| Cache key isolation | Request Saigon and London catalogs | Two dealership-scoped keys; updating one does not affect the other. |

The API-level scenarios below are **Not run**; research-phase probes on 2026-11-01 covered the underlying packages, image, cache timing, and health-status mapping as recorded above. Detailed test files/commands and the full requirement matrix are in [[research/2/04-verification-and-documentation|Part 4]].

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement | Status |
| --- | --- | --- | --- | --- | --- |
| C-01 | Decision | Exact cache key/UUID/payload/version? | `scheduler:v1:dealership:{D}:service-types`, lowercase hyphenated GUID, web-default JSON, v1. | S2-05 | Resolved and format-verified 2026-11-01: `Guid.ToString("D")` produced the lowercase hyphenated form and web-default JSON emitted camelCase; cache lifecycle tests deferred. |
| C-02 | Decision | Cache budget and Redis limits? | 250 ms/operation; 500 ms read+populate; 500 ms connect/command; retry 1; `FailFast`; cancellation propagated. | S2-05, AC2-05 | Resolved and probed 2026-11-01: registration 17-20 ms, warm operations 254-265 ms, cold read+populate 508 ms, paused Redis 257-265 ms, recovery 1 ms; API outage/stall tests deferred. |
| C-03 | Risk | `RedisCache` cancellation may not cancel the underlying command. | Bound caller with `WaitAsync`; bound underlying command with `AsyncTimeout`; `BacklogPolicy.FailFast`; observe abandoned task. | S2-05 | Verified 2026-11-01: `WaitAsync` stopped the caller at ~250 ms, the paused container recovered after unpause, and abandoned operations were observed; API wrapper test deferred. |
| C-04 | Decision | How to expose optional degradation? | `ready`-tagged Redis check returns `Degraded`; readiness JSON shows checks; Healthy/Degraded -> 200, Unhealthy SQL -> 503. | S2-05, AC2-05 | Resolved and framework-verified 2026-11-01: a `Degraded` check returned HTTP `200` under default `MapHealthChecks`; readiness JSON assertions deferred. |
| C-05 | Risk | `docker compose up --wait` could wait for Redis health. | Do not add a Redis healthcheck that gates `--wait`; API depends only on setup; document `redis-cli ping` separately. | S2-05, AC2-05 | Deferred: the Compose Redis service does not exist yet; implementation check. |
| C-06 | Decision | How to invalidate after seed changes? | Version bump or `DEL`; TTL bounds staleness. | S2-05 | Resolved; deployment notes and invalidation test deferred. |
| C-07 | Risk | Cache payload changes could be read by old/new code. | Version segment in the key; bump on DTO/serialization change; old keys expire/delete. | S2-05 | Resolved; docs/test deferred. |
| C-08 | Decision | How to test hit without SQL? | Populate cache and point SQL to an unreachable endpoint; `200` proves no SQL; separate miss test proves SQL read. | AC2-04, AC2-06 | Resolved; API hit/miss tests deferred. |
| C-09 | Risk | Corrupt cache JSON could break catalog. | Narrow `JsonException` handling: log, remove, SQL fallback, repopulate. | S2-05 | Resolved; unit test deferred. |
| C-10 | Decision | Cache hit for a deleted dealership? | Documented TTL/version limitation; no delete API in scope; no SQL check on hit. | S2-01, S2-05 | Resolved with limitation. |
| C-11 | Decision | Redis image/package pins? | Selected in this part; pull/run verification remains. | S2-05 | Verified 2026-11-01: packages restored/built; image pulled with digest, `linux/amd64`, `PONG`, server v7.4.12. |
| C-12 | Risk | Per-class Redis database indices may be exhausted. | Use 0-15 for the expected test classes; if exceeded, allocate a unique key prefix or start another container. | S2-05 | Deferred: test-class inventory is an implementation check. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S2-05 / P-02 | Key/payload/TTL, miss/hit/expiry/invalidation, empty/unknown, stale duration, cache key isolation. | API + Redis + SQL Server |
| AC2-04 | Hit avoids SQL; miss populates; expiry/invalidation fresh read. | API + Redis + SQL Server |
| AC2-05 | Redis connection failure/stall bounded fallback; concurrent requests; recovery; startup succeeds; readiness stays 200 when SQL healthy. | API + Redis + SQL Server |
| AC2-06 | SQL outage: discovery/availability/catalog miss `503`; catalog hit `200`; readiness `503`; both failures; recovery. | API + Redis + SQL Server |
| T-12 | Cache lifecycle. | Redis + API |
| T-13 | Redis unavailable. | API + SQL Server + Redis |

Research-phase probes executed 2026-11-01; all ticket-level API/cache tests remain **Not run**.

## References checked

| Reference | Use | Status |
| --- | --- | --- |
| [Microsoft.Extensions.Caching.StackExchangeRedis versions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.caching.stackexchangeredis/index.json) and [10.0.13 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.caching.stackexchangeredis/10.0.13/microsoft.extensions.caching.stackexchangeredis.nuspec) | Package availability and StackExchange.Redis `2.7.27` dependency | Verified 2026-11-01: restored/built in a temporary probe; StackExchange.Redis `2.7.27` resolved |
| [Testcontainers.Redis versions](https://api.nuget.org/v3-flatcontainer/testcontainers.redis/index.json) | Package availability | Verified 2026-11-01: package restored/compiled; Redis container executed |
| [Testcontainers 4.14.0 IContainer source](https://raw.githubusercontent.com/testcontainers/testcontainers-dotnet/4.14.0/src/Testcontainers/Containers/IContainer.cs) | `PauseAsync`/`UnpauseAsync` availability | Verified 2026-11-01: methods compiled and executed against the pinned image |
| [Docker Hub Redis 7.4 tag metadata](https://hub.docker.com/v2/repositories/library/redis/tags?page_size=50&name=7.4) | `7.4.12-alpine` manifest digest | Verified 2026-11-01: pulled by digest; `linux/amd64`; `redis-cli ping` = `PONG` |
| [SERedis configuration documentation](http://seredis.dev/Configuration.html) | `connectTimeout`, `asyncTimeout`, `syncTimeout`, `connectRetry`, `abortConnect`, code-only `BacklogPolicy` | Checked; package API compiled and cache behavior probed 2026-11-01 |
| [StackExchange.Redis BacklogPolicy reference](https://docs.dndocs.com/n/StackExchange.Redis/2.8.37/api/StackExchange.Redis.BacklogPolicy.html) | `FailFast`/`Default` semantics and version history | Checked; `BacklogPolicy.FailFast` compiled and used in probe 2026-11-01 |
