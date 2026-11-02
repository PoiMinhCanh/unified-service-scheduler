---
title: "2 - Service Discovery and Availability: Research Overview"
tags: [research, service-scheduler, api, availability, redis]
ticket: 2
status: verified
updated: 2026-11-01
---

# 2 - Service Discovery and Availability: Research Overview

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Goal and scope: add dealership discovery, dealership-scoped service catalogs through a bounded Redis cache-aside path, and an advisory availability read that evaluates an explicit-offset future start against authoritative SQL Server duration, opening hours, technician qualification/shift, bay activity, and existing confirmed appointments.
- Requirement IDs: S2-01 through S2-06, AC2-01 through AC2-07, and verification cases T-02, T-03, T-04, T-10, T-11, T-12, and T-13; project requirements A-02, D-01, D-02, D-03, D-04, P-02, and P-04.
- Evidence baseline: checkout HEAD `3f8ab1e2cac26e54c5364f683d5df70cb1cfe5ff` (2026-11-01), clean working tree before the research documents were added; inspected ticket, README, architecture, setup guide, database and test guides, application source, migrations, tests, Compose, configuration, and package pins. Rechecked for research verification on 2026-11-01 with the research changes staged.
- Environment observed: .NET SDKs `8.0.100` and `10.0.403` installed; `global.json` pins `10.0.403` with roll-forward disabled; Docker server `25.0.3`; Compose `v2.24.6-desktop.1`.
- Related plan: [[plans/2/README|Ticket 2 implementation plan]], verified 2026-11-01; implementation and its test records followed.

> [!IMPORTANT]
> This document records requirements, inspected evidence, proposed design, and planned verification. The pre-implementation audit below describes the evidence baseline (HEAD `3f8ab1e`); ticket 2 implementation and its executed test records landed on 2026-11-01 and are recorded in [[test/2-test|the test report]], not in this research. The 2026-11-01 scope amendment retired the standalone demo script and requires interactive Swagger API documentation plus automated API integration tests. Research-phase probes were executed on 2026-11-01 for generated SQL, package/image availability, DST conversion, cache timing, and health-status mapping; this research document does not establish ticket tests or acceptance.

## Decomposition decision

Use multipart research under `docs/research/2/` because ticket 2 spans three API operations, an availability rule set with time-zone/DST behavior, a Redis cache with explicit resilience budgets, and a real-dependency verification matrix. Cache, API-contract, SQL-index, and documentation concerns need separate evidence rather than one oversized document.

| Part | File | Responsibility |
| --- | --- | --- |
| Overview | [[research/2/00-overview\|00-overview]] | Scope lock, part index, current architecture, consolidated coverage/audit, gaps, dependency-ordered change plan, recovery, handoffs, traceability, decisions, and out-of-scope items. |
| Part 1 | [[research/2/01-api-contracts-and-validation\|01-api-contracts-and-validation]] | Routes, DTOs, explicit-offset/future-start validation, UTC `Z` serialization, Problem Details error codes/trace IDs, OpenAPI documentation, Swagger request documentation, and automated API contract tests. |
| Part 2 | [[research/2/02-availability-and-scheduling\|02-availability-and-scheduling]] | Availability algorithm, opening/shift/qualification/active-state rules, DST policy, indexed conflict queries, existing-index assessment, schema impact, and availability verification data. |
| Part 3 | [[research/2/03-catalog-cache-and-resilience\|03-catalog-cache-and-resilience]] | Cache-aside contract, key/payload/TTL/invalidation, Redis connection and cache-operation budgets, SQL fallback, health degradation, Compose and Testcontainers changes. |
| Part 4 | [[research/2/04-verification-and-documentation\|04-verification-and-documentation]] | Test architecture/fixtures, T-02-T-13 scenario matrix, commands, evidence rules, documentation/deployment impact, and handoff to planning. |

The overview owns shared scope, consolidated decisions, and Phase 7 traceability. Each part owns its concern-specific evidence, audit, gaps, and proposed changes. Later phases must use this overview as the primary research entry point.

## Phase 0: Scope lock

### 0.1 Ticket requirements

| ID / source | Requirement text (condensed) | Acceptance evidence | Relevant assumption / rule |
| --- | --- | --- | --- |
| S2-01 | Return seeded dealerships with IANA time-zone IDs and dealership-scoped service catalogs with explicit response contracts and stable ordering. | AC2-01: three endpoints and success/error DTOs in OpenAPI; discovery/list ordering, empty catalogs, missing dealership behavior correct. | Supports D-02; local synthetic catalogs are read-only seeded configuration (AS-08). |
| S2-02 | Validate an explicit-offset future start with injected `TimeProvider`; derive duration/end from SQL Server; evaluate the full interval in dealership-local opening time and UTC elapsed time. | AC2-02 and AC2-03: time-offset, strict future-start, DST, boundary, and authoritative-duration tests pass; availability returns documented fields for both outcomes. | AS-03, AS-04, AS-05, AS-06. |
| S2-03 | Check an active, qualified technician and active bay for the full interval, within one opening interval and one technician shift, with independent SQL-side conflict checks in a stable order. | AC2-02: qualification, inactive resource, dealership scope, coverage, and interval tests pass. | AS-01, AS-06, AS-07; A-02. |
| S2-04 | Return an advisory availability result with consistent Problem Details: `400` for invalid input, `404` for missing/out-of-dealership resources, `503` for classified SQL unavailability, and sanitized `500` for unexpected failures. | AC2-03 and AC2-06: both availability outcomes use the documented fields; SQL outage produces controlled failure, not false availability. | A-02, D-02; BR-08. |
| S2-05 | Use `IDistributedCache` with Redis cache-aside for catalogs, absolute TTL, explicit invalidation/versioning, bounded SQL fallback, degradation logging, optional health degradation, and startup/readiness behavior that tolerates an unreachable Redis. | AC2-04 and AC2-05: miss/hit/expiry/invalidation against real Redis and SQL pass; outage/stall falls back within the documented budget; startup and readiness remain correct. | P-02; BR-09; Redis is not authoritative for scheduling or booking. |
| S2-06 | Provide interactive Swagger API documentation, automated verification, and updated implementation/configuration/cache/schema records; no standalone demo script is required. | AC2-07: README, Swagger/manual API instructions, cache/schema docs, and recorded test results reflect implemented behavior. | D-01-D-04, P-04. |

### 0.2 Acceptance criteria

| ID | Acceptance criterion | Research owner |
| --- | --- | --- |
| AC2-01 | All three endpoints and success/error DTOs are documented in OpenAPI and exercised by automated API integration tests using synthetic data; Swagger UI supports supplementary manual requests; ordering, empty catalogs, and missing-dealership behavior match S2-01. | Parts 1 and 4 |
| AC2-02 | Qualification, inactive resource, dealership scope, full opening/shift coverage, interval boundary, time-offset, strict future-start, and DST tests pass. | Parts 1, 2, and 4 |
| AC2-03 | Availability reads existing confirmed appointments correctly, returns the documented fields for either outcome, uses SQL duration even when the catalog cache is stale, and does not mutate data or promise a subsequent booking. | Parts 1, 2, and 4 |
| AC2-04 | Catalog miss, hit, expiry, and explicit invalidation pass against real Redis and SQL Server; hits demonstrably avoid the catalog SQL query. | Parts 3 and 4 |
| AC2-05 | Redis outage/stall produces correct catalog results within the documented cache budget through SQL fallback; availability remains correct, startup succeeds, and readiness does not fail solely because Redis is down. | Parts 3 and 4 |
| AC2-06 | SQL unavailability on SQL-dependent reads produces the documented controlled failure, not a false availability result; a valid catalog cache hit may still return `200`; readiness remains `503`. | Parts 1, 3, and 4 |
| AC2-07 | README, Swagger/manual API instructions, cache/schema documentation, and recorded test results reflect implemented behavior. | Part 4 |

### 0.3 Ticket verification cases

| ID | Check | Level | Expected result | Requirements |
| --- | --- | --- | --- | --- |
| T-02 | Qualification or inactive resource | Unit + integration | Unqualified/inactive candidates never satisfy availability. | A-02, S2-03 |
| T-03 | Partial shift/opening coverage | Unit + integration | A resource free at the start but unavailable later cannot satisfy availability for the full interval. | A-02, S2-02, S2-03 |
| T-04 | Interval boundaries | Unit + SQL Server | Adjacent intervals are permitted; partial overlap, containment, and identical confirmed intervals are rejected for either resource; reads create no reservations. | A-02, S2-03 |
| T-10 | Time handling | Unit + API | Reject past/offset-free starts; accept equivalent explicit-offset instants; evaluate local opening boundaries, including a DST example. | A-02, S2-02 |
| T-11 | Dealership scope | API + SQL Server | Reject missing dealerships/services and out-of-dealership services; foreign-dealership technicians/bays never satisfy availability; catalog keys and SQL queries remain dealership scoped. | A-02, S2-01, S2-03 |
| T-12 | Cache lifecycle | Redis + API | Miss populates cache; hit uses it; expiry or explicit invalidation causes a fresh database read. | P-02, S2-05 |
| T-13 | Redis unavailable | API + SQL Server + Redis | Catalog uses bounded SQL fallback, availability remains correct, degradation is logged, and readiness does not fail solely because Redis is unavailable. | P-02, S2-05 |

