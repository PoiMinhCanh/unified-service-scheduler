---
title: "2 - Service Discovery and Availability"
tags: [ticket, service-scheduler, api, availability, redis]
ticket: 2
status: specified
created: 2026-10-28
updated: 2026-11-01
---

# 2 - Service Discovery and Availability

## Objective and baseline

Let a service advisor discover dealerships and services and check whether a qualified technician and service bay are available for a requested service interval. Add Redis caching for service catalogs without making it authoritative for scheduling.

Read [README](../../README.md) and [[architecture]] for the shared domain, assumptions, API/error contracts. Build on the project foundation and test framework established in [[1 - Init Project]]. Add tests alongside each function.

### Inspected starting point

The ticket 1 API host, `SchedulerDbContext`, initial migration `20261031092016_InitialCreate`, synthetic catalogs/resources, future-shift seeder, restricted SQL runtime identity, and SQL Server/API fixtures exist. [[test/1-test|Ticket 1 test report]] records its executed verification; this review does not rerun those checks. Business controllers, Redis integration, and application error-code/trace-ID customization are still absent. Extend the existing foundation rather than introducing another host, persistence model, or generic repository layer.

The runtime identity currently has `SELECT` access only. Keep reads on that identity; use privileged fixture connections solely for test data setup. Existing appointment indexes cover dealership/technician/start and dealership/bay/start with end/status included. Inspect generated availability SQL before deciding whether an additional migration is necessary.

### Requirement traceability

Use these ticket-local IDs in research, plans, review, and execution evidence. The rules below define their required behavior.

| ID | Deliverable | Project requirements |
| --- | --- | --- |
| S2-01 | Dealership discovery and dealership-scoped service catalogs with explicit response contracts. | D-02 |
| S2-02 | Explicit-offset/future-start validation, authoritative duration, and local opening-hour evaluation. | A-02 |
| S2-03 | Full-interval qualification, active-resource, shift, and independent conflict checks through scoped SQL reads. | A-02 |
| S2-04 | Advisory availability response, consistent Problem Details, and controlled SQL failure behavior. | A-02, D-02 |
| S2-05 | Redis catalog cache-aside, TTL, invalidation, bounded fallback, and optional health degradation. | P-02 |
| S2-06 | Interactive Swagger API documentation, automated verification, and updated implementation/configuration records; no standalone demo script is required. | D-01, D-02, D-03, D-04, P-04 |

## Functions to implement

| Endpoint | Behavior |
| --- | --- |
| `GET /api/dealerships` | Return seeded dealerships and their IANA time-zone IDs. |
| `GET /api/dealerships/{dealershipId}/service-types` | Return dealership services and positive durations using Redis cache-aside. |
| `GET /api/dealerships/{dealershipId}/availability?serviceTypeId=...&startsAt=...` | Evaluate the exact requested start and computed duration; return availability, interval, and evaluation timestamp. |

### API contracts

- Discovery returns a JSON array of `{ id, name, timeZoneId }`; catalogs return a JSON array of `{ id, name, durationMinutes }`. Use stable ordering, documented in OpenAPI, for both lists. An existing dealership with no services returns `200` and `[]`; an unknown dealership returns `404`, including on the catalog path.
- Availability returns `{ dealershipId, serviceTypeId, available, startsAt, endsAt, evaluatedAt }`. All response timestamps are UTC with `Z`. Return the same interval and evaluation fields for both available and unavailable outcomes; selected technician/bay IDs are not part of this response.
- `serviceTypeId` and `startsAt` are required. Malformed UUIDs, missing parameters, unparseable timestamps, and a computed end outside the supported timestamp range return `400`. Validate the raw timestamp's explicit offset before conversion; do not let model binding silently interpret offset-free input as local time. URL-encode positive offsets (`+` as `%2B`) in query examples.
- An unknown dealership or a missing/service-from-another-dealership reference returns `404`. Resource scope is enforced internally: this endpoint accepts no technician or bay references, so resource-scope tests seed foreign-dealership candidates rather than inventing request parameters.
- Classified database unavailability returns `503`; unexpected failures return sanitized `500`. Define stable application error-code values and the trace-ID extension consistently for validation, not-found, and dependency failures, including framework-generated validation responses.

