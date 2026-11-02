---
title: "Scheduler Reference Data Cache"
tags: [database, redis, service-scheduler]
updated: 2026-11-02
---

# Reference data cache

Redis caches dealership service catalogs as described in [[architecture]]. SQL Server remains authoritative for availability, booking validation, and confirmation. Redis is not a booking lock and cached availability/duration is never used for authoritative scheduling.

Cache failure has bounded behavior: each operation has a caller-side budget, recoverable Redis failures fall back to SQL Server, and readiness reports optional cache degradation without hiding SQL Server unavailability.

## Cache telemetry

Instrumentation records `cache.get`/`cache.set`/`cache.remove` activities and `scheduler.cache.operations` counters with bounded `operation`/`result` labels and no cache key or dealership ID; log degradation events drop the key. `CacheHealthCheck` uses a non-recording read path so health probes do not enter the catalog hit-ratio denominator. A corrupt cached payload remains a transport-level `get` hit before fallback and is documented in the dashboard meaning.

## Scheduling and cache consistency

Booking reads `ServiceTypes.DurationMinutes` directly from SQL Server for its precheck and the procedure reads it again inside the transaction; the service never calls `ServiceCatalogService` or Redis. Retrieval reads the appointment table directly. Availability remains uncached and queries committed appointments in SQL, so a newly confirmed appointment is visible to later availability without invalidation. A stale catalog hit cannot change a new booking's duration or an existing appointment's snapshot.

## Cache inventory

| Field | Contract |
| --- | --- |
| Owner | `src/ServiceScheduler.Api/Features/ServiceCatalog/` (`ServiceCatalogService`, `ServiceCatalogCacheKeys`) and `src/ServiceScheduler.Api/Infrastructure/Caching/` (`BoundedCache`, `CacheOptions`, `CacheHealthCheck`). |
| Key | `scheduler:v1:dealership:{dealershipId:D}:service-types`, using the lowercase hyphenated GUID format. The `v1` segment is the payload/serialization version. |
| Source | `dbo.ServiceTypes` filtered by `DealershipId` and ordered by `Name` then `Id`; dealership existence is read from `dbo.Dealerships` on a miss. |
| Payload | UTF-8 JSON array of `{ id, name, durationMinutes }` with web-default camelCase; order preserved from SQL. Empty catalogs are cached as `[]`. |
| Freshness | Absolute TTL 10 minutes by default (`Scheduler:Cache:ServiceCatalog:AbsoluteTtl`); no sliding expiration. |
| Refresh when | `ServiceTypes` rows, names, or durations for a dealership change, or the payload/serialization version changes. |
| Invalidation | Delete affected `scheduler:v1:dealership:*` keys or bump the key version to `v2` in the same deployment. A missed invalidation is bounded by the TTL. |
| Deployment | Redis service and `ConnectionStrings:Cache`; no database migration. The Redis image is `redis:7.4.12-alpine@sha256:8da19c7399dca900c6822a1cf30f9d2285585e2c4b457ff4095b0f4500054d3f`; package `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13` (`StackExchange.Redis` `2.7.27`). |
| Verification | Miss/hit, expiry, explicit invalidation, empty/unknown catalog, dealership isolation, stale duration, Redis outage/stall, SQL outage with a catalog hit, cache health, and startup behavior. |

## Budgets and failure behavior

| Item | Value |
| --- | --- |
| Per-operation caller budget | `Scheduler:Cache:OperationTimeout` default 250 ms for each get/set/remove; request cancellation propagates and is not logged as degradation. |
| Cumulative read+populate budget | 250 ms get + 250 ms set = 500 ms configured for the normal path, excluding SQL/HTTP time. This is a configured wait budget, not a strict wall-clock SLA. |
| Corrupt-payload repair worst case | 250 ms get + 250 ms remove + 250 ms set = 750 ms. This is the absolute maximum cumulative cache wait for any supported request path. |
| Health-check read budget | 1 second internal budget inside the 2-second health-check registration timeout, so a cold multiplexer does not report a false `Degraded`; request operations keep 250 ms. |
| Redis limits | `AbortOnConnectFail=false`; 500 ms connect/sync/async timeouts; `ConnectRetry=1`; `BacklogPolicy.FailFast`; `AllowAdmin=false`. |
| Cache write failure | Logged as a warning and the SQL-derived `200` response is still returned. |
| Corrupt cached JSON | Logged as an error, key removed, SQL read and repopulation attempted. |
| Unexpected exception | Not swallowed; mapped to sanitized `500 internal_error` by the shared error pipeline. |
| Health degradation | Redis failures/budget expiry return `Degraded`; readiness stays `200` when SQL is healthy and is `503` when SQL is unhealthy. |

## Invalidation and local commands

```powershell
# Stop/start the local cache without gating the API
docker compose stop redis
docker compose start redis

# Explicit invalidation for one dealership
docker compose exec redis redis-cli DEL "scheduler:v1:dealership:<lowercase-guid>:service-types"

# Confirm the key and health
docker compose exec redis redis-cli EXISTS "scheduler:v1:dealership:<lowercase-guid>:service-types"
Invoke-WebRequest http://127.0.0.1:8080/health/ready
```

A cache hit for a dealership later removed from SQL is a documented limitation: the API has no dealership delete/admin API, the catalog is read-only seeded configuration, and the TTL/version invalidation bounds staleness. Do not add a SQL existence check to every hit.

## Maintenance checklist

For each cache introduced or changed, record the owner, key, source, payload/version, freshness, refresh triggers, deployment/invalidation, and verification shown above. A schema change must review cache impact even when no key changes. See [[database/maintenance|Maintenance guideline]] and [[architecture]].