### 0.4 Exclusions and necessary dependencies

| Item / ticket source | Classification | Reason and requirement link |
| --- | --- | --- |
| Three discovery/catalog/availability GET endpoints and their DTOs | Required | S2-01, S2-02, S2-04, AC2-01. |
| Injected `TimeProvider`, explicit-offset parsing, UTC normalization, local opening evaluation | Required | S2-02, AS-03, AS-05. |
| Availability SQL reads for eligibility, shifts, opening hours, and both conflict checks | Required | S2-03, A-02, BR-04-BR-06. |
| Problem Details error-code/trace-ID customization and narrow SQL failure classification | Required | S2-04, AC2-06. |
| Redis `IDistributedCache` cache-aside with TTL, invalidation, bounded fallback, health degradation | Required | S2-05, P-02, AC2-04-AC2-06. |
| OpenAPI operation/schema documentation, Swagger/manual API instructions, tests, README/setup/database/cache records | Required | S2-06, AC2-01, AC2-07. |
| Standalone demo script (`scripts/ticket2-demo.ps1`) | Retired | 2026-11-01 ticket amendment: no standalone demo script is required; automated API integration tests exercise synthetic requests and Swagger UI supports supplementary manual requests (S2-06, AC2-01). |
| `Microsoft.Extensions.Caching.StackExchangeRedis`, `Testcontainers.Redis`, Redis image | Necessary dependency | Required to implement and verify S2-05; exact versions selected in Part 3. |
| Existing EF Core/SQL Server schema and indexes | Necessary dependency | S2-03 reads existing catalogs, shifts, qualifications, opening hours, and appointments; no schema change is expected. |
| Existing setup/runtime identities and fixtures | Necessary dependency | Reads use `scheduler_app`; test appointment/catalog probes use the privileged setup identity. |
| Booking `POST`, retrieval, booking procedure, concurrency, `409` capacity handling | Excluded | Ticket 2 owns the A-02 read path; ticket 3 owns confirmation/retrieval and the procedure. |
| Telemetry infrastructure (Serilog, OpenTelemetry, Prometheus, Tempo, Grafana) | Excluded | Ticket 4; ticket 2 logs degradation through the available `ILogger`. |
| Authentication/authorization, admin CRUD, cancellation/rescheduling, idempotency, vehicle-overlap protection | Excluded | README/architecture exclusions; initial local demo is unauthenticated. |
| Cache stampede protection, refresh-ahead, distributed locking | Excluded | Not required by P-02; TTL and bounded fallback suffice for this read-only catalog. |
| Holidays, overnight opening intervals, local-time booking input | Excluded | AS-06 and ticket availability rules. |
| A custom frontend or browser automation | Excluded | Backend-only local release; Swagger UI is generated. |

### 0.5 Attachments

No images, PDFs, or other attachments are referenced by ticket 2. `docs/assets/` contains only its guide, so no asset inspection applies.

## Current architecture and entry points

> [!NOTE]
> Ticket 1 is implemented and locally verified; ticket 2 business behavior was absent at the evidence baseline and is now implemented as recorded in [[test/2-test|the test report]]. The paths and symbols below were inspected at the evidence baseline.

| Component | Inspected evidence | Status |
| --- | --- | --- |
| API composition root | `src/ServiceScheduler.Api/Program.cs:14-66` builds the web host, registers controllers, default Problem Details, built-in OpenAPI, options validation, `SchedulerDbContext`, and health checks; `--db-setup` branches before host construction at `Program.cs:9-12`. | Existing; business endpoints absent. |
| Controllers and feature folders | `services.AddControllers()` exists at `Program.cs:19`, but no `Features/` or controller files exist in the checkout. | Missing — proposed addition. |
| Application time | No `TimeProvider` registration exists in `Program.cs`; only setup code passes `TimeProvider.System` (`DatabaseSetupCommand.cs:33`) and the seeder takes an injected `TimeProvider` (`FutureShiftSeeder.cs:12`). | Missing — proposed addition for API. |
| Problem Details | `AddProblemDetails()` and `UseExceptionHandler()`/`UseStatusCodePages()` are registered (`Program.cs:20,47-48`); the unknown-route test asserts 404 Problem Details with `traceId` and no stack trace (`ProblemDetailsTests.cs:10-31`), but no stable application `code` extension or DB-failure handler exists. | Existing baseline; Must Change for S2-04. |
| Database reads | `SchedulerDbContext` exposes the ten domain sets and applies configurations (`SchedulerDbContext.cs`); all reads currently in the API are health-check reads. | Existing; no business read path. |
| Domain model and mappings | Entities in `Infrastructure/Persistence/Entities/`, configurations in `Infrastructure/Persistence/Configurations/`; schema reference records tables, checks, composite FKs, and indexes (`docs/database/schema.md:24-74`). | Existing; no schema change expected. |
| Static seeds | `Infrastructure/Persistence/Configurations/SeedData.cs:52-66` seeds Saigon/London dealerships; `:68-71` and `:232-244` seed weekday 09:00-17:00 opening hours; `:109-132` seeds three service types; `:134-230` seeds technicians, qualifications, and bays. | Existing; test-only probes can add rows through setup. |
| Future shifts | `FutureShiftSeeder.cs:20-91` captures the clock once, starts at the next dealership-local calendar day, and adds weekday 09:00-17:00 shifts for a 28-day horizon; `ToUtc` rejects invalid local times (`:116-127`). | Existing; availability tests should use the same fixed clock (`SqlServerContainerFixture.cs:20`). |
| Initial migration | `Migrations/20261031092016_InitialCreate.cs` creates the schema, seeds, checks, and indexes; no procedure or `EXECUTE` grant. | Existing; no ticket 2 migration expected. |
| Runtime identity | `RuntimePrincipalProvisioner.cs` grants only `CONNECT` and `SELECT ON SCHEMA::dbo`; `RuntimePermissionTests` proves reads and denied appointment writes/DDL. | Existing; availability/catalog reads use this identity unchanged. |
| Health endpoints | `/health/live` selects no checks; `/health/ready` selects `ready`-tagged `AddDbContextCheck` with a 5-second timeout (`Program.cs:34-43,60-64`). | Existing; Redis degradation is absent. |
| OpenAPI/Swagger | `/openapi/v1.json` and `/swagger/index.html` are mapped in Development (`Program.cs:50-57`); the ticket 1 document has no business operations (`OpenApiTests.cs:10-42`). | Existing; Must Change for S2-06/AC2-01. |
| Test fixtures | `SchedulerApiFactory` sets Development and in-memory runtime connection (`SchedulerApiFactory.cs:11-39`); `SqlServerContainerFixture` starts one SQL container per collection with the pinned image and fixed clock at `:22`, one database per class (`SqlServerContainerFixture.cs:17-22,64-125`). | Existing; Must Change/extend for cache and controllable API clock. |
| Compose | SQL Server, one-shot `db-setup`, and API only (`compose.yaml:3-57`); API depends on setup success, not Redis. | Existing; Must Change to add Redis without a startup gate. |
| Configuration | `ConnectionStrings:Runtime` is validated for API mode (`DatabaseOptions.cs`, `DatabaseOptionsValidator.cs`); setup uses `ConnectionStrings:Setup`/`Scheduler:RuntimePassword`; no cache configuration exists. | Existing; Must Change for `ConnectionStrings:Cache` and cache options. |
| Package pins | Central versions use `10.0.13` for the ASP.NET Core/EF Core Microsoft packages (with `Microsoft.Extensions.TimeProvider.Testing` `10.0.0`) and `4.14.0` for Testcontainers.MsSql (`Directory.Packages.props:6-18`); no Redis package exists. | Existing; Must Change. |
| Executable examples/scripts | `scripts/` contains no files; README examples cover health/OpenAPI only. | Retired by the 2026-11-01 scope amendment; no standalone script is required. Automated API integration tests and Swagger UI provide the request coverage. |

### Proposed request flow (pre-implementation design)

```text
GET /api/dealerships
  -> controller -> EF Core AsNoTracking ordered read -> DTO[] -> 200

GET /api/dealerships/{dealershipId}/service-types
  -> bounded Redis get (250 ms)
     -> hit (including []) -> cached DTO[] -> 200
     -> miss/timeout/cache error -> SQL dealership existence + ordered services
        -> missing dealership -> 404, no cache write
        -> SQL dependency failure -> classified 503, no cache write
        -> success -> bounded cache set (250 ms), log write failure -> 200

GET /api/dealerships/{dealershipId}/availability?serviceTypeId=...&startsAt=...
  -> controller parses explicit-offset startsAt -> captured TimeProvider instant
  -> EF read service duration + dealership time zone (dealership-scoped)
  -> compute UTC end, overflow check
  -> convert to dealership local, require same local date, SQL opening-interval check
  -> EF existence queries for one eligible technician (active, qualified, one shift,
     no overlapping confirmed appointment) and one eligible active bay (no overlap)
  -> 200 { dealershipId, serviceTypeId, available, startsAt, endsAt, evaluatedAt }
```

Only cache population is a write in ticket 2. Availability performs no writes and never consults Redis.

## Phase 1: Consolidated layer coverage