### Availability rules

- Require a future timestamp with `Z` or an explicit offset; reject offset-free input and use an injected `TimeProvider`. Normalize to UTC and evaluate dealership opening hours in its local time zone.
- Capture the evaluation clock once per request; require `startsAt > evaluatedAt`, and return that captured value as `evaluatedAt`. Compute elapsed duration in UTC, then check the full interval against dealership-local opening boundaries. The explicit input offset identifies the instant even during a repeated DST hour; it need not match the dealership offset.
- Compute the end from the authoritative SQL Server service duration. Clients cannot select duration, end, or arbitrary resource IDs.
- Require an active, qualified technician and active bay in the same dealership. The full interval must fit within one opening interval and one technician shift; split shifts represent breaks.
- Follow AS-06: holidays and overnight opening intervals are excluded. Do not combine adjacent or split opening/shift rows to satisfy coverage. An end exactly at closing or shift end is allowed. Specify and verify a deterministic policy for any ambiguous or nonexistent local opening boundary used by the DST fixture; do not add a local-time booking input API.
- Use half-open intervals `[start, end)` and SQL-side indexed overlap filtering: `existing.StartUtc < requested.EndUtc AND existing.EndUtc > requested.StartUtc`.
- Check conflicts for technicians and bays independently and evaluate candidates in a stable order. Do not load all appointments into memory.
- Return `200` with `available: false` when a valid request has no eligible pair. Availability is advisory, creates no reservation, and is not cached.
- Use shared Problem Details contracts: `400` for malformed/past/offset-free input, `404` for missing/out-of-dealership resources, and controlled dependency/server errors. Include a stable error code and trace ID without exposing secrets or stack traces.

### Catalog cache

- Use `IDistributedCache` with Redis and a dealership-scoped key such as `scheduler:v1:dealership:{id}:service-types`; configure an absolute TTL, initially 10 minutes.
- Cache only successful catalog results for existing dealerships, including empty catalogs; do not cache missing-dealership responses or dependency failures. Preserve the same DTO fields and ordering on hits and misses. Document the exact key/UUID format and payload serialization version.
- On a miss, read SQL Server and populate the cache. Bound cache-operation time and fall back to SQL Server on recoverable Redis connectivity/timeout failures.
- Log degradation using the available application logger. Do not swallow unrelated programming errors.
- Research and planning must select a concrete, configurable cache-operation budget and underlying Redis connection/command limits, and state the maximum cumulative cache wait for a request that attempts both read and population. Verify those limits during connection failure and a stalled operation; cancelling only the caller's wait must not leave an unbounded retry/task backlog. Propagate request cancellation rather than treating it as a recoverable cache outage.
- Document explicit key removal or namespace versioning after catalog seed changes. TTL bounds eventual freshness if invalidation fails.
- Cache only service catalogs, with no customer/vehicle data. Scheduling and booking validation always use authoritative SQL Server data.
- Add Redis to local Compose and Testcontainers fixtures. Redis failure must not become a hard readiness failure; expose optional degradation without hiding database unavailability.
- The API must start and serve SQL-backed requests when Redis is unreachable; Compose must not require healthy Redis as an API startup gate. Readiness remains `200` for SQL healthy/Redis down, and `503` for SQL down regardless of Redis. Keep liveness dependency-free. Document how optional degradation is observable without exposing connection strings.

## Acceptance criteria