| Component | Coverage and evidence | Research part |
| --- | --- | --- |
| API contract and validation | Missing — proposed controllers/DTOs, explicit-offset parsing, stable error codes, UTC `Z` responses. | Part 1 |
| Scheduling rules | Missing — proposed pure time/opening/duration rules plus SQL-authoritative eligibility checks. | Part 2 |
| Resource selection and availability | Missing — proposed bounded technician and bay existence queries with half-open overlap predicates. | Part 2 |
| Domain model and EF mappings | Relevant — existing entities/FKs/checks/indexes support the queries; no schema change expected. | Part 2 |
| EF Core migrations and seeds | Relevant — no migration anticipated; existing static seeds and fixed-clock future shifts supply test data; test probes use setup identity. | Part 2 |
| Booking procedure and transactions | N/A — ticket 3 owns confirmation and the procedure; ticket 2 performs reads only. | Overview / ticket 3 handoff |
| Runtime permissions | Relevant — existing `scheduler_app` `SELECT` is sufficient; no new grants or direct writes. | Part 2 |
| Failure and retry handling | Relevant — proposed Problem Details customization and narrow SQL unavailability classification; no booking retries in ticket 2. | Part 1 |
| Redis catalog cache | Missing — proposed cache-aside, key/payload/TTL/invalidation, bounded fallback, health degradation. | Part 3 |
| Configuration and packaging | Must Change — add `ConnectionStrings:Cache`, cache options, Redis Compose service, Testcontainers.Redis, package pins. | Parts 3 and 4 |
| Telemetry | Partial — use existing `ILogger` for degradation; full Serilog/OpenTelemetry remains ticket 4. | Part 3 |
| Unit tests | Missing — proposed pure interval/time-zone/opening/validation rules and cache wrapper tests. | Part 4 |
| API and dependency integration tests | Missing — proposed WebApplicationFactory + real SQL Server + real Redis tests, outage/stall checks, deterministic clock and isolated keys. | Part 4 |
| Operational verification | Missing — proposed Compose Redis-down/up, cache invalidation, startup/readiness, and Swagger/manual API checks. | Part 4 |
| Documentation and delivery | Must Change — README, architecture, setup, schema/cache/migration records, docs index, deploy notes, test report. | Part 4 |
| UI or new integrations | N/A — backend-only ticket; Swagger UI is the generated API client. | Overview |

### 1.2 Workflow change checklist

| Check | Evidence to collect | Status |
| --- | --- | --- |
| All callers and entry points | New controller routes, DI registrations, cache service calls, health-check registration, test factory settings. | Proposed; none exist yet. |
| Rules enforced across boundaries | App validation versus SQL duration/eligibility/overlap checks; repository/query predicates remain dealership scoped. | Proposed; inspect generated SQL and runtime identity. |
| Read and write consistency | Response mapping for both availability outcomes; SQL duration independent of cached catalog; cache hit/miss DTO equality and ordering. | Proposed; Part 3 scenarios. |
| Failure paths | Cache timeout/connection failure fallback; request cancellation propagation; SQL failure classification; sanitized 500; no cache write on failures. | Proposed; Parts 1 and 3 scenarios. |
| Verification coverage | Requirement-linked unit/API/SQL/Redis tests, deterministic clocks, isolated cache keys, final state assertions. | Proposed; Part 4 matrix. |

## Phase 2: Consolidated data-flow trace

### 2.1 READ paths

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Dealership discovery | HTTP -> controller -> `Dealerships.AsNoTracking()` ordered by name/id -> DTO -> `200` | Runtime identity; SQL only; no cache. | Missing controller; existing entity/view no DTO. |
| Service catalog | HTTP -> bounded Redis get -> hit -> DTO; miss/timeout -> EF dealership existence + ordered `ServiceTypes` -> bounded cache set -> DTO | Cache key is dealership scoped; only successful existing-dealership results cached; SQL remains authoritative for availability. | Missing cache and endpoint. |
| Availability | HTTP -> parse/validate -> EF service+dealership read -> UTC end -> local opening check -> EF technician/bay eligibility with `NOT EXISTS` overlap -> DTO | `TimeProvider` clock captured once; no cache; no writes; SQL half-open predicates. | Missing endpoint/rules; existing indexes cover predicates. |
| Appointment retrieval | N/A — ticket 3. | N/A | Excluded. |

### 2.2 WRITE paths

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Catalog cache population | SQL success -> bounded `IDistributedCache.SetAsync` with absolute TTL | Cache write failure must not fail the request; no write for 404/SQL failure. | Proposed; Redis absent. |
| Appointment confirmation/procedure | N/A — ticket 3. | N/A | Excluded; no runtime writes. |
| Migration / synthetic seeding | No ticket 2 schema or seed change; test probes use the privileged setup identity. | Existing setup identity; no runtime grant change. | Existing; verify no migration needed. |

### 2.3 Path coverage verification

| Affected path / requirement | Value source and rule enforcement | Failure / cleanup behavior | Verification scenario | Observed evidence |
| --- | --- | --- | --- | --- |
| Discovery S2-01 | SQL `Dealerships` seeded rows; ordered by name/id | SQL outage -> 503; no cache | API + SQL test; unknown route unchanged | Not run |
| Catalog S2-01/S2-05 | Redis cached DTO or SQL `ServiceTypes`; dealership existence checked on miss | Cache errors -> SQL fallback; SQL error -> 503; no cache on 404/failure | T-11, T-12, T-13 | Not run |
| Availability S2-02/S2-03 | SQL service duration, dealership zone, opening rows, technicians/qualifications/shifts/bays/appointments | SQL outage -> 503; invalid input -> 400; no eligible pair -> 200 `available:false`; no writes/cache | T-02, T-03, T-04, T-10, T-11 | Not run |
| Cache population S2-05 | SQL catalog DTO serialized as v1 JSON with absolute TTL | Timeout/connection failure logged; caller waits bounded; request cancellation propagates | T-12, T-13; stall/outage tests | Not run |

## Phase 3: UX state matrix

N/A — ticket 2 adds backend GET operations and uses generated Swagger UI, not a custom frontend. Request/response and error states are covered by Part 1 and the Phase 2 data-flow trace.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict / evidence |
| --- | --- | --- | --- | --- |
| `Program.cs` | Composition root; DI/middleware/health/OpenAPI | Controllers, Problem Details, OpenAPI, options validation, DbContext, health; no business endpoint, Redis, `TimeProvider`, cache health, or exception handler. | S2-01-S2-06 | Must Change. |
| `SchedulerDbContext.cs` | Read boundary | Ten DbSets, configurations, convention FK-index removal; no query helpers. | S2-03 | No Change; use directly for reads. |
| Entity configurations | Schema/index contract | Existing indexes: `IX_TechnicianShifts_TechnicianId_StartUtc` (includes `EndUtc`), `IX_Technicians_DealershipId`, `IX_TechnicianQualifications_ServiceTypeId_DealershipId`, `IX_Appointments_DealershipId_TechnicianId_StartUtc` (includes `EndUtc, Status`), `IX_Appointments_DealershipId_ServiceBayId_StartUtc` (includes `EndUtc, Status`), opening-hours PK, service/bay unique indexes. | S2-03, T-02-T-04 | No Change expected; verify generated SQL/index use. |
| `SeedData.cs` | Seeded dealerships/services/hours/resources | Two dealerships with IANA zones; weekday 09:00-17:00 hours; three services; active/inactive technicians and bays; qualifications. | S2-01-S2-03 | No Change; tests add probes via setup. |
| `FutureShiftSeeder.cs` | Seeded shift horizon | Fixed-clock relative, next local day + 28 days, weekday 09:00-17:00, idempotent; invalid local time throws. | S2-02, S2-03 | No Change; tests use fixed clock `2026-11-02T00:00Z`. |
| `20261031092016_InitialCreate.cs` | Schema/index migration | Creates tables/checks/FKs/indexes/seeds; no procedure/grants. | S2-03 | No Change; no migration expected. |
| `RuntimePrincipalProvisioner.cs` | Runtime permissions | Grants `CONNECT` and `SELECT ON SCHEMA::dbo`; rejects unexpected roles/permissions. | S2-01-S2-03 | No Change; reads only. |
| `SchedulerApiFactory.cs` | Test host configuration | Development environment; in-memory runtime connection only. | S2-01-S2-06 | Must Change: cache settings, controllable `TimeProvider`, optional test services. |
| `ProblemDetailsTests.cs` | Existing error contract | Asserts 404 Problem Details, `traceId`, no stack/password. | S2-04 | Must Change/extend: assert stable `code`; 400/503/500 cases. |
| `OpenApiTests.cs` | Existing OpenAPI | Asserts document and Swagger UI exist; no business operations. | S2-06, AC2-01 | Must Change/extend: assert three operations, DTO schemas, ordering docs, error responses. |
| `HealthEndpointTests.cs`, `InitializedReadinessTests.cs` | Existing readiness | Live 200 with SQL down; ready 503 with SQL down; ready 200 with initialized SQL. | S2-05 | Must Change/extend: add cache-degradation cases. |
| `MigrationAndSeedTests.cs`, `ConstraintTests.cs`, `RuntimePermissionTests.cs` | Existing SQL fixture/probe patterns | Fixed clock, one DB per class, setup probes, constraint/permission assertions. | S2-03, S2-06 | No Change; reuse patterns for ticket 2 probes. |
| `compose.yaml` | Local dependencies | SQL, setup, API only; API waits for setup success. | S2-05 | Must Change: add Redis without requiring its health. |
| `Directory.Packages.props`, `ServiceScheduler.Api.csproj` | Package pins | No caching/Redis/Testcontainers.Redis package. | S2-05 | Must Change. |
| `.env.example`, `appsettings.json`, setup docs | Configuration contract | Runtime/setup SQL only; no cache key. | S2-05 | Must Change. |
| `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, database docs, `docs/README.md` | Delivery records | Ticket 2 behavior/Redis still planned; ticket 2 research not indexed. | S2-06 | Must Change after implementation; research entry added now. |
| `scripts/` | Executable examples | Empty directory; no ticket 2 demo. | S2-06, AC2-01 | Retired by the 2026-11-01 scope amendment; no standalone script is required. |
| `docs/test/2-test.md`, `docs/deploy-notes/2-deploy-notes.md` | Verification/deployment records | Absent. | S2-06, AC2-07 | Proposed Addition during implementation. |

### Gate 2: Call path and boundary verification

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| `GET /health/live` | `Program.cs:60` -> `MapHealthChecks` with no predicate -> 200 | No dependency. | Yes (health only). | Liveness must remain dependency-free when Redis is added. |
| `GET /health/ready` | `Program.cs:61-64` -> `ready`-tagged `AddDbContextCheck` | Runtime SQL; 5 s check timeout; no retry policy. | Yes (SQL only). | Extend with a Redis check that returns `Degraded`, not `Unhealthy`, for recoverable failures. |
| `GET /openapi/v1.json`, `/swagger/index.html` | `Program.cs:50-57` | Development only. | Yes. | Must include proposed business operations. |
| Unknown `/api/...` | `UseStatusCodePages()` + `AddProblemDetails()` | No controller. | Yes (404). | Extend `code` extension; malformed GUID route must bind and return 400, not 404. |
| Proposed `GET /api/dealerships` | Missing controller -> no call chain | N/A | No. | Proposed controller/query. |
| Proposed catalog/availability routes | Missing controller -> no call chain | N/A | No. | Proposed controllers/services and tests. |
| Runtime identity | `scheduler_app` with `SELECT ON SCHEMA::dbo`; no `EXECUTE`/writes | Runtime connection. | Yes for reads; no write path. | Availability/catalog reads fit; no new grant. |

## Phase 4: Consolidated code audit

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence / remaining verification |
| --- | --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | No business controllers, `TimeProvider`, Redis, cache health, stable error codes, or DB exception handler. | Register discovery/catalog/availability controllers, `TimeProvider.System`, Redis cache/options/health, exception handler, Problem Details customization, OpenAPI transformers, JSON health writer. | S2-01-S2-06 | Inspected `Program.cs:14-66`; implementation/tests pending. |
| Must Change | `Directory.Packages.props`, `ServiceScheduler.Api.csproj`, `tests/ServiceScheduler.IntegrationTests.csproj` | No Redis/cache/test packages. | Add `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13`, `Testcontainers.Redis` `4.14.0`, and any explicit StackExchange.Redis pin if needed. | S2-05, AC2-04-AC2-06 | Package metadata checked; temporary probe restore/build verified 2026-11-01; solution integration pending. |
| Must Change | `SchedulerApiFactory.cs` | Only runtime connection config; no cache/clock/test-service hooks. | Add default unreachable cache connection, cache overrides, optional `TimeProvider` replacement and service configuration. | S2-01-S2-06 | Inspected `:11-39`; test host behavior pending. |
| Must Change | `ProblemDetailsTests.cs` | Does not assert stable `code` or 400/503/500 behavior. | Extend contract assertions and add error-path tests. | S2-04 | Inspected `:10-31`; execution pending. |
| Must Change | `OpenApiTests.cs` | Only document existence/Swagger. | Assert three operations, DTOs, ordering descriptions, and error responses; add transformer tests if applicable. | S2-06, AC2-01 | Inspected `:10-42`; execution pending. |
| Must Change | `HealthEndpointTests.cs`, `InitializedReadinessTests.cs` | No Redis degradation cases. | Add readiness/liveness/cache-status cases with SQL/Redis combinations. | S2-05, AC2-05 | Inspected; execution pending. |
| Must Change | `compose.yaml` | No Redis service/API cache env. | Add pinned Redis service and API `ConnectionStrings__Cache`; do not gate API startup on Redis health. | S2-05, AC2-05 | Inspected `:3-57`; Compose run pending. |
| Must Change | `.env.example`, `appsettings.json` | No cache configuration. | Add `ConnectionStrings__Cache` and non-secret cache defaults. | S2-05 | Inspected; startup tests pending. |
| Must Change | `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/database/schema.md`, `docs/database/overview.md`, `docs/database/migration.md`, `docs/database/cache.md`, `docs/README.md` | Ticket 2 remains planned; ticket 2 research not indexed. | Record implemented behavior, configuration, endpoints, cache inventory, no-schema-change rationale, test evidence, and links after implementation. | S2-06, AC2-07 | Inspected; documentation updates pending implementation. |
| Need Review | Existing appointment/technician/bay/shift indexes | Predicate coverage appears sufficient; generated SQL for the proposed query shapes was captured on 2026-11-01 and contains the required dealership/resource/time/overlap predicates. | Confirm execution-plan/index use on representative data during implementation; add a migration only if a missing index is proven. | S2-03, T-02-T-04 | Schema docs `:58-66`; generated SQL verified; execution plan Not run. |
| Need Review | Opening-hour/DST policy | No API time-zone evaluation exists. | Adopt wall-clock local opening comparison with same-local-date requirement and documented repeated/nonexistent boundary policy (Part 2). | S2-02, T-10 | Policy and fixture conversions verified 2026-11-01; API/DST tests pending. |
| Need Review | Cache health representation | Existing readiness reports only SQL. | Add Redis check tagged `ready` returning `Degraded` on recoverable failure; readiness remains 200 when SQL is healthy; JSON response shows check states. | S2-05, AC2-05 | `Degraded` -> HTTP `200` verified 2026-11-01; readiness body and Redis registration pending implementation. |
| No Change | `SchedulerDbContext.cs` | Model/config boundary; direct EF reads. | Use directly; no generic repository. | S2-03 | Inspected. |
| No Change | Entity configurations | Existing keys/checks/indexes cover query predicates. | No schema change expected; verify generated SQL. | S2-03 | Inspected configurations and `docs/database/schema.md`. |
| No Change | `SeedData.cs`, `FutureShiftSeeder.cs` | Deterministic catalogs/resources/shift horizon. | Do not change production seeds for tests; add probes via setup in test databases. | S2-01-S2-03, T-02-T-04 | Inspected. |
| No Change | `20261031092016_InitialCreate.cs` | Existing schema/indexes/seeds. | No ticket 2 migration expected. | S2-03 | Inspected migration record. |
| No Change | `RuntimePrincipalProvisioner.cs` | Least-privilege runtime reads. | No new grants; no direct writes. | S2-01-S2-03 | Inspected; existing permission tests. |
| No Change | `DatabaseSetupRunner.cs`, `DatabaseSetupCommand.cs`, `SchedulerSetupOptions.cs` | Setup path. | No ticket 2 change unless a migration is later proven necessary. | S2-03 | Inspected. |
| No Change | `MigrationAndSeedTests.cs`, `ConstraintTests.cs`, `RuntimePermissionTests.cs` | Existing schema/permission evidence. | Keep; add ticket 2 tests in new files. | S2-03, S2-06 | Inspected. |
| Proposed Addition | `Features/Dealerships/`, `Features/ServiceCatalog/`, `Features/Availability/` controllers/contracts/services | No business endpoints. | Add HTTP-focused controllers, feature-local DTOs, query/use-case services, request parser, pure rules. | S2-01-S2-04 | Not present; design in Parts 1-2. |
| Proposed Addition | `Infrastructure/Caching/` options, cache service/wrapper, health check | No cache. | Add bounded `IDistributedCache` wrapper, key/payload contract, Redis options, health check. | S2-05 | Not present; design in Part 3. |
| Proposed Addition | `Infrastructure/Errors/` problem customization, SQL classifier, exception handler | Default Problem Details only. | Add stable `code`/`traceId`, narrow SQL failure classification, sanitized 500. | S2-04 | Not present; design in Part 1. |
| Proposed Addition | Test fixtures/tests for Redis, availability, catalog, Problem Details, OpenAPI | No ticket 2 tests. | Add SQL+Redis fixtures/collections, data helpers, unit/integration tests. | S2-01-S2-06 | Not present; design in Part 4. |
| Retired | `scripts/ticket2-demo.ps1` | No executable examples. | Retired by the 2026-11-01 scope amendment; automated API integration tests and Swagger UI cover S2-06/AC2-01. | S2-06, AC2-01 | No longer required; historical design in Part 1/4. |
| Proposed Addition | `docs/test/2-test.md`, `docs/deploy-notes/2-deploy-notes.md`, `docs/test/2-manual-test.md` (later) | Absent. | Record implementation execution, deployment/cache recovery, and manual checklist. | S2-06, AC2-07 | Not present; implementation/test phases. |

## Phase 5: Consolidated gap register

Detailed analysis is in the parts. Status vocabulary: **Resolved** means the research decision is accepted; **Verified** means the stated evidence was executed during research verification on 2026-11-01; **Deferred** means the check requires the ticket implementation. No gap remains Open at the research level.

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact | Status / evidence |
| --- | --- | --- | --- | --- | --- |
| G-01 | Decision | What cache-operation budget and underlying Redis limits should be selected? | 250 ms caller budget per cache operation; 500 ms configured cumulative cache wait for a read+populate request; Redis `ConnectTimeout`/`SyncTimeout`/`AsyncTimeout` 500 ms, `ConnectRetry` 1, `AbortOnConnectFail=false`, `BacklogPolicy.FailFast`; request cancellation propagates. | S2-05, AC2-05 | Resolved and probed 2026-11-01: registration 17-20 ms, warm operations 254-265 ms, cold read+populate 508 ms, paused Redis 257-265 ms, recovery 1 ms. Treat 500 ms as the configured budget, not a strict wall-clock SLA; runtime stall/API tests deferred. |
| G-02 | Decision | Which Redis package and image versions should be pinned? | `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13` (matches existing Microsoft 10.0.13 baseline; depends on StackExchange.Redis `2.7.27`), `Testcontainers.Redis` `4.14.0` (matches Testcontainers.MsSql), `redis:7.4.12-alpine@sha256:8da19c7399dca900c6822a1cf30f9d2285585e2c4b457ff4095b0f4500054d3f`. | S2-05, AC2-04-AC2-06 | Verified 2026-11-01: packages restored and compiled; StackExchange.Redis resolved to 2.7.27; image pulled by digest, `linux/amd64`, `redis-cli ping` = `PONG`, server v7.4.12. |
| G-03 | Decision | Exact cache key, UUID format, payload and version? | Key `scheduler:v1:dealership:{dealershipId:D}:service-types` with lowercase hyphenated GUID; JSON array of `{id,name,durationMinutes}` using web-default camelCase; version `v1`; ordering by name/id. | S2-01, S2-05 | Resolved and format-verified 2026-11-01: `Guid:D` produced the lowercase hyphenated form and web-default JSON emitted camelCase; catalog lifecycle tests deferred. |
| G-04 | Decision | How should readiness expose optional Redis degradation? | Add a `redis` health check tagged `ready` returning `Degraded` on recoverable Redis failures; readiness JSON shows check states; Healthy/Degraded -> 200, Unhealthy (SQL) -> 503. | S2-05, AC2-05 | Resolved and framework-verified 2026-11-01: a `Degraded` check returned HTTP `200` under default `MapHealthChecks`; readiness body assertions deferred. |
| G-05 | Decision | What is the DST/opening-boundary policy? | Convert UTC instants to dealership-local wall clock; require same local date; compare `TimeOnly` against one opening row. Repeated fall-back wall times compare identically; nonexistent spring-forward boundaries are compared as stored wall-clock values; shifts remain UTC-authoritative. | S2-02, T-10 | Resolved and conversion-verified 2026-11-01: fixture instants converted as documented; API/DST tests deferred. |
| G-06 | Decision | Which SQL failures map to `503`? | Narrow classifier over `SqlException.Number` connectivity/timeout/deadlock codes (`-2`, `2`, `53`, `121`, `233`, `1205`, `1222`, `4060`, `40197`, `40501`, `40613`, `49918`-`49920`, `10053`, `10054`, `10060`, `10061`, `10928`, `10929`, `11001`) traversing inner exceptions; others remain sanitized `500`. | S2-04, AC2-06 | Resolved; classifier and API error-path tests deferred to implementation. |
| G-07 | Decision | Is a schema migration required? | No schema change expected: existing opening-hours PK and appointment/technician/shift/qualification indexes cover the predicates. Capture generated availability SQL and document index mapping; add a migration only if inspection proves a gap. | S2-03 | Resolved and partially verified 2026-11-01: generated SQL captured for all four query shapes with the required predicates and `NOT EXISTS`; execution-plan/index confirmation remains implementation-time. No migration planned. |
| G-08 | Decision | How should timestamps be serialized as UTC `Z`? | Response DTOs use `DateTime` values with `Kind=Utc`, produced from `DateTimeOffset.UtcDateTime`; request parsing retains `DateTimeOffset` and normalizes to UTC. | S2-02, AC2-03 | Resolved and verified 2026-11-01: `System.Text.Json` emitted a trailing `Z`; API serialization test deferred. |
| G-09 | Decision | How are catalog seed changes invalidated? | Bump the key version (`v1` -> `v2`) or delete affected keys with `redis-cli DEL`; TTL bounds staleness to 10 minutes if invalidation is missed. | S2-05 | Resolved; documentation and invalidation test deferred. |
| G-10 | Decision | How to test Redis outage and stall? | Use Testcontainers `PauseAsync`/`UnpauseAsync` (confirmed in `Testcontainers` 4.14.0) for stalls; use an unreachable cache endpoint and real SQL for connection failure; use a Redis test collection with `DisableParallelization` and per-class Redis database index. | S2-05, AC2-05 | Resolved and probed 2026-11-01: `PauseAsync`/`UnpauseAsync` compiled and executed against the pinned image; API-level outage tests deferred. |
| G-11 | Decision | Stable application error codes and trace ID? | `validation_error`, `not_found`, `dependency_unavailable`, `internal_error`; extension names `code` and `traceId`; framework-generated validation responses customized consistently. | S2-04 | Resolved; error-path tests deferred. |
| G-12 | Decision | How to test empty catalogs and unknown dealerships? | Insert a temporary dealership with no services via setup; unknown GUID returns 404 and creates no cache key. | S2-01, AC2-01, T-11 | Resolved; tests deferred. |
| G-13 | Decision | How to document OpenAPI contracts/examples? | Controller attributes plus OpenAPI operation/schema transformers for summaries, ordering, error schemas, and examples; Swagger UI remains the interactive client. | S2-06, AC2-01 | Resolved and capability-verified 2026-11-01: `IOpenApiOperationTransformer`/`IOpenApiSchemaTransformer` and type-based/delegate transformer types exist in `Microsoft.AspNetCore.OpenApi` 10.0.13; document assertions deferred. |
| G-14 | Decision | How should executable examples avoid expiring dates? | Retired with the standalone demo script by the 2026-11-01 scope amendment. Automated API tests use the fixed clock and seeded 28-day horizon to compute future starts; OpenAPI/Swagger documentation avoids embedding expiring dates. | S2-06, AC2-01 | Retired; no standalone script is required. |
| G-15 | Decision | How to isolate Redis test data? | One Redis container per collection; each test class uses a distinct `defaultDatabase` index and a non-parallel collection; tests delete their own keys. | S2-05, AC2-04 | Resolved; tests deferred. |
| G-16 | Decision | How is `TimeProvider` supplied to the API and tests? | `AddSingleton(TimeProvider.System)` in the API; `SchedulerApiFactory` replaces it with `FakeTimeProvider(2026-11-02T00:00Z)` for deterministic availability tests. | S2-02 | Resolved; `Microsoft.Extensions.TimeProvider.Testing` `10.0.0` and `FakeTimeProvider` are present in the fixtures; API registration tests deferred. |
| G-17 | Risk | Redis image digest/tag may be stale or platform-specific. | Pin tag+digest and `linux/amd64`; record registry metadata check; pull/run and verify `redis-cli ping` during implementation. | S2-05 | Verified 2026-11-01: pull by digest succeeded; image is `linux/amd64`; `redis-cli ping` returned `PONG`. |
| G-18 | Decision | What happens on corrupt cached JSON? | Treat `JsonException` as a cache-data miss: log error, best-effort remove key, read SQL, repopulate; do not catch unrelated programming exceptions. | S2-05 | Resolved; unit test deferred. |
| G-19 | Decision | How to prove catalog hits avoid SQL? | Use a populated Redis key with SQL pointed at an unreachable endpoint; a `200` catalog proves no SQL dependency; a separate miss test proves SQL read/population. | S2-05, AC2-04, AC2-06 | Resolved; API hit/miss tests deferred. |
| G-20 | Decision | How to test sanitized unexpected `500` without a test-only endpoint? | Insert a temporary dealership with an invalid IANA `TimeZoneId` plus a dealership-scoped service type via setup and request availability; assert `500` Problem Details with `code`/`traceId` and no stack/password. | S2-04 | Resolved with scenario correction; deferred to TC2-29. |
| G-21 | Decision | Does ticket 2 need deployment notes? | Yes: Redis service/config, cache namespace/invalidation, no migration, readiness/smoke checks, rollback. Create `docs/deploy-notes/2-deploy-notes.md`. | S2-06 | Deferred to the implementation phase. |
| G-22 | Decision | What cache behavior applies on a cache hit for a dealership later removed from SQL? | No SQL check on a valid hit; seeded catalogs are read-only and there is no delete API. TTL (10 min) and versioned invalidation bound staleness; document the limitation. | S2-01, S2-05 | Resolved with documented limitation. |

## Phase 6: Dependency-ordered change plan

Detailed file-level plans belong to the plans phase. This order keeps the cache/read path, API contract, and verification aligned.

| Step | Requirement IDs | Actual or proposed files / components | Change and dependency | Verification / completion criteria |
| --- | --- | --- | --- | --- |
| 1 | S2-02, S2-04, S2-05 | `Directory.Packages.props`, API/test project files, `Program.cs` | Add cache/test packages, `TimeProvider.System`, Redis registration/options, Problem Details customization, exception handler, cache health check, JSON health writer. | Restore/build; startup with missing/malformed config fails; unreachable Redis still starts. |
| 2 | S2-01, S2-04 | `Features/Dealerships/`, `Infrastructure/Errors/`, `Program.cs` | Add discovery controller/DTO and problem-details/error-code behavior. | API test: ordered dealerships, `Z` timestamps where applicable, 400/404 codes/trace IDs. |
| 3 | S2-01, S2-05 | `Features/ServiceCatalog/`, `Infrastructure/Caching/` | Add catalog controller, cache-aside service, key/payload/TTL, bounded operations, invalidation/versioning. | T-11/T-12/T-13 cache tests; empty/unknown catalog behavior. |
| 4 | S2-02, S2-03, S2-04 | `Features/Availability/`, `Features/.../Rules`, persistence reads | Add request parser, pure time/opening rules, availability service, SQL eligibility/conflict queries, DTO mapping. | T-02/T-03/T-04/T-10/T-11; generated SQL/index review; no writes. |
| 5 | S2-05, AC2-05 | `compose.yaml`, `.env.example`, `appsettings.json`, setup docs | Add Redis service/config; API must start/serve with Redis unreachable; readiness 200/503 behavior. | Compose config; Redis stop/start; readiness/liveness checks. |
| 6 | S2-06, AC2-01 | OpenAPI transformers, Swagger/manual instructions, automated API integration tests | Document OpenAPI operation/schema contracts and provide Swagger/manual request instructions exercised by automated API integration tests. | OpenAPI includes three operations/DTOs/errors/ordering; automated API tests cover the documented requests; no standalone script is required. |
| 7 | S2-01-S2-06 | `tests/ServiceScheduler.UnitTests/`, `tests/ServiceScheduler.IntegrationTests/` | Complete/extend test infrastructure (Redis fixture, SQL+Redis collection, factory clock/services, data helpers) and the full requirement-linked matrix; tests for steps 2-4 are written alongside those steps. | `dotnet test` unit/integration; real SQL Server + Redis; T-02-T-13. |
| 8 | S2-06, AC2-07 | README, architecture, setup guide, database schema/overview/migration/cache, docs index, deploy notes, `docs/test/2-test.md` | Record implemented behavior, no-migration rationale, cache inventory, configuration, executed results, and deployment/recovery actions. | Documentation review; links; actual commands/results; unexecuted checks explicit. |

### Database and cache impact

- Database impact: **none expected**. No table, column, relationship, constraint, index, procedure, seed, or runtime-grant change is required for ticket 2. The existing opening-hours PK and the appointment/technician/shift/qualification indexes support the proposed predicates; generated availability SQL was captured on 2026-11-01 and matches the predicates, while execution-plan/index use remains implementation-time. If a missing index is proven, add a migration and update `docs/database/schema.md`, `docs/database/migration.md`, and the deploy notes.
- Cache impact: **yes**. Add the service-catalog cache inventory in `docs/database/cache.md`: key `scheduler:v1:dealership:{id:D}:service-types`, JSON payload version `v1`, absolute TTL 10 minutes (configurable), source `ServiceTypes` ordered by name/id, missing-dealership/dependency failures not cached, empty catalogs cached, version bump or `DEL` on seed changes, bounded Redis fallback, and hit/miss/expiry/invalidation/outage verification.
- Runtime permissions: no change; catalog/availability reads continue through `scheduler_app` with `SELECT ON SCHEMA::dbo`. Tests insert appointments/catalog probes through the privileged setup connection.
- Deployment: Redis service/config and cache namespace/invalidation steps belong in `docs/deploy-notes/2-deploy-notes.md`; no schema backfill or downtime.

## Rollback or forward recovery

| Step | Objects / migration IDs | Recovery action and command / working directory | Prerequisites / identity | Compatibility and data-loss limits | Verification / observed evidence |
| --- | --- | --- | --- | --- | --- |
| Schema | None for ticket 2 | No schema rollback required; if an unplanned migration is added, follow [[database/migration]] and deploy notes. | Privileged setup identity only if a migration exists. | A revert or down migration does not restore deleted data; ticket 2 expects no data deletion. | Not run; no migration expected. |
| Application | API image/config | Redeploy the previous API version; remove `ConnectionStrings:Cache` from that version's configuration if it did not require it. | Local Compose/host configuration. | Catalog cache is optional; availability and discovery remain SQL-backed. | Startup/readiness check after rollback; Not run. |
| Cache | Redis keys/config | Stop/remove the Redis service and its keys; keys are ephemeral and contain no customer/vehicle data. If keeping Redis, delete the ticket 2 key pattern or bump the key version. | Local Redis admin (`redis-cli DEL`/namespace version). | No persistent business data is lost; stale keys expire within TTL. | Cache lifecycle checks; Not run. |
| Configuration | `.env`/Compose | Restore previous runtime SQL configuration; ensure the API does not require Redis health. | Local operator. | No SQL schema/data change. | Live/ready checks; Not run. |

## Handoffs to other parts

| Source part | Finding / decision and evidence | Target part | Required action | Dependency / status |
| --- | --- | --- | --- | --- |
| Overview | Existing schema/indexes appear sufficient; no migration expected. | Part 2 | Generated SQL captured 2026-11-01; confirm execution-plan/index use and flag any proven gap during implementation. | Planning; partially verified. |
| Part 1 | Stable error codes/trace IDs, explicit-offset parsing, UTC `Z` DTOs. | Parts 2-4 | Use the same contracts in availability/cache tests and docs. | Planning. |
| Part 2 | DST wall-clock policy, same-local-date requirement, single-row coverage, SQL overlap predicates. | Parts 1/4 | Document policy in API/architecture and test it deterministically. | Planning. |
| Part 3 | Cache key/payload/TTL, 250/500 ms budgets, Redis `FailFast`, health degradation. | Parts 1/4 | Align DTO mapping, health/error behavior, and outage tests. | Planning. |
| Part 4 | Test matrix/fixtures/commands. | Implementation | Write and execute tests; record evidence in `docs/test/2-test.md`. | Planned; not executed. |
| Part 2 | No appointment write path or new grants. | Ticket 3 | Preserve runtime read-only boundary; ticket 3 owns procedure/write path. | Deferred. |
| Part 3 | Cache inventory/freshness. | Deploy notes/docs | Record invalidation and deployment steps. | Planned. |

## Phase 7: Scope verification

### 7.1 Change-to-requirement trace

| Change / affected component | Requirement ID / source | Direct requirement or necessary dependency | Research evidence / proposed verification |
| --- | --- | --- | --- |
| Discovery controller/DTO and ordered SQL read | S2-01, AC2-01 | Required | Part 1; API/SQL tests for fields/order/errors. |
| Catalog controller, cache-aside, empty/unknown behavior | S2-01, S2-05, AC2-01, AC2-04 | Required | Parts 1 and 3; T-11/T-12. |
| Explicit-offset parser, strict future check, `TimeProvider`, UTC `Z` DTOs | S2-02, S2-04, AC2-02, AC2-03 | Required | Part 1; T-10; API contract tests. |
| SQL duration and end computation with overflow validation | S2-02, AC2-03 | Required | Part 2; duration/overflow tests. |
| Opening-hours/shift coverage, active/qualified/seeded resource checks | S2-02, S2-03, AC2-02 | Required | Part 2; T-02/T-03. |
| Independent technician/bay overlap predicates and stable order | S2-03, AC2-02, AC2-03 | Required | Part 2; T-04 and independent-conflict tests. |
| Advisory response/no writes/no cache for availability | S2-04, AC2-03 | Required | Parts 1 and 2; before/after state tests. |
| Problem Details `code`/`traceId`, 400/404/503/500 behavior | S2-04, AC2-06 | Required | Part 1; error-path tests. |
| Redis cache contract, TTL, invalidation, bounded fallback, logging | S2-05, AC2-04, AC2-05 | Required | Part 3; T-12/T-13. |
| Cache health degradation, startup/readiness behavior | S2-05, AC2-05, AC2-06 | Required | Part 3; health/outage tests; Compose check. |
| Compose/Redis/config/test fixtures/packages | S2-05, S2-06 | Necessary dependency | Parts 3 and 4; Commands/tests. |
| OpenAPI documentation and Swagger/manual API instructions | S2-06, AC2-01 | Required | Parts 1 and 4; document assertions and automated API tests. |
| README/architecture/setup/database/cache docs, deploy notes, test report | S2-06, AC2-07 | Required | Part 4; documentation review and executed evidence. |
| No schema migration | S2-03 | Necessary dependency (reuse existing schema) | Part 2; generated SQL/index review; no migration unless proven gap. |
| No booking procedure/write path/telemetry stack | Excluded sources | Not ticket 2 requirements | Out-of-scope list; tickets 3/4. |

### 7.2 Scope checklist

- [x] Every S2-01-S2-06 requirement, AC2-01-AC2-07 acceptance criterion, and T-02-T-13 verification case has a research owner or an explicit gap.
- [x] Each proposed change traces to a ticket requirement or a necessary dependency.
- [x] Assumptions (AS-01-AS-10 where relevant), exclusions, and deferred extensions remain explicit.
- [x] Optional improvements (e.g., OpenAPI transformers, cache-health detail) are separated from required behavior.
- [x] Existing behavior, proposed additions, and executed verification are distinguished; no implementation or passing evidence is claimed.
- [x] Database, permissions, cache, configuration, telemetry, and test impacts are assessed; no migration is expected but generated SQL remains to verify.
- [x] Proposed paths and commands are labelled; no actual implementation paths are invented.
- [ ] Research verification has not run; this checklist does not assert verification or acceptance.

### 7.3 Items flagged for removal

| Item | Why it falls outside scope | Follow-up, if any |
| --- | --- | --- |
| Booking `POST`, appointment retrieval, booking procedure, concurrency/`409` handling | Ticket 2 verification boundaries; ticket 3 owns confirmation/retrieval and the protected write path. | Ticket 3. |
| Serilog/OpenTelemetry/Prometheus/Tempo/Grafana | Ticket 4 owns telemetry infrastructure; ticket 2 uses `ILogger` for degradation. | Ticket 4. |
| Authentication/authorization, admin CRUD, cancellation/rescheduling, idempotency, vehicle overlap | README/architecture exclusions; separate scoped requirements. | Future tickets. |
| Cache stampede protection, refresh-ahead, distributed locks | Not required by P-02; TTL + bounded fallback suffice for a read-only synthetic catalog. | Future optimization if measured. |
| Holidays/overnight opening windows, local-time booking input | AS-06 and ticket rules exclude them. | Future scoped requirement. |
| A custom frontend/browser automation | Backend-only local release; Swagger UI is generated. | Not applicable. |

## Dependencies

| Dependency | Requirement / component blocked | Resolution needed | Status / evidence |
| --- | --- | --- | --- |
| `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13` | Redis cache/health implementation | Add to central package management and API project; restore/build. | Verified 2026-11-01: temporary probe restored and compiled; StackExchange.Redis `2.7.27` resolved; solution integration deferred. |
| `Testcontainers.Redis` `4.14.0` | Real Redis integration/stall tests | Add to integration test project; confirm the pinned image and `PauseAsync`/`UnpauseAsync` API. | Verified 2026-11-01: package restored/compiled; `PauseAsync`/`UnpauseAsync` executed against the pinned image. |
| Redis image `7.4.12-alpine@sha256:8da19c...` | Compose/Testcontainers Redis | Pull/run `redis-cli ping`; record actual digest. | Verified 2026-11-01: pulled by digest; `linux/amd64`; `PONG`; server v7.4.12. |
| Existing SQL Server container/image | Availability/conflict tests | Reuse the ticket 1 pinned SQL image and fixture. | Existing; ticket 1 tests passed; the SQL suite was not rerun during research verification. |
| Existing EF Core model/indexes | Availability queries | Confirm generated SQL/index use; migration only if proven necessary. | Generated SQL captured 2026-11-01 with the required predicates/`NOT EXISTS`; execution-plan/index confirmation and any migration decision remain implementation-time. |
| Docker/Compose | Redis outage/startup manual checks | Redis service and API start without Redis health gate. | Docker `25.0.3`/Compose `v2.24.6` verified; Compose Redis service and outage checks deferred to implementation. |
| OpenAPI transformer capability | AC2-01 documentation | Confirm built-in OpenAPI transformer API in .NET 10 and document operations. | Verified 2026-11-01: `IOpenApiOperationTransformer`/`IOpenApiSchemaTransformer` and type-based/delegate transformer types exist in `Microsoft.AspNetCore.OpenApi` 10.0.13; operation documentation deferred. |
| Testcontainers pause API | Redis stall test | Confirmed `PauseAsync`/`UnpauseAsync` in Testcontainers 4.14.0 source. | Verified 2026-11-01: methods compiled and executed; container paused and recovered. |

## Decisions made

| Decision | Reason / alternatives | Requirement impact | Date / evidence |
| --- | --- | --- | --- |
| Use multipart research for ticket 2. | Four concern areas (API contract/time, availability/SQL, cache/resilience, verification/docs) with separate evidence needs. | Keeps one primary entry point and traceability. | 2026-11-01; research guide. |
| Use existing EF Core/SQL schema and indexes; no migration expected. | Ticket 2 is read-only; existing indexes cover dealership/resource/time/overlap predicates; generated SQL now confirms the predicates. | S2-03, AC2-02; execution-plan/index confirmation remains implementation-time. | 2026-11-01; inspected configurations/schema and captured generated SQL. |
| Use wall-clock local opening comparison with same-local-date requirement. | Avoids constructing ambiguous/nonexistent local boundaries; shifts remain UTC-authoritative. | S2-02, T-10. | 2026-11-01; [[research/2/02-availability-and-scheduling\|Part 2]]. |
| Use 250 ms per cache operation / 500 ms configured cumulative read+populate wait; Redis timeouts 500 ms and `FailFast`. | Bounds caller wait and underlying backlog. Probe observed warm waits of 254-265 ms, a cold read+populate of 508 ms, and immediate recovery; treat 500 ms as the configured budget rather than a strict wall-clock SLA. | S2-05, AC2-05. | 2026-11-01; Part 3 and research-phase probe. |
| Expose Redis degradation as a `ready`-tagged `Degraded` health check with JSON readiness output. | Keeps readiness 200 when SQL is healthy/Redis down and 503 when SQL is down; observable without secrets. | S2-05, AC2-05. | 2026-11-01; Part 3. |
| Use `code` and `traceId` Problem Details extensions with four stable codes. | Consistent validation/not-found/dependency/server contracts, including framework validation responses. | S2-04. | 2026-11-01; Part 1. |
| Cache only existing-dealership catalogs, including empty arrays; never 404 or dependency failures. | Preserves contract and avoids caching errors. | S2-01, S2-05. | 2026-11-01; Part 3. |
| Retire the standalone demo script; use automated API integration tests and Swagger/manual instructions. | The 2026-11-01 user-approved scope amendment removed the script deliverable; automated synthetic-data tests and Swagger UI provide the executable contract coverage. | S2-06, AC2-01. | 2026-11-01 scope amendment; ticket S2-06/AC2-01/AC2-07 and Parts 1/4. |

## Out of scope

| Item | Ticket exclusion or reason | Follow-up, if any |
| --- | --- | --- |
| Booking confirmation (`POST`), appointment retrieval, booking procedure, `409` capacity handling, concurrency | Ticket 2 owns the A-02 read path; ticket 3 owns writes/retrieval. | Ticket 3. |
| Serilog, OpenTelemetry, Prometheus, Tempo, Grafana | Ticket 4; only `ILogger` degradation now. | Ticket 4. |
| Authentication/authorization, admin CRUD, cancellation/rescheduling, idempotency, vehicle overlap | README/architecture exclusions. | Separate scoped tickets. |
| Cache stampede protection, refresh-ahead, distributed locks | Not required by P-02. | Future if measured. |
| Holidays, overnight opening intervals, local-time booking API | AS-06/ticket rules. | Future scoped requirement. |
| Production hosting, backups, high availability | Local demo scope. | Separate design. |
| Custom frontend/browser automation | Backend-only release; Swagger UI generated. | Not applicable. |

## Files referenced

Actual inspected files and symbols:

- `docs/tickets/2 - Service Discovery and Availability.md` (scope, requirements, acceptance, verification, exclusions).
- `README.md` (project scope, status, commands, implemented/planned).
- `docs/architecture.md` (domain, assumptions, API boundary, data flow, catalog/availability, observability, deployment).
- `docs/setup-guide.md` (configuration, startup sequence, commands, troubleshooting).
- `docs/database/overview.md`, `docs/database/schema.md`, `docs/database/migration.md`, `docs/database/cache.md`, `docs/database/maintenance.md`.
- `docs/test/README.md`, `docs/test/backend-test-template.md`, `docs/test/integration-test-template.md`; `docs/test/1-test.md` is referenced by the ticket as the ticket 1 baseline but was not reread or rerun.
- `docs/research/README.md`, `docs/templates/research-template.md`, `docs/plans/README.md`, `docs/deploy-notes/README.md`, `docs/deploy-notes/1-deploy-notes.md`, `docs/README.md`.
- `src/ServiceScheduler.Api/Program.cs`, `appsettings.json`, `appsettings.Development.json`, `ServiceScheduler.Api.csproj`, `Dockerfile`.
- `src/ServiceScheduler.Api/Infrastructure/Configuration/DatabaseOptions.cs`, `DatabaseOptionsValidator.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Persistence/SchedulerDbContext.cs`, `Entities/*.cs`, `Configurations/*.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Setup/DatabaseSetupCommand.cs`, `DatabaseSetupRunner.cs`, `FutureShiftSeeder.cs`, `RuntimePrincipalProvisioner.cs`, `SchedulerSetupOptions.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Persistence/Configurations/SeedData.cs` (cited as `SeedData.cs` in the parts).
- `src/ServiceScheduler.Api/Migrations/20261031092016_InitialCreate.cs`; the model snapshot is recorded in the schema/migration docs but was not separately reread.
- `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs`, `SqlServerContainerFixture.cs`, `SqlServerTestBase.cs`, `SqlServerCollection.cs`.
- `tests/ServiceScheduler.IntegrationTests/Startup/ProblemDetailsTests.cs`, `Startup/StartupValidationTests.cs`, `OpenApi/OpenApiTests.cs`, `Health/HealthEndpointTests.cs`, `Health/InitializedReadinessTests.cs`, `Persistence/MigrationAndSeedTests.cs`, `Persistence/ConstraintTests.cs`, `Security/RuntimePermissionTests.cs`.
- `compose.yaml`, `.env.example`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `ServiceScheduler.slnx`, `.gitignore`, `.github/workflows/` (empty directory; no workflow files).

Proposed files/directories at the evidence baseline (not existing then): `src/ServiceScheduler.Api/Features/Dealerships/`, `Features/ServiceCatalog/`, `Features/Availability/`, `Infrastructure/Caching/`, `Infrastructure/Errors/`, ticket 2 unit/integration test files, `docs/test/2-test.md`, `docs/test/2-manual-test.md`, `docs/deploy-notes/2-deploy-notes.md`, and the ticket 2 plan under `docs/plans/2/`. `scripts/ticket2-demo.ps1` was retired by the 2026-11-01 scope amendment and is not a deliverable. The implementation subsequently added the application and test files; see [[test/2-test|the test report]].

Research-phase probes (not repository files): temporary .NET 10 projects under the user's temp directory referencing the API assembly or the selected packages; they used EF Core 10.0.13, `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.13, `Testcontainers.Redis` 4.14.0, and the pinned Redis image to capture generated SQL and cache timing. They were deleted or left outside the checkout and are not ticket tests.

## Verification

- Command: `/orchestrator-ticket verify research 2` (re-verification after the 2026-11-01 ticket scope amendment).
- Status: **Passed** for planning.
- Verified on: 2026-11-01.
- Checkout: HEAD `3f8ab1e2cac26e54c5364f683d5df70cb1cfe5ff`; the working tree contains the ticket 2 implementation, research, plan, review, test, and deployment records, including the 2026-11-01 amendment that retired the standalone demo script (ticket S2-06, AC2-01, and AC2-07).
- Previous verification: the pre-amendment research verification recorded on 2026-11-01 was invalidated by that ticket amendment; this section supersedes it. The pre-implementation audit above is retained as historical evidence and is not implementation evidence.
- Input fingerprints (SHA-256 of LF-normalized content; CRLF checkout line endings are not part of the fingerprint): the overview fingerprint is for the research content before this Verification section; this section is the verification record, not a research input.
- Input table:

| Input | Fingerprint |
| --- | --- |
| `docs/tickets/2 - Service Discovery and Availability.md` | `b0bed502b6a8b7e59fe4904b5924e80592d4de0248dd2c0f0476abb80feb64a5` |
| `docs/research/2/00-overview.md` (content before this section) | `39f51052e5ea6e4ca95853c4ebb0c1da063a7ac189dd7bfc8cb4d2bf7e7d09f8` |
| `docs/research/2/01-api-contracts-and-validation.md` | `4bfc6d0fc1eb18d504203c553561c9e9c5184dcfb386ccf08e8a3fbe935e81db` |
| `docs/research/2/02-availability-and-scheduling.md` | `f41d2819664a76334fc271caea46a471d0c3b8b2a265bf56464ecca884a9fa07` |
| `docs/research/2/03-catalog-cache-and-resilience.md` | `8dbc44891d6321130cc2da4d38b7c5bcb3e8b645f52274088287ae0ecc989f0d` |
| `docs/research/2/04-verification-and-documentation.md` | `abcede91982e0be22404704d1efb7e85ea2729ac2da1100fafbfa5377d92ee2c` |

- Forward trace: every S2-01-S2-06 requirement, AC2-01-AC2-07 acceptance criterion, T-02-T-13 verification case, and ticket project requirement (A-02, D-01-D-04, P-02, P-04) has a research owner in Phase 0, the part documents, and Phase 7.1. The amended S2-06, AC2-01, and AC2-07 wording is reflected in Phase 0.1/0.2 and Parts 1 and 4; no ticket requirement is unowned.
- Reverse trace: every proposed change in Phase 6 and Phase 7.1 traces to a ticket requirement or a necessary dependency; the retired standalone demo script is no longer proposed as a deliverable. Exclusions preserve the ticket boundaries; no booking write, procedure, telemetry stack, authentication, cache stampede, or schema scope is proposed.
- Requirement traceability (the test IDs are the required checks; executed results belong to [[test/2-test|the test report]] and are not re-verified here):

| Requirements | Research coverage | Verification mapping |
| --- | --- | --- |
| S2-01, AC2-01 | Overview; Part 1 routes/DTOs; Part 3 cache contract; Part 1 Swagger/automated API coverage | TC2-01-TC2-08, TC2-33 (TC2-35 retired) |
| S2-02, AC2-02, AC2-03, T-10 | Parts 1 and 2 | TC2-12-TC2-14, TC2-22, TC2-23 |
| S2-03, AC2-02, T-02-T-04, T-11 | Part 2; overview index audit | TC2-15-TC2-21, TC2-24, TC2-25 |
| S2-04, AC2-06 | Part 1 Problem Details; Part 2 failure behavior | TC2-26-TC2-29, TC2-34 |
| S2-05, AC2-04, AC2-05, T-12, T-13 | Part 3 | TC2-02, TC2-05-TC2-11, TC2-30-TC2-32 |
| S2-06, AC2-07 | Part 1 OpenAPI/Swagger/automated API; Part 4 documentation and evidence | TC2-33 and documentation review (TC2-35 retired) |

- Executed evidence from the original research phase (temporary projects outside the repository; no ticket test file was run; not re-executed for this re-verification):

| Check | Actual command / probe | Observed result |
| --- | --- | --- |
| Toolchain | `dotnet --version`; `dotnet --list-sdks`; `docker version`; `docker compose version` | SDK `10.0.403` and `8.0.100`; Docker `25.0.3`; Compose `v2.24.6-desktop.1`. |
| Baseline restore/build | `dotnet restore && dotnet build --no-restore` | Restore up to date; build succeeded with 0 warnings and 0 errors. |
| Package graph | Temporary project + `dotnet list package --include-transitive` | `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13`, `Testcontainers.Redis` `4.14.0`, `StackExchange.Redis` `2.7.27`. |
| Redis image | `docker pull redis:7.4.12-alpine@sha256:8da19c...`; `docker image inspect`; `redis-cli ping`; `redis-server --version` | Digest matched; `linux/amd64`; `PONG`; server v7.4.12. |
| DST/time zone | Temporary .NET 10 probe with `TimeZoneInfo` | `2027-03-28T01:30Z` -> `02:30+01:00`; `03:00Z` -> `04:00+01:00`; `2027-10-31T00:30Z` -> `01:30+01:00`; `02:00Z` -> `02:00+00:00`; `03:00Z` -> `03:00+00:00`. |
| UTC `Z` | Temporary .NET 10 probe with `System.Text.Json` | `{"startsAt":"2026-11-03T03:00:00Z"}` for `DateTime` with `Kind=Utc` and `DateTimeOffset.UtcDateTime`. |
| Generated SQL | Temporary EF Core 10.0.13 probe calling `ToQueryString()` | Dealership/day/opening predicates, `TOP(1)`, stable name/ID ordering, independent `NOT EXISTS`, `Status = N'Confirmed'`, and half-open start/end comparisons present in all four query shapes. |
| OpenAPI capability | Temporary .NET 10 probe reflecting `Microsoft.AspNetCore.OpenApi` 10.0.13 | `IOpenApiOperationTransformer`, `IOpenApiSchemaTransformer`, and type-based/delegate transformer types present. |
| Cache budget | Temporary `RedisCache` probe with the pinned package/image | Registration resolved in 17-20 ms without connecting; warm waits 254-265 ms; cold read+populate 508 ms; paused Redis 257-265 ms; recovery read 1 ms; no unbounded backlog observed. |
| Health mapping | Temporary ASP.NET Core probe | A `Degraded` health check returned HTTP `200` under default `MapHealthChecks`. |

- Corrections applied during this re-verification: replaced the superseded S2-06/AC2-01/AC2-07 requirement text in Phase 0.1/0.2 with the amended ticket wording (interactive Swagger documentation and automated API integration tests; no standalone demo script); marked the `scripts/ticket2-demo.ps1` proposal and its Phase 3b row, Phase 4 audit row, gap G-14, change-plan step 6, Phase 7.1 row, and decisions row as retired; changed TC2-35 to Retired in Part 4 and removed its command and manual-check prerequisites; updated Parts 1 and 4 so AC2-01 is covered by Swagger UI plus automated API integration tests; added P-04 to the overview requirement list; added implementation-status notes to Parts 1 and 4 so the pre-implementation audit is not read as current behavior; rechecked the affected forward/reverse traces and cross-references. The corrections recorded by the pre-amendment verification remain in the research body.
- Unresolved gaps (non-blocking for research verification): the ticket's final paragraph still says executable examples must discover seeded IDs and generate a future weekday start; the automated API integration tests satisfy that behavior, but the wording is ambiguous alongside the explicit retirement of the standalone script, so a future ticket edit should clarify it. The research remains a pre-implementation design record; executed implementation evidence, Compose operations, and manual checks belong to [[test/2-test|the test report]] and the not-yet-generated `docs/test/2-manual-test.md`, and are not re-verified here. Plan verification is stale after this research change and must be refreshed; execution-plan/index confirmation and the ticket 1 SQL suite were not rerun.
- Result: **Passed** for planning. The research now covers the amended ticket, and the previous verification's stale fingerprints are superseded. This record does not establish implementation, executed tests, Compose behavior, Swagger output, or acceptance; AC2-01-AC2-07 and T-02-T-13 must not be marked Passed from this document.