- [ ] AC2-01: All three endpoints and their success/error DTOs are documented in OpenAPI and exercised by automated API integration tests using synthetic data. Swagger UI supports supplementary manual requests. Discovery/list ordering, empty catalogs, and missing-dealership behavior match S2-01.
- [ ] AC2-02: Qualification, inactive resource, dealership scope, full opening/shift coverage, interval boundary, time-offset, strict future-start, and DST tests pass (S2-02, S2-03).
- [ ] AC2-03: Availability reads existing confirmed appointments correctly, returns the documented fields for either outcome, uses SQL duration even when the catalog cache is stale, and does not mutate data or promise a subsequent booking (S2-02-S2-04).
- [ ] AC2-04: Catalog miss, hit, expiry, and explicit invalidation tests pass against real Redis and SQL Server; hits demonstrably avoid the catalog SQL query (S2-05).
- [ ] AC2-05: Redis outage/stall produces correct catalog results within the documented cache budget through SQL fallback; availability remains correct, startup succeeds, and readiness does not fail solely because Redis is down (S2-05).
- [ ] AC2-06: Database unavailability on SQL-dependent reads produces the documented controlled failure, not a false availability result. A valid catalog cache hit may still return `200`; readiness must remain `503` (S2-04, S2-05).
- [ ] AC2-07: README, Swagger/manual API instructions, cache/schema documentation, and recorded test results reflect implemented behavior (S2-06).

## Verification and scope boundaries

Owns the A-02 read path and P-02. The cases below verify this ticket's read and cache behavior. Use seeded confirmed intervals through the privileged test fixture to verify conflicts before booking APIs exist; do not add a public appointment write path or grant runtime direct writes.

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-02 Qualification or inactive resource | Unit + integration | Unqualified/inactive candidates never satisfy availability. | A-02 |
| T-03 Partial shift/opening coverage | Unit + integration | A resource free at the start but unavailable later cannot satisfy availability for the full interval. | A-02 |
| T-04 Interval boundaries | Unit + SQL Server | Availability permits adjacent intervals and rejects partial overlap, containment, and identical confirmed intervals for either resource. Reads create no reservations. | A-02 |
| T-10 Time handling | Unit + API | Reject past/offset-free starts; accept equivalent explicit-offset instants; evaluate opening boundaries in dealership local time, including a DST example. | A-02 |
| T-11 Dealership scope | API + SQL Server | Reject missing dealerships/services and out-of-dealership services; foreign-dealership technicians/bays never satisfy availability. Catalog keys and all SQL queries remain dealership scoped. | A-02 |
| T-12 Cache lifecycle | Redis + API | Miss populates cache; hit uses it; expiry or explicit invalidation causes a fresh database read. | P-02 |
| T-13 Redis unavailable | API + SQL Server + Redis | Catalog uses bounded SQL fallback, availability remains correct, degradation is logged, and readiness does not fail solely because Redis is unavailable. | P-02 |

Supplement T-02-T-13 with API contract assertions for both availability outcomes, empty catalogs, malformed/missing UUIDs and timestamps, starts equal to the clock, duration overflow, and consistent Problem Details. Include independent technician-only and bay-only conflicts, a free pair whose resources differ from those on existing appointments, and no eligible candidates. Verify SQL-side overlap predicates without loading appointment history into memory.

Cache verification must distinguish cache reads from catalog SQL reads, isolate keys across dealerships/tests, and use a short configured TTL with bounded polling for expiry. Seed a stale duration in Redis and assert availability uses SQL duration. Verify SQL outage on discovery, availability, and a catalog miss separately from a successful catalog hit; also test SQL/Redis simultaneous failure and Redis recovery. Use deterministic clocks and privileged appointment fixtures satisfying the existing schema constraints; do not depend on wall-clock dates or a public booking endpoint.

Appointment creation/retrieval, transactional booking, concurrent booking verification, and telemetry infrastructure are outside this ticket's scope. Verification here covers catalog fallback and availability behavior during Redis outages.

Record commands and results in `docs/test/2-test.md` using [[test/README|Testing guidance]]. Update [[database/cache]], relevant database records, [[architecture]], and [[setup-guide]].

Executable examples must discover actual seeded IDs and generate a future weekday start within the seeded opening/shift horizon, rather than embedding an expiring date. Record the selected Redis package/image pins, settings, cumulative wait budget, invalidation command, and startup/health behavior in setup/cache documentation. Record schema impact explicitly, including the rationale if existing tables/indexes need no migration. Business implementation and test execution remain pending; this ticket update supplies the input for the orchestrator research phase.
