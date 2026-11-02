---
title: "3 - Appointment Booking and Retrieval: Research Overview"
tags: [research, service-scheduler, booking, concurrency, api]
ticket: 3
status: verified
updated: 2026-11-01
---

# 3 - Appointment Booking and Retrieval: Research Overview

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Goal and scope: add validated appointment creation and dealership-scoped retrieval, with authoritative full-interval eligibility, atomic technician/bay allocation through a restricted SQL Server procedure, bounded candidate and deadlock handling, and verified failure/cancellation behavior.
- Requirement IDs: S3-01 through S3-06; project requirements A-01, A-02, A-03, P-01, D-01, D-02, D-03, D-04, and P-04; verification cases T-01 through T-16 and S3-V01 through S3-V06.
- Evidence baseline: checkout HEAD `90a62840d04756e1ec3c28bdc64ba53365be282f` plus the staged 2026-11-01 ticket 3 update; inspected API, persistence, migration, setup/permission, availability, cache, error, OpenAPI, test, Compose, and documentation files listed in [[#Files referenced]]. Research-phase SQL Server probes ran on 2026-11-01 against the pinned SQL Server 2022 CU23 image; they are behavior probes, not ticket tests.
- Environment observed: .NET SDK `10.0.403`; Docker server `25.0.3`; the repository is on branch `main` with the ticket 3 update staged and no other working-tree changes.
- Related plan: not created; this research precedes planning.

> [!IMPORTANT]
> This document records requirements, inspected evidence, proposed contracts, resolved decisions, and planned verification. The booking endpoints, booking procedure, time-zone mapping, grants, tests do not exist yet. Every ticket-level test is **Not run**; research-phase probes executed in an isolated SQL Server container do not establish ticket acceptance.

## Decomposition decision

Use multipart research under `docs/research/3/` because ticket 3 combines an HTTP/DTO contract, an authoritative SQL procedure with locking and time-zone rules, a runtime-permission and migration change, and a real-SQL concurrency/failure matrix. The API mapping, procedure contract, provisioning/migration, and verification concerns need separate evidence.

| Part | File | Responsibility |
| --- | --- | --- |
| Overview | [[research/3/00-overview\|00-overview]] | Scope lock, part index, current architecture, consolidated coverage/audit, gap decisions, dependency-ordered change plan, recovery, handoffs, traceability, decisions, and out-of-scope items. |
| Part 1 | [[research/3/01-api-contracts-and-retrieval\|01-api-contracts-and-retrieval]] | POST/GET routes, exact DTOs, Location, explicit-offset and future-start validation, ownership/scope validation, Problem Details including `409`, OpenAPI, retrieval, and contract verification. |
| Part 2 | [[research/3/02-booking-procedure-and-concurrency\|02-booking-procedure-and-concurrency]] | Procedure signature/outputs/error numbers, IANA-to-SQL time-zone mapping, authoritative eligibility/coverage/conflict checks, technician-then-bay locking, candidate ordering, retries/timeouts/budget, clock ownership, and concurrency verification. |
| Part 3 | [[research/3/03-permissions-migrations-and-operations\|03-permissions-migrations-and-operations]] | Runtime `EXECUTE` grant and explicit write denials, provisioner allowlist, migration/model/seed changes, setup order, repeated setup, ticket 2 upgrade, recovery, configuration, cache impact, and operational documentation. |
| Part 4 | [[research/3/04-verification-and-documentation\|04-verification-and-documentation]] | Unit/API/SQL/Redis/operational test architecture, T-01-T-16 and S3-V01-S3-V06 scenario matrix, fixtures, commands, evidence rules, and documentation/deployment records. |

The overview owns shared scope, consolidated decisions, and Phase 7 traceability. Later phases must use this overview as the primary research entry point.

## Phase 0: Scope lock

### 0.1 Ticket requirements

| ID | Requirement text (condensed) | Acceptance evidence | Project requirements |
| --- | --- | --- | --- |
| S3-01 | Validate and create an appointment from customer, vehicle, service type, and explicit-offset start; persist and return the complete appointment with a retrieval `Location`. | A valid POST returns `201`, complete retrievable associations, and a followable Location; invalid input is rejected without writes. | A-01, A-03, D-02 |
| S3-02 | Authoritatively validate full-interval eligibility, dealership/ownership scope, positive SQL duration, duration snapshot, and independent technician/bay conflicts. | Qualification, active-state, opening/shift coverage, ownership, scope, boundary, and conflict tests pass against real SQL Server. | A-02, A-03 |
| S3-03 | Allocate one technician and one bay atomically inside one short `SERIALIZABLE` procedure transaction with technician-then-bay locks and a restricted runtime write boundary. | Concurrent requests produce one confirmed appointment; failed attempts leave no partial row; direct runtime writes are denied and procedure execution succeeds. | A-03, P-01 |
| S3-04 | Bound candidate attempts and deadlock retries; classify conflicts, dependency failures, cancellation, and unknown commit outcomes safely. | Conflicting candidates are retried within a documented limit; deadlock/lock/command limits and cancellation are verified; unknown commit is not replayed. | P-01, D-02 |
| S3-05 | Retrieve persisted appointments dealership-scoped, independent of Redis, using stored end/duration. | GET returns the complete persisted appointment; missing or other-dealership appointments return `404`; restart preserves retrieval. | A-03 |
| S3-06 | Provide automated verification, OpenAPI, and updated operational/database records. | Automated and operational checks execute; checks use seeded IDs and generated future times; docs record actual evidence. | D-01, D-02, D-03, D-04, P-04 |

### 0.2 Acceptance criteria

The ticket lists its acceptance criteria as unlabeled bullets. `AC3-01` through `AC3-13` are research-local traceability labels applied in the same order as those 13 ticket bullets; they are not new ticket requirements and do not change the criteria text. The S3 IDs, T-series, and S3-V IDs remain the ticket-defined requirement/verification identifiers.

| Research label | Acceptance criterion | Research owner |
| --- | --- | --- |
| AC3-01 | Valid booking returns `201` and complete retrievable associations; confirmation survives an API restart. | Overview; Parts 1, 2, and 4 |
| AC3-02 | Invalid ownership, dealership scope, qualification, time, shift/opening coverage, and resource availability are rejected without unintended writes. | Parts 1, 2, and 4 |
| AC3-03 | Adjacent appointments succeed; partial overlaps, containment, and identical intervals conflict for either resource independently. | Part 2 and Part 4 |
| AC3-04 | With one eligible pair and 20 distinct coordinated concurrent POSTs, exactly one returns `201`, nineteen return `409`, and one appointment is stored. | Parts 2 and 4 |
| AC3-05 | Independent runtime connections calling the procedure without API prechecks reject overlapping technician-only and bay-only bookings; valid execution succeeds and direct writes are denied. | Parts 2, 3, and 4 |
| AC3-06 | Tests cover an initially empty appointment table, lock waiting followed by a fresh overlap check, atomic rollback/released locks, and bounded deadlock/timeout behavior, exercising competing API instances where practical. | Parts 2 and 4 |
| AC3-07 | Booking still works during Redis outage; SQL Server outage prevents confirmation and fails readiness. | Parts 3 and 4 |
| AC3-08 | Booking/retrieval and concurrency checks use real seeded UUIDs and generated future times within seeded shifts. | Part 4 |
| AC3-09 | Local end-to-end startup and demo commands work from a clean checkout; README, design, and database/procedure documentation include actual verification evidence. | Parts 3 and 4 |
| AC3-10 | POST/GET DTOs, Location, UTC formatting, all Problem Details outcomes, and strict input handling match the documented contract. | Part 1 and Part 4 |
| AC3-11 | A changed catalog duration does not alter an existing appointment; a stale Redis catalog cannot influence a new booking's authoritative duration; subsequent availability sees committed reservations. | Parts 1 and 4 |
| AC3-12 | Alternative-pair allocation, request cancellation, and unknown-commit handling follow the documented bounds without unsafe write replay. | Parts 2 and 4 |
| AC3-13 | Fresh setup, repeated setup, and upgrade from the ticket 2 database apply the procedure and intended runtime permissions successfully; recovery is documented and verified against isolated SQL Server data. | Part 3 and Part 4 |

### 0.3 Verification cases

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-01 Valid future booking | API + SQL Server | `201`, correct duration, qualified technician and bay, retrievable persisted associations. | A-01, A-02, A-03 |
| T-02 Qualification or inactive resource | Unit + integration | Unqualified/inactive candidates never satisfy availability or booking. | A-02 |
| T-03 Partial shift/opening coverage | Unit + integration | Resources free at the start but unavailable later cannot be booked. | A-02 |
| T-04 Interval boundaries | Unit + SQL Server | Adjacent reservations succeed; partial overlap, containment, and identical intervals conflict. | A-02, P-01 |
| T-05 Technician conflict only | SQL Server | Same technician with a different bay still conflicts. | P-01 |
| T-06 Bay conflict only | SQL Server | Same bay with a different technician still conflicts. | P-01 |
| T-07 Concurrent competition | API + SQL Server | With one eligible pair, 20 distinct concurrent POSTs produce one `201`, nineteen `409`s, and one stored appointment. | P-01 |
| T-08 Database write boundary | SQL Server | Runtime procedure calls without API checks reject technician-only and bay-only overlaps; direct DML is denied; valid procedure execution succeeds. | P-01 |
| T-09 Atomic failure | SQL Server | Failed allocation leaves no partial or confirmed booking. | A-03, P-01 |
| T-10 Time handling | Unit + API | Reject past/offset-free starts; accept equivalent explicit-offset instants; evaluate opening boundaries in dealership local time, including DST. | A-01, A-02 |
| T-11 Relationship and scope | API + SQL Server | Reject mismatched customer/vehicle and cross-dealership service/resource references; retrieval through another dealership returns `404`. | A-01, A-03 |
| T-13 Booking during Redis outage | API + SQL Server + Redis | Booking and retrieval remain correct while Redis is unavailable; final allocation uses SQL. | P-01 |
| T-14 Restart and database outage | Integration/smoke | Booking survives API restart; database outage prevents confirmation and fails readiness. | A-03, P-04 |
| T-16 Booking clean checkout | Local smoke | Documented initialization, migration, seed, startup, booking/retrieval, and concurrency checks work from a clean checkout using synthetic data and generated future times. | D-02, D-03, D-04, P-04 |
| S3-V01 API contract | API + OpenAPI | Exact success fields, followable Location, scoped retrieval, UTC timestamps, malformed/missing input, and documented Problem Details codes/trace IDs. | S3-01, S3-04, S3-05 |
| S3-V02 Duration and read compatibility | API + SQL Server + Redis | A stale catalog cannot change booking duration; later service changes preserve stored snapshots; uncached availability observes the committed reservation. | S3-02, S3-05 |
| S3-V03 Alternative candidates | API + SQL Server | A controlled conflict on the first pair permits another free eligible pair within the configured limit; exhaustion follows documented bounds. | S3-03, S3-04 |
| S3-V04 Failure boundaries | API + SQL Server | Deadlock/lock/command timeout limits and cancellation release resources; unknown commit is not blindly replayed; unexpected SQL errors are not reported as capacity conflicts. | S3-03, S3-04 |
| S3-V05 Direct-call validation parity | SQL Server | Runtime procedure calls without API checks enforce ownership, scope, duration, eligibility, opening/shift coverage, future-start policy, and both conflicts, including offset/DST fixtures. | S3-02, S3-03 |
| S3-V06 Setup and recovery | SQL Server + local smoke | Fresh/repeated setup and ticket 2 upgrade retain data, apply exact permitted grants/denials, reject excessive privileges, and satisfy documented recovery checks. | S3-03, S3-06 |

### 0.4 Exclusions and necessary dependencies

| Item / ticket source | Classification | Reason and requirement link |
| --- | --- | --- |
| POST/GET appointment endpoints and DTOs | Required | S3-01, S3-05, AC3-01, AC3-10. |
| Same-owner SQL booking procedure with `SERIALIZABLE`, `XACT_ABORT`, technician-then-bay locks, full revalidation, and insert/commit | Required | S3-02, S3-03, AC3-03, AC3-05; BR-05-BR-08. |
| Runtime `EXECUTE` grant and explicit appointment DML denials | Required | S3-03, S3-06, AC3-05; P-01. |
| New migration for the procedure and any required time-zone mapping persistence | Required | S3-03, S3-06; SQL Server cannot use the persisted IANA ID directly with `AT TIME ZONE`. |
| Bounded candidate/deadlock handling, timeouts, cancellation, and unknown-commit safety | Required | S3-04, AC3-06, AC3-12. |
| OpenAPI, automated tests, README/design/database/setup/deploy/test records | Required | S3-06, AC3-08-AC3-11; D-01-D-04, P-04. |
| Existing ticket 2 discovery/catalog/availability reads and Problem Details pipeline | Necessary dependency | Reuse the endpoint conventions, explicit-offset parser, coverage rules, SQL classifier, and OpenAPI transformers; booking must not treat availability as a reservation. |
| Existing `scheduler_app` read identity and privileged setup identity | Necessary dependency | The API reads with `scheduler_app`; migrations/seeds use setup; ticket 3 adds only procedure execution and explicit denies. |
| Existing appointment indexes | Necessary dependency | `IX_Appointments_DealershipId_TechnicianId_StartUtc` and `IX_Appointments_DealershipId_ServiceBayId_StartUtc` include `EndUtc`/`Status`; research confirms they cover the procedure predicates. |
| Redis catalog cache | Not required by booking | Booking reads SQL duration and writes SQL; no cache read, invalidation, or new key. |
| Serilog/OpenTelemetry/Prometheus/Tempo/Grafana | Excluded | Ticket 4; ticket 3 uses existing `ILogger` only. |
| Authentication/authorization, customer access control, admin CRUD | Excluded | Local unauthenticated demo; ownership/dealership checks are data integrity (AS-09). |
| Appointment cancellation/rescheduling, idempotent POST, vehicle-overlap protection | Excluded | README and ticket exclusions; a repeated POST is a new attempt (AS-11). Client request-abort handling is in scope for S3-04, but appointment cancellation is not. |
| External dealership integrations, payments, notifications, background jobs | Excluded | Initial release scope. |
| Cache stampede protection, Redis locks, new telemetry infrastructure | Excluded | Not required; Redis is not a booking lock. |
| Separate frontend/UI | Excluded | Backend-only release; generated Swagger UI is the interactive client. |

### 0.5 Attachments

Ticket 3 references no images, PDFs, or other attachments. No asset inspection applies.

## Current architecture and entry points

> [!NOTE]
> Tickets 1 and 2 are implemented and recorded in [[test/1-test|the ticket 1 test report]] and [[test/2-test|the ticket 2 test report]]. Ticket 3 endpoint, procedure, mapping, permission, and test work is absent at this baseline. The ticket states that preparing ticket 3 does not rerun or establish those prior results.

| Component | Inspected evidence | Status |
| --- | --- | --- |
| Composition root | `src/ServiceScheduler.Api/Program.cs`: `AddProblemDetails()` line 29, `AddExceptionHandler<SqlDependencyExceptionHandler>()` line 65, built-in OpenAPI line 67, `AddDbContext`/`UseSqlServer` lines 87-90 with no retry strategy, `AddScoped<AvailabilityService>()` line 98, `TimeProvider.System` registration, health checks, and Swagger UI. | Existing; Must Change to register booking options/service/procedure boundary. |
| Controllers and features | `Features/Dealerships/`, `Features/ServiceCatalog/`, `Features/Availability/`; `[ApiController]` controllers, feature-local contract records, scoped services. No `Features/Appointments/`. | Read features existing; appointment endpoints missing — proposed addition. |
| Persistence model | `Infrastructure/Persistence/Entities/` and `Configurations/`; `Appointment` entity fields, `CK_Appointments_*` checks, composite consistency FKs, `IX_Appointments_DealershipId_TechnicianId_StartUtc` and `...ServiceBayId...` with `EndUtc`/`Status` included. | Existing; no appointment table/index change expected. |
| Initial migration | `Migrations/20261031092016_InitialCreate.cs` creates the ten tables, checks, FKs, indexes, and static seeds; no procedure, time-zone mapping, or runtime `EXECUTE`. | Existing; ticket 3 adds a new migration. |
| Setup and runtime identities | `Infrastructure/Setup/DatabaseSetupRunner.cs` runs `MigrateAsync`, then `RuntimePrincipalProvisioner`, then `FutureShiftSeeder`. `RuntimePrincipalProvisioner` grants only `CONNECT` and `SELECT ON SCHEMA::dbo`; `ValidateDatabasePermissionsAsync` lines 244-287 rejects every other explicit permission. | Existing; Must Change for procedure execution and write denies. |
| Availability rules and queries | `Features/Availability/AvailabilityService.cs`, `AvailabilityQueries.cs`, `StartsAtParser.cs`, `AvailabilityInterval.cs`, `OpeningCoverage.cs`, `ShiftCoverage.cs`, `HalfOpenOverlap.cs`; advisory reads, explicit-offset parsing, SQL duration, half-open conflicts, stable name/ID order. | Existing; reuse or extract shared rules where justified; availability remains advisory. |
| Error handling | `Infrastructure/Errors/ProblemCodes.cs` (400/404/503/500), `ProblemResponse.cs`, `SqlFailureClassifier.cs` (includes 1205/1222/-2), `SqlDependencyExceptionHandler.cs`. No `409` code. | Existing; Must Change to add `capacity_unavailable` and booking-specific error mapping. |
| OpenAPI | `Infrastructure/OpenApi/ApiOperationTransformer.cs`, `ProblemDetailsSchemaTransformer.cs`; ticket 2 operations documented; schema description lists four codes. | Existing; Must Change for POST/GET and `409`. |
| Catalog cache | `Features/ServiceCatalog/ServiceCatalogService.cs`, `Infrastructure/Caching/`; optional Redis cache-aside; availability never reads Redis. | Existing; booking will not use it; no cache change planned. |
| Tests and fixtures | `tests/ServiceScheduler.UnitTests/` (availability rules, cache, configuration, errors) and `tests/ServiceScheduler.IntegrationTests/` (WebApplicationFactory, real SQL Server/Redis Testcontainers, fixed clock `2026-11-02T00:00Z`, setup/runtime connections, permission/constraint/migration tests). | Existing; extend with booking tests and concurrency fixtures. |
| Containers and configuration | `compose.yaml` SQL Server 2022 CU23, Redis, one-shot `db-setup`, API; `.env.example`; `appsettings.json` cache defaults. | Existing; add booking options and setup/migration behavior; no new container. |
| Database and workflow records | `docs/database/{overview,schema,migration,cache,maintenance}.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/README.md`, `docs/test/README.md`, deploy-notes guides. | Existing; update during implementation with actual evidence. |

### Proposed request flow

```text
POST /api/dealerships/{dealershipId}/appointments
  -> controller binds raw startsAt and IDs, validates offset/UUID presence
  -> AppointmentService captures evaluatedAt from TimeProvider
     -> EF reads dealership/service/customer/vehicle, SQL duration, opening coverage,
        ordered technician candidates, and ordered bay candidates from SQL Server
     -> pure candidate planner enumerates bounded pairs in name/ID order
     -> for each pair: IAppointmentBookingProcedure.ExecuteAsync
          -> dbo.CreateAppointment
             -> SERIALIZABLE transaction, XACT_ABORT ON, LOCK_TIMEOUT
             -> lock technician row, then bay row (UPDLOCK, HOLDLOCK)
             -> revalidate ownership, scope, duration, active/qualified/shift/opening
             -> check technician and bay half-open conflicts
             -> insert one Confirmed appointment, commit
          -> 51001/51002 -> skip conflicted resource and try next pair
          -> 1205 -> bounded whole-attempt retry; 1222/-2/connection -> 503
          -> 51003 -> 400; 51004/51005 -> next pair then 409; 51006 -> 500
  -> 201 Created + Location + AppointmentResponse
```

```text
GET /api/dealerships/{dealershipId}/appointments/{appointmentId}
  -> EF AsNoTracking query with appointmentId AND dealershipId
  -> found: AppointmentResponse with stored start/end/duration/status/createdAt
  -> missing or other dealership: 404 not_found
```

## Phase 1: Consolidated layer coverage

| Component | Coverage and evidence | Research part |
| --- | --- | --- |
| API contract and validation | Missing — proposed POST/GET controllers, request/response DTOs, raw-offset parsing reuse, ownership/scope validation, Problem Details `409`, OpenAPI. | Part 1 |
| Scheduling rules | Existing pure helpers for duration/offset/opening/shift/overlap; booking orchestration and candidate policy missing. | Parts 1 and 2 |
| Resource selection and availability | Existing SQL candidate queries; booking must select bounded ordered pairs and revalidate in the procedure. | Part 2 |
| Domain model and EF mappings | Existing appointment entity/checks/FKs/indexes; new time-zone mapping persistence proposed. | Parts 2 and 3 |
| EF Core migrations and seeds | Initial migration existing; new mapping table/seed/FK and procedure migration proposed. | Part 3 |
| Booking procedure and transactions | Missing — proposed `dbo.CreateAppointment` with `SERIALIZABLE`, technician-then-bay locks, full revalidation, insert/commit. | Part 2 |
| Runtime permissions | Existing `CONNECT`/`SELECT`; proposed `EXECUTE` grant and explicit appointment DML denies, with provisioner allowlist changes. | Part 3 |
| Failure and retry handling | Existing narrow SQL classifier; proposed bounded candidate/deadlock/timeouts/budget and unknown-commit policy. | Part 2 |
| Redis catalog cache | Relevant only for compatibility; booking reads SQL duration and never Redis. No key/payload/TTL change. | Parts 2 and 3 |
| Configuration and packaging | Proposed booking options; existing Compose/package pins; no new container/package expected. | Part 3 |
| Telemetry | Existing `ILogger` only; Serilog/OpenTelemetry remains ticket 4. | Part 3 |
| Unit tests | Missing booking planner/allocator/options tests; existing availability rule tests reusable. | Part 4 |
| API and dependency integration tests | Missing booking API/procedure/concurrency/permission tests; existing SQL/Redis fixtures and fixed clock reusable. | Part 4 |
| Operational verification | Missing restart/outage checks, setup/upgrade/recovery evidence. | Part 4 |
| Documentation and delivery | Must Change — README, architecture, setup, database schema/migration/overview/cache, docs index, test report, deploy notes. | Part 3 and Part 4 |
| UI or new integrations | N/A — backend-only ticket; generated Swagger UI is supplementary. | Overview |

### 1.2 Workflow change checklist

| Check | Evidence to collect | Status |
| --- | --- | --- |
| All callers and entry points | New POST/GET routes, `AppointmentService`, procedure boundary, DI registrations, OpenAPI operations. | Proposed; none exist. |
| Rules enforced across boundaries | API prechecks versus procedure revalidation; same duration/opening/shift/conflict predicates; direct-call parity. | Proposed; Part 2 designs both sides. |
| Read and write consistency | Stored duration/end snapshots; GET after POST; stale catalog cannot affect booking; availability sees committed rows. | Proposed; Parts 1 and 2. |
| Failure paths | Conflict candidate retry, deadlock retry, lock/command timeout, cancellation, unknown commit, 400/404/409/503/500 mapping. | Proposed; Parts 1 and 2. |
| Verification coverage | Requirement-linked unit/API/SQL/concurrency/permission/setup/upgrade tests, final-state assertions, and unexecuted checks. | Proposed; Part 4. |

## Phase 2: Consolidated data-flow trace

### 2.1 READ paths

| Path | Flow | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Booking validation | HTTP -> controller parsing -> EF dealership/service/customer/vehicle reads -> SQL duration -> end/overflow/future check | Runtime identity; explicit offset; no Redis; 400/404 before writes | Missing; Part 1 |
| Opening/candidate selection | API -> EF opening rows + ordered technician/bay candidate queries | SQL authoritative; bounded ordered reads; availability is advisory | Existing query shapes; booking usage missing |
| Appointment retrieval | API -> EF dealership-scoped `AsNoTracking` query -> response DTO | Stored associations, end, duration, status, creation time; 404 for other dealership | Missing; Part 1 |
| Post-commit availability | Availability -> EF confirmed appointment overlap checks | Sees newly committed rows; no cache | Existing; verify in S3-V02 |

### 2.2 WRITE paths

| Path | Flow | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Booking confirmation | API -> bounded pair loop -> `IAppointmentBookingProcedure` -> `dbo.CreateAppointment` -> locked revalidation -> insert/commit -> response | Runtime `EXECUTE`; same-owner procedure; no direct DML | Missing; Parts 2 and 3 |
| Direct procedure call | Runtime connection -> `dbo.CreateAppointment` with explicit resources and evaluation instant | Must enforce all invariants without API prechecks | Missing; Part 2 |
| Migration/seed | Privileged setup -> mapping table/seed/procedure -> provisioner grants/denies -> shift seeder | Separate identity; upgrade preserves appointments | Missing; Part 3 |

### 2.3 Path coverage verification

| Affected path / requirement | Value source and rule enforcement | Failure / cleanup | Verification scenario | Observed evidence |
| --- | --- | --- | --- | --- |
| Booking creation S3-01/S3-02 | SQL duration, relationships, candidates, procedure revalidation | 400/404/409/503/500; no partial row | T-01-T-06, T-09, S3-V01/V02/V05 | Not run |
| Atomic allocation S3-03/S3-04 | `SERIALIZABLE` locks and committed insert | Rollback, bounded retries, cancellation | T-07-T-09, S3-V03/V04 | Not run |
| Retrieval S3-05 | Stored appointment row | 404 for missing/other dealership | T-01, T-11, T-14, S3-V01 | Not run |
| Setup/permissions S3-06 | Mapping/procedure/grant state | Setup fails on invalid state; upgrade preserves data | T-08, S3-V06 | Not run |
| Cache compatibility S3-02/S3-06 | SQL direct reads; no cache | Redis outage does not affect booking | T-13, S3-V02 | Not run |

## Phase 3: UX state matrix

N/A — ticket 3 is backend-only. Request/response and error states are covered in Part 1; generated Swagger UI is a supplementary client, not a custom UI.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict / evidence |
| --- | --- | --- | --- | --- |
| `Program.cs` | Composition root | Controllers, Problem Details, OpenAPI, options validation, DbContext, `TimeProvider.System`, availability service, health; no booking registration. | S3-01-S3-06 | Must Change. |
| `Infrastructure/Persistence/Entities/Appointment.cs` | Persisted booking shape | ID, dealership/customer/vehicle/service/technician/bay IDs, UTC start/end, duration snapshot, status, creation time. | S3-01-S3-03 | No Change; map response from it. |
| `AppointmentConfiguration.cs` | Constraints/indexes | Positive duration, end > start, end = DATEADD snapshot, status `Confirmed`, UTC offsets; composite consistency FKs; two resource/start indexes with end/status included. | S3-02-S3-03 | No Change expected; no new appointment index. |
| `20261031092016_InitialCreate.cs` | Schema baseline | Creates tables/checks/FKs/indexes/seeds; no procedure, mapping, or `EXECUTE`. | S3-03 | No Change; new migration proposed. |
| `SchedulerDbContext.cs` | Persistence boundary | Ten DbSets; direct EF reads; convention FK indexes removed. | S3-02-S3-05 | Must Change to add the time-zone mapping DbSet. |
| `RuntimePrincipalProvisioner.cs` | Runtime permissions | Grants `CONNECT` + schema `SELECT`; allowlist rejects other explicit permissions; no write grants. | S3-03-S3-06 | Must Change to allow procedure `EXECUTE` and exact appointment DML denies. |
| `DatabaseSetupRunner.cs` | Setup order | `MigrateAsync` -> provisioner -> shift seeder. | S3-06 | Must Change to invoke mapping/procedure validation and keep grant order. |
| `AvailabilityQueries.cs` | Candidate/coverage predicates | Dealership-scoped service lookup, opening coverage, ordered active/qualified technician candidates, ordered active bay candidates, half-open `NOT EXISTS` conflicts. | S3-02 | Need Review/reuse; booking needs bounded candidate reads and the same predicates. |
| `AvailabilityService.cs` / coverage helpers | .NET coverage policy | Explicit offset, future check, SQL duration, local opening coverage, UTC shift/conflict checks; no writes/cache. | S3-02 | No Change; reuse or extract only if the plan justifies it. |
| `StartsAtParser.cs` | Raw timestamp parsing | Requires trailing `Z` or explicit numeric offset; invariant parsing. | S3-01 | No Change; reuse for POST `startsAt`. |
| `ProblemCodes.cs` / `ProblemResponse.cs` | Error contract | Four codes; no `409`; helper adds `code`/`traceId`. | S3-01-S3-05 | Must Change for `capacity_unavailable`. |
| `SqlFailureClassifier.cs` / `SqlDependencyExceptionHandler.cs` | Dependency classification | Narrow SQL numbers including 1205/1222/-2; sanitized 503; unexpected 500. | S3-04 | No Change; booking-specific procedure errors handled before the global handler. |
| `ServiceCatalogService.cs` / `BoundedCache` | Redis cache | Catalog cache-aside only; availability never reads it. | S3-02/S3-06 | No Change; booking must not use it. |
| `OpenApiTests.cs`, `ProblemDetailsTests.cs`, `RuntimePermissionTests.cs`, `MigrationAndSeedTests.cs`, `ConstraintTests.cs` | Existing verification patterns | Ticket 2 operation/error tests; permission probes; migration/constraint probes. | S3-01-S3-06 | Must Change/extend with booking cases. |
| `SchedulerApiFactory.cs`, `SqlServerContainerFixture.cs`, `SqlServerTestBase.cs`, `SchedulingTestData.cs` | Test infrastructure | In-memory config, optional `TimeProvider`, real SQL container, one DB per class, setup probes, fixed clock. | S3-01-S3-06 | Need Review/extend for booking and competing factories. |
| `compose.yaml`, `.env.example`, `appsettings.json` | Configuration/packaging | SQL/Redis/setup/API; cache defaults; no booking options. | S3-06 | Must Change for booking options/docs. |
| `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/database/*.md`, `docs/README.md` | Delivery records | Booking planned; no procedure/mapping/grants/commands/evidence. | S3-06 | Must Change during implementation. |

### Gate 2: Call path and boundary verification

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| `POST /api/dealerships/{id}/appointments` | Missing controller -> no call chain | N/A | No | Proposed controller/service/procedure; Parts 1-3. |
| `GET /api/dealerships/{id}/appointments/{appointmentId}` | Missing controller -> no call chain | N/A | No | Proposed controller/service query; Part 1. |
| `GET /api/dealerships/{id}/availability` | Controller -> `AvailabilityService.EvaluateAsync` -> EF queries | Runtime `scheduler_app`; no transaction; advisory | Yes | Must remain advisory; booking revalidates in SQL. |
| `GET /api/dealerships/{id}/service-types` | Controller -> `ServiceCatalogService.GetAsync` -> bounded Redis/SQL | Runtime identity; optional cache | Yes | Booking must not use this cache for duration. |
| `--db-setup` | `DatabaseSetupCommand` -> `DatabaseSetupRunner` -> `MigrateAsync` -> `RuntimePrincipalProvisioner.ProvisionAsync` -> `FutureShiftSeeder` | Privileged setup; migration before provisioning | Yes | Must add mapping/procedure validation and grant allowlist. |
| Runtime identity | `scheduler_app` with `CONNECT` + schema `SELECT`; no `EXECUTE`/DML | Runtime connection | Reads only | Must add procedure `EXECUTE` and DML denies; Part 3. |
| Global error pipeline | `UseExceptionHandler` -> `SqlDependencyExceptionHandler` -> Problem Details | 503 only for classified SQL errors; 500 otherwise | Yes | Booking-known procedure errors must be handled before this boundary. |

## Phase 4: Consolidated code audit

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence / remaining verification |
| --- | --- | --- | --- | --- | --- |
| Must Change | `Program.cs` | No booking options, service, procedure boundary, or appointment registration. | Register booking options/validator, `IAppointmentBookingProcedure`, allocator/service, and controllers; keep existing pipeline. | S3-01-S3-06 | Inspected composition root. |
| Must Change | `ProblemCodes.cs`, `ProblemResponse.cs`, `ProblemDetailsSchemaTransformer.cs` | No `409`; schema describes four codes. | Add `capacity_unavailable`; document/emit it consistently. | S3-01, S3-04, S3-05 | Inspected. |
| Must Change | `ApiOperationTransformer.cs` | Documents only ticket 2 operations. | Add POST/GET summaries, parameters, success/error responses, Location, and conflict description. | S3-01, S3-06 | Inspected. |
| Must Change | `RuntimePrincipalProvisioner.cs` | Rejects any permission other than database `CONNECT` and schema `SELECT`. | Allow exact object `EXECUTE` on the procedure and exact appointment `DENY` DML; grant/validate idempotently. | S3-03, S3-06 | Inspected `ValidateDatabasePermissionsAsync` and `GrantRuntimePermissionsAsync`; probe verified ownership-chaining behavior. |
| Must Change | `DatabaseSetupRunner.cs` | Migrations -> provisioner -> seeder. | Invoke time-zone/procedure validation after migrations; preserve grant order and repeated setup. | S3-06 | Inspected. |
| Must Change | `SchedulerDbContext.cs`, new entity/configuration, `SeedData.cs` | No time-zone mapping persistence. | Add `TimeZoneMappings` entity/DbSet/configuration/seed; update model snapshot. | S3-02, S3-06 | Inspected model. |
| Must Change | New EF migration | No procedure or mapping. | Create mapping table/seed/FK, then `dbo.CreateAppointment`; verify operation order and upgrade. | S3-03, S3-06 | Proposed; actual ID generated during implementation. |
| Must Change | `appsettings.json`, booking options | No booking limits/timeouts. | Add defaults `MaxCandidateAttempts=5`, `MaxDeadlockRetries=2`, `RetryDelay=50ms`, `RequestBudget=20s`, `CommandTimeout=10s`; validate. | S3-03, S3-04 | Proposed. |
| Must Change | `OpenApiTests.cs`, `ProblemDetailsTests.cs`, `RuntimePermissionTests.cs` | No booking/409/procedure/permission cases. | Extend with documented POST/GET/error and permission assertions. | S3-01-S3-06 | Inspected patterns. |
| Must Change | `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/database/{overview,schema,migration,cache}.md`, `docs/README.md` | Booking remains planned; no procedure/mapping/grants/commands. | Record implemented behavior, configuration, migration, grant/deny, cache no-impact, and actual evidence. | S3-06 | Inspected. |
| Need Review | `AvailabilityQueries.cs` | Existing candidate queries are ordered and indexed but unbounded. | Reuse with `Take` bounds for booking, or add booking-specific query shapes; keep availability behavior unchanged. | S3-02 | Inspected predicates; generated SQL from ticket 2 research. |
| Need Review | `StartsAtParser.cs` / coverage helpers location | Parser and rules live under `Features/Availability`; booking would cross-reference them. | Reuse in place or extract to a shared scheduling namespace without behavior change; decide in plan. | S3-01, S3-02 | Inspected. |
| Need Review | `SqlFailureClassifier.cs` | Already maps 1205/1222/-2 to 503; no procedure error numbers. | Keep; handle 51001-51006 in the booking boundary before the global handler. | S3-04 | Inspected. |
| Need Review | Existing appointment indexes | Two resource/start indexes include end/status; no unique start key. | Verify the procedure predicates use them; add no redundant index unless a proven gap. | S3-02, S3-03 | Inspected configuration/migration; execution plan remains implementation-time. |
| Need Review | Database isolation configuration | New databases default to RCSI off in the probe; procedure will explicitly set `SERIALIZABLE`. | Document/verify `is_read_committed_snapshot_on` and use no `NOLOCK`/snapshot reads. | S3-03 | Probe result; runtime verification pending. |
| Need Review | EF retry configuration | `UseSqlServer` has no retrying execution strategy. | Do not enable automatic write retries; booking implements narrow 1205 retry only. | S3-04 | Inspected `Program.cs` and design-time factory. |
| No Change | `Appointment` entity and `AppointmentConfiguration` | Shape/checks/FKs/indexes already support the write. | Persist through the procedure; no appointment table/index change. | S3-01-S3-03 | Inspected. |
| No Change | `20261031092016_InitialCreate.cs` | Initial schema/seeds. | Leave intact; new migration adds ticket 3 objects. | S3-03, S3-06 | Inspected. |
| No Change | `ServiceCatalogService`, `BoundedCache`, cache configuration | Catalog cache only. | Booking must not read it; no cache key/payload/TTL change. | S3-02, S3-06 | Inspected; cache impact recorded in Part 3. |
| No Change | `SqlDependencyExceptionHandler` | Sanitized 503/500 mapping. | Keep for unhandled dependency/unexpected errors; booking handles known procedure errors. | S3-04 | Inspected. |
| Proposed Addition | `Features/Appointments/` controller, DTOs, service, options, planner, allocator | No booking HTTP/orchestration. | Add feature-local contracts and scheduling orchestration. | S3-01-S3-05 | Not present. |
| Proposed Addition | `Infrastructure/Persistence/AppointmentBookingProcedure.cs` (interface/implementation) | No write boundary. | Add `SqlCommand` procedure caller with output parameters, command timeout, and narrow `SqlException` mapping. | S3-03, S3-04 | Not present. |
| Proposed Addition | `Infrastructure/Setup/TimeZoneMappingValidator.cs` | No SQL time-zone mapping validation. | Validate mapping rows, Windows IDs, and .NET/SQL DST parity at setup. | S3-02, S3-06 | Not present. |
| Proposed Addition | Booking unit/integration tests, `docs/test/3-test.md`, `docs/deploy-notes/3-deploy-notes.md` | No ticket 3 evidence. | Add requirement-linked tests, records. | S3-01-S3-06 | Not present. |

## Phase 5: Consolidated gap register

Detailed analysis is in the parts. Status vocabulary: **Resolved** means the research decision is accepted for planning; **Probed** means supporting research-phase behavior was executed; **Deferred** means the check belongs to implementation/verification. No blocking gap remains Open.

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact | Status / evidence |
| --- | --- | --- | --- | --- | --- |
| O-01 | Decision | Procedure name, signature, inputs, outputs, and error numbers? | `dbo.CreateAppointment`; explicit dealership/customer/vehicle/service/technician/bay IDs, UTC start, API-owned evaluation instant; outputs appointment ID, end, duration snapshot, creation time; errors 51001-51006 (see Part 2). | S3-01-S3-05 | Resolved; Part 2. |
| O-02 | Decision | How does SQL opening validation use the persisted IANA zone when SQL Server rejects IANA IDs? | Persist an explicit IANA-to-Windows mapping (`TimeZoneMappings`), map the dealership row, use `AT TIME ZONE` inside the procedure, and validate parity/setup. | S3-02, S3-03 | Resolved and probed: SQL Server rejected `Asia/Ho_Chi_Minh` with error 9820; Windows IDs converted correctly; Part 2. |
| O-03 | Decision | Stable candidate order, attempt limit, and behavior when the first pair loses a race? | Order technicians and bays by name/ID; bounded pair attempts; skip resources known to conflict; try the next pair; return `409` when attempts are exhausted; document that capacity discovery is not exhaustive. | S3-03, S3-04 | Resolved; Part 2. |
| O-04 | Decision | Deadlock/candidate retry limits, backoff, lock/command timeouts, and total budget? | 5 candidate attempts; 2 deadlock retries; 50 ms backoff per retry; procedure `LOCK_TIMEOUT` 5 s; command timeout 10 s; total request budget 20 s. | S3-03, S3-04 | Resolved; Part 2. |
| O-05 | Decision | Who owns the future-start clock, and when is it evaluated? | API `TimeProvider` owns it; initial validation and a fresh per-attempt evaluation instant passed to the procedure; the procedure rechecks `@StartUtc > @EvaluatedAtUtc` with the per-attempt instant before its transaction/locks; direct callers supply an explicit instant; lock-wait limitation documented. | S3-01-S3-04 | Resolved; Part 2. |
| O-06 | Decision | How are migration, grants/denials, provisioner validation, repeated setup, and ticket 2 upgrade coordinated? | New migration creates mapping/seed/FK/procedure; provisioner creates the user then grants `EXECUTE` and denies direct appointment DML; setup validates mappings/procedure; tests cover fresh, repeated, and upgrade paths. | S3-03, S3-06 | Resolved; Part 3. |
| O-07 | Decision | Catalog-cache and availability compatibility? | Booking reads SQL duration and never the catalog cache; no cache key/payload/TTL change; committed appointments are visible to uncached availability; no booking-driven invalidation. | S3-02, S3-06 | Resolved; Parts 1-3. |
| O-08 | Decision | Stable `409` application error code? | `capacity_unavailable`, added to `ProblemCodes`/OpenAPI. | S3-01, S3-04, S3-05 | Resolved; Part 1. |
| O-09 | Decision | Exact POST/GET property names, types, UTC format, and Location behavior? | Request `customerId`, `vehicleId`, `serviceTypeId`, `startsAt` (raw string); response `id`, association IDs, `startsAt`, `endsAt`, `durationMinutes`, `status`, `createdAt`; UTC `Z`; `CreatedAtAction` Location. | S3-01, S3-05 | Resolved; Part 1. |
| O-10 | Decision | How do procedure errors map to HTTP outcomes? | 51001/51002/51004/51005 -> candidate retry then `409`; 51003 -> `400`; 51006 -> `500`; 1205 -> bounded retry then `503`; 1222/-2/classified connectivity -> `503`; unexpected -> `500`. | S3-01-S3-05 | Resolved; Parts 1 and 2. |
| O-11 | Decision | Does a direct procedure call enforce every rule without API prechecks? | Yes; the procedure validates ownership, scope, duration/end, active/qualified resources, opening/shift coverage, future start, and both conflicts. | S3-02, S3-03 | Resolved; Part 2; probe verified THROW/rollback behavior. |
| O-12 | Decision | Are new appointment indexes required? | No; existing technician/bay/start indexes include end/status and cover the procedure predicates. The mapping table gets its own PK/unique index. | S3-02, S3-03 | Resolved; Part 2; execution plan remains implementation-time. |
| O-13 | Decision | Which isolation configuration is used? | Explicit `SERIALIZABLE` in the procedure; no `NOLOCK`/snapshot reads; verify the database default RCSI state (probe: off) and keep booking reads authoritative. | S3-03 | Resolved and probed; Part 2. |
| O-14 | Decision | How are deadlock, lock/command timeout, cancellation, and unknown commit tested without flaky timing? | Unit-test the allocator/retry policy with a fake procedure boundary; integration-test lock timeout with a held lock and cancellation with an aborted request; document unknown-commit coverage as policy/code inspection plus no-retry tests. | S3-04 | Resolved; Part 4. |
| O-16 | Decision | Does "all Problem Details" require 405/415 codes? | The documented mandatory outcomes (400/404/409/503/500) and framework model-state 400 carry `code`/`traceId`; 405/415 are routing/media errors outside the ticket's documented booking matrix. Add them only if the plan decides the literal wording requires it. | S3-01, S3-04, S3-05 | Resolved with a documented interpretation; Part 1. |
| O-17 | Decision | Who generates appointment ID and creation time? | SQL Server generates `NEWID()` and UTC `SYSUTCDATETIME` cast to `datetimeoffset(7)`; the procedure returns both as output. | S3-01, S3-05 | Resolved; Part 2. |
| O-18 | Decision | Should the IANA/Windows mapping have a physical FK from dealerships? | Yes, proposed `TimeZoneMappings` PK `IanaId`, unique `WindowsId`, and `Dealerships.TimeZoneId` FK; migration order/upgrade is verified; setup also validates Windows IDs/DST parity. | S3-02, S3-06 | Resolved; Parts 2 and 3. |
| O-19 | Decision | What happens if a client disconnects after commit but before acknowledgment? | The API returns `503` or propagates cancellation; it does not replay the write. A later GET may reveal the appointment; idempotency is out of scope. | S3-03, S3-04 | Resolved with a documented limitation; Part 2. |
| O-20 | Decision | Is the procedure's `@EvaluatedAtUtc` parameter a security weakness? | It is an internal same-owner boundary executable only by `scheduler_app`; API clients cannot supply it. Direct callers/tests supply an explicit instant; the API derives it from the injected clock. | S3-01-S3-04 | Resolved; Part 2. |
| O-21 | Decision | Is a separate procedure interface/allocator justified? | Yes; it isolates SQL error mapping and makes candidate/deadlock/budget/cancellation policy unit-testable with a fake boundary while the real SQL procedure is integration-tested. | S3-03, S3-04 | Resolved; Parts 2 and 4. |
| O-22 | Decision | How does retrieval survive restart? | SQL Server named volume; GET reads SQL with no Redis; test a new API host/factory against the same database. | S3-05 | Resolved; Part 1/Part 4. |
| O-23 | Decision | How are rolling back to ticket 2 or recovering a failed setup handled? | Forward migration/down migration preserves appointments; runtime grants/denies require explicit revoke or database reset because the ticket 2 provisioner would reject them; document both. | S3-06 | Resolved; Part 3. |
| O-24 | Decision | Is the procedure allowed to use `SYSUTCDATETIME` for creation time while the API owns future-start? | Yes; creation time is server-authoritative, while future-start uses the API-owned evaluation instant to keep deterministic tests without client control. | S3-01-S3-04 | Resolved; Part 2. |

## Phase 6: Dependency-ordered change plan

Detailed file-level steps belong to the plans phase. This order keeps the schema/procedure, write boundary, API, and verification aligned.

| Step | Requirement IDs | Actual or proposed files / components | Change and dependency | Verification / completion criteria |
| --- | --- | --- | --- | --- |
| 1 | S3-02, S3-03, S3-06 | `Infrastructure/Persistence/Entities/TimeZoneMapping.cs`, `Configurations/TimeZoneMappingConfiguration.cs`, `DealershipConfiguration.cs`, `SeedData.cs`, `SchedulerDbContext.cs`, new migration | Add mapping table/seed/FK and `dbo.CreateAppointment`; update model snapshot; preserve existing schema/indexes. | Migration applies to empty and ticket 2 databases; mapping rows/procedure/FK/checks present; no appointment index change. |
| 2 | S3-03, S3-06 | `RuntimePrincipalProvisioner.cs`, `Infrastructure/Setup/TimeZoneMappingValidator.cs`, `DatabaseSetupRunner.cs` | Allow exact `EXECUTE` and DML denies; validate mapping/procedure/setup order; keep repeated setup idempotent. | Fresh/repeated setup and upgrade tests; exact permission rows; excessive permissions rejected; procedure executes. |
| 3 | S3-01-S3-04 | `Features/Appointments/BookingOptions.cs`, validator, planner, allocator, `IAppointmentBookingProcedure`/`SqlAppointmentBookingProcedure`, `AppointmentService.cs`, `Program.cs`, `appsettings.json` | Add orchestration, SQL boundary, retry/timeout/budget/cancellation policy, and configuration. | Unit tests for options/planner/allocator/failure policy; build/format. |
| 4 | S3-01, S3-05 | `Features/Appointments/AppointmentsController.cs`, contract records, `ProblemCodes.cs`, OpenAPI transformers | Add POST/GET, DTOs, Location, `capacity_unavailable`, OpenAPI docs. | API contract tests; OpenAPI/Problem Details assertions. |
| 5 | S3-02, S3-05 | Reuse/extract availability parsing/coverage queries in `AppointmentService`; no catalog cache calls | Validate ownership/scope/duration/opening and select bounded candidates from SQL. | Qualification/coverage/scope/cache-staleness tests; no writes on failure. |
| 6 | S3-01-S3-06 | New unit/integration tests and fixtures | Add T-01-T-16/S3-V01-V06 coverage, concurrency, direct-call, setup/upgrade, outage, and clean-checkout checks. | Executed commands/results recorded in `docs/test/3-test.md`; required failures block completion. |
| 7 | S3-06 | README, architecture, setup guide, database schema/overview/migration/cache, docs index, `docs/deploy-notes/3-deploy-notes.md` | Record implemented behavior, migration, grants/denials, commands, cache no-impact, recovery, and actual evidence. | Documentation review/links; no planned behavior presented as executed evidence. |

### Database and cache impact

- Database impact: **yes**. Add `dbo.TimeZoneMappings` (`IanaId` PK, `WindowsId` unique), seed the two supported mappings, add a `Dealerships.TimeZoneId` FK, and add `dbo.CreateAppointment`. No `Appointments` column or index change is expected; the existing resource/start indexes cover conflict predicates. The actual migration ID is generated during implementation and recorded in [[database/migration|Migrations]].
- Cache impact: **none**. No catalog key, payload, serialization version, TTL, or invalidation change is required. Booking reads `ServiceTypes.DurationMinutes` directly from SQL Server and never reads Redis; availability remains uncached; committed appointments are in SQL, so later availability sees them. Record this reason in the ticket records and, if useful, a short note in [[database/cache|Cache]] during implementation.
- Runtime permissions: add object `EXECUTE` on the procedure and explicit `DENY INSERT/UPDATE/DELETE` on `dbo.Appointments`; retain database `CONNECT` and schema `SELECT`; no roles, schema `EXECUTE`, DDL, or direct write grants.
- Configuration: add `Scheduler:Booking` options; no new container or package is expected.
- Telemetry: existing `ILogger` only; no Serilog/OpenTelemetry work in this ticket.

## Rollback or forward recovery

| Step | Objects / migration IDs | Recovery action and command / working directory | Prerequisites / identity | Compatibility and data-loss limits | Verification / observed evidence |
| --- | --- | --- | --- | --- | --- |
| Schema downgrade | New ticket 3 migration (ID generated during implementation) | Apply the migration down (`dotnet ef database update 20261031092016_InitialCreate --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj`) or run the documented local reset. | Privileged setup identity; repository root. | Drops the procedure, mapping FK/table; existing appointments remain. A down migration does not restore deleted data. | Planned; verify against isolated SQL Server. |
| Runtime permissions after downgrade | `scheduler_app` object grants | `REVOKE EXECUTE ON OBJECT::dbo.CreateAppointment FROM scheduler_app;` and `REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments FROM scheduler_app;` (or drop/recreate the database). | Privileged setup identity. | Required before the ticket 2 provisioner can pass its allowlist; otherwise setup fails on unexpected permissions. | Planned; Part 3. |
| Application rollback | API artifact/configuration | Redeploy the previous API version and remove booking configuration; SQL remains. | Local Compose/host configuration. | Previous version has no booking endpoints; existing appointment rows remain readable only by direct SQL/admin. | Planned. |
| Forward recovery | Procedure/mapping/grant state | Fix the mapping/procedure/grants and rerun `--db-setup`; migrations are transactional and repeated setup is idempotent. | Privileged setup identity. | Preferred for a partially failed setup; no appointment data loss for additive changes. | Planned; S3-V06. |
| Destructive local reset | SQL named volume | `docker compose down -v` then `docker compose up -d --wait`. | Local operator. | Removes all local synthetic data, including appointments. | Existing ticket 1/2 procedure; retest if used. |

## Handoffs to other parts

| Source part | Finding / decision and evidence | Target part | Required action | Dependency / status |
| --- | --- | --- | --- | --- |
| Overview | Procedure/mapping/permission design and limits. | Parts 1-4 | Use the same contracts, error mapping, and verification IDs. | Planning. |
| Part 1 | DTOs, `capacity_unavailable`, Location, validation matrix. | Parts 2 and 4 | Align procedure outputs and API/error tests. | Planning. |
| Part 2 | Procedure errors/timeouts/candidate policy/time-zone mapping. | Parts 1, 3, and 4 | Map HTTP outcomes, provision grants, and design tests. | Planning. |
| Part 3 | Migration/grants/setup/upgrade/recovery. | Parts 2 and 4 | Verify procedure operability and setup tests. | Planning. |
| Part 4 | Test matrix, commands, evidence rules. | Implementation | Write and execute tests; record in `docs/test/3-test.md`. | Planned; not executed. |
| Ticket 2 | Advisory availability, catalog cache, and read-only runtime boundary. | Ticket 3 | Preserve advisory semantics; do not use cache for booking; add only the protected write path. | Existing behavior; ticket 3 changes the runtime permission boundary. |

## Phase 7: Scope verification

### 7.1 Change-to-requirement trace

| Change / affected component | Requirement ID / source | Direct requirement or necessary dependency | Research evidence / proposed verification |
| --- | --- | --- | --- |
| POST/GET controllers, DTOs, Location, UTC formatting | S3-01, S3-05, AC3-01, AC3-10 | Required | Part 1; T-01, S3-V01. |
| Ownership/scope/offset/future/overflow validation | S3-01, S3-02, AC3-02 | Required | Parts 1 and 4; T-10, T-11. |
| SQL duration, opening/shift coverage, qualification/active checks | S3-02, AC3-02, AC3-11 | Required | Parts 1 and 2; T-02, T-03, S3-V02/V05. |
| Half-open technician/bay conflict checks | S3-02, AC3-03 | Required | Part 2; T-04-T-06. |
| `dbo.CreateAppointment` locks/transaction/insert/commit | S3-03, AC3-05, AC3-06 | Required | Part 2; T-07-T-09. |
| Time-zone mapping table/seed/FK/procedure join | S3-02, S3-06, AC3-05 | Necessary dependency for SQL opening validation | Parts 2 and 3; S3-V05/V06. |
| Runtime `EXECUTE` and DML denies/provisioner allowlist | S3-03, S3-06, AC3-05 | Required | Part 3; T-08, S3-V06. |
| Candidate planner/allocator, deadlock retry, timeouts, budget, cancellation | S3-04, AC3-06, AC3-12 | Required | Part 2; S3-V03/V04. |
| SQL error mapping (`51001`-`51006`, `1205`, `1222`, `-2`, unknown) | S3-04, AC3-12 | Required | Parts 1 and 2; S3-V04. |
| New migration/model/seed | S3-03, S3-06, AC3-13 | Required | Part 3; S3-V06. |
| Booking options/configuration | S3-03, S3-04 | Necessary dependency | Part 3; unit/API tests. |
| No cache change; no cache read in booking | S3-02, S3-06, AC3-11 | Required compatibility assessment | Parts 1-3; T-13, S3-V02. |
| OpenAPI/Problem Details updates | S3-01, S3-04, S3-05, AC3-10 | Required | Part 1; S3-V01. |
| Unit/API/SQL/concurrency/permission/setup tests | S3-06, AC3-04-AC3-13 | Required | Part 4; T-01-T-16, S3-V01-V06. |
| README/architecture/setup/database/cache/deploy/test records | S3-06, AC3-09, AC3-13 | Required | Parts 3 and 4; documentation review/executed evidence. |
| Telemetry, auth, cancellation/rescheduling, idempotency, vehicle overlap | Excluded sources | Not ticket 3 requirements | Out-of-scope list. |

### 7.2 Scope checklist

- [x] Every S3-01-S3-06 requirement, AC3-01-AC3-13 acceptance criterion, T-01-T-16 case, and S3-V01-S3-V06 case has a research owner or an explicit gap.
- [x] Each proposed change traces to a ticket requirement or a necessary dependency.
- [x] Assumptions, exclusions, and deferred extensions remain explicit.
- [x] Optional improvements are separated from required work (405/415 code interpretation is the only optional item).
- [x] Existing behavior, proposed additions, and executed research probes are distinguished; no ticket test or acceptance is claimed.
- [x] Database, permission, cache, configuration, operational, and test impacts are assessed.
- [x] Proposed paths/commands are labelled; no implementation or passing evidence is invented.
- [x] Research verification passed on 2026-11-01; this checklist does not assert implementation, executed tests, or ticket acceptance.

### 7.3 Items flagged for removal

| Item | Why it falls outside scope | Follow-up, if any |
| --- | --- | --- |
| Appointment cancellation/rescheduling | Ticket and README exclude it; client request-abort handling remains in scope. | Separate ticket. |
| Idempotent POST / exactly-once behavior | AS-11 and ticket exclusion; a repeat POST is a new attempt. | Separate scoped contract. |
| Vehicle overlap protection | AS-10; only technician/bay capacity is protected. | Separate extension. |
| Authentication/customer access control | Local unauthenticated demo; ownership checks are data integrity. | Separate auth/authorization design. |
| Serilog/OpenTelemetry/Prometheus/Tempo/Grafana | Ticket 4. | Ticket 4. |
| Redis booking locks/cache stampede protection | Redis is not a lock and booking never reads cache. | Not required. |
| New appointment indexes | Existing indexes cover predicates; add only if a proven gap. | Implementation review. |
| 405/415 Problem Details codes | Outside the documented booking outcome matrix; optional literal-wording extension. | Decide in plan only if required. |
| Admin CRUD/external integrations/payments/notifications/background jobs | Initial release exclusions. | Separate tickets. |

## Dependencies

| Dependency | Requirement / component blocked | Resolution needed | Status / evidence |
| --- | --- | --- | --- |
| Existing SQL Server 2022 CU23 image and Testcontainers fixture | Procedure locking, permissions, migration, concurrency tests | Reuse the pinned image/fixtures. | Existing; ticket 1/2 records. |
| Existing `scheduler_app` and setup identities | Runtime execution/denies and privileged migration/seed | Extend provisioner allowlist and grants; preserve SID/role checks. | Existing; Part 3. |
| Existing appointment schema/checks/indexes | Atomic insert and conflict predicates | Reuse; no appointment table/index change expected. | Inspected; Part 2. |
| `Microsoft.Data.SqlClient` | Procedure caller/output parameters/error numbers | Already used transitively/explicitly by setup; no new package expected; add an explicit pin only if build/analyzer requires it. | Existing; probe used 6.1.7. |
| SQL Server `AT TIME ZONE` Windows IDs | IANA opening validation inside the procedure | Add `TimeZoneMappings`; verify seeding/upgrade and DST parity. | Probed; Part 2. |
| Docker/Compose | Clean-checkout, restart, Redis/SQL outage checks | Reuse the Compose stack and named volume. | Existing. |
| No new cache/telemetry package | Cache/telemetry scope | Do not add. | Required exclusion. |

## Decisions made

| Decision | Reason / alternatives | Requirement impact | Date / evidence |
| --- | --- | --- | --- |
| Use multipart research for ticket 3. | Four concern areas with separate evidence needs (API, procedure/concurrency, permissions/migration, verification). | Keeps one primary entry point and traceability. | 2026-11-01; research guide. |
| Use `dbo.CreateAppointment` with explicit resource IDs and an API-owned evaluation instant. | The API selects bounded ordered candidates; the procedure revalidates and owns the transaction; direct calls are testable without API prechecks. | S3-01-S3-05. | 2026-11-01; Part 2. |
| Add `dbo.TimeZoneMappings` and use Windows IDs with `AT TIME ZONE`. | SQL Server rejects IANA IDs; an explicit mapping preserves the persisted IANA key and makes the dependency visible/validated. | S3-02, S3-03. | Probed 2026-11-01; Part 2. |
| Use explicit appointment DML `DENY` plus object `EXECUTE`. | The ticket asks for explicit write denials; ownership chaining lets the procedure insert while direct DML is denied. | S3-03, S3-06. | Probed 2026-11-01; Part 3. |
| Use candidate attempts 5, deadlock retries 2, 50 ms backoff, 5 s lock timeout, 10 s command timeout, 20 s budget. | Concrete bounded policy; short transactions; no unbounded waits or write replay. | S3-03, S3-04. | 2026-11-01; Part 2. |
| Use `capacity_unavailable` for `409`. | Stable, descriptive code distinct from validation/not-found/dependency failures. | S3-01, S3-04, S3-05. | 2026-11-01; Part 1. |
| Keep booking independent of Redis; no cache change. | Catalog cache is not authoritative for duration or capacity; booking writes SQL. | S3-02, S3-06. | 2026-11-01; Parts 1-3. |
| Use a procedure interface/allocator seam for failure tests. | Deadlock/cancellation/unknown-commit policy is otherwise hard to exercise deterministically. | S3-04. | 2026-11-01; Parts 2 and 4. |
| Do not enable EF automatic write retries. | A connection failure at commit may have an unknown outcome; only a rolled-back 1205 is safely replayable. | S3-04. | 2026-11-01; Part 2. |
| Use SQL `NEWID()`/`SYSUTCDATETIME` for ID/creation time. | Server-authoritative persisted values; API returns them. | S3-01, S3-05. | 2026-11-01; Part 2. |

## Out of scope

| Item | Ticket exclusion or reason | Follow-up, if any |
| --- | --- | --- |
| Appointment cancellation and rescheduling | Ticket/README exclusions; separate contracts and tests. | Separate ticket. |
| Authentication/authorization/customer access control | Local unauthenticated demo; dealership/ownership checks are data integrity. | Separate auth design. |
| Idempotent POST/exactly-once | AS-11; repeated POST is a new attempt. | Separate scoped contract. |
| Vehicle overlap protection | AS-10. | Separate extension. |
| Serilog/OpenTelemetry/Prometheus/Tempo/Grafana | Ticket 4. | Ticket 4. |
| Redis booking locks/cache stampede protection | Redis is not a lock; booking uses SQL procedure locking. | Not required. |
| Admin CRUD, external integrations, payments, notifications, background jobs | Initial release exclusions. | Separate tickets. |
| Production hosting/backups/high availability/performance targets | Local demo scope; no performance claims. | Separate design. |
| Custom frontend/UI | Backend-only; generated Swagger UI. | N/A. |

## Files referenced

Actual inspected files and symbols:

- `docs/tickets/3 - Appointment Booking and Retrieval.md` (staged 2026-11-01 update): scope, requirements, acceptance criteria, verification cases, open decisions.
- `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/README.md`, `docs/database/overview.md`, `docs/database/schema.md`, `docs/database/migration.md`, `docs/database/cache.md`, `docs/database/maintenance.md`, `docs/test/README.md`, `docs/research/README.md`, `docs/templates/research-template.md`, `docs/plans/README.md`, `docs/deploy-notes/2-deploy-notes.md`.
- `docs/research/2/00-overview.md`, `docs/research/2/01-api-contracts-and-validation.md`, `docs/research/2/02-availability-and-scheduling.md` (reusable decisions, DST policy, error/query patterns).
- `src/ServiceScheduler.Api/Program.cs`; `ServiceScheduler.Api.csproj`; `appsettings.json`; `appsettings.Development.json`.
- `src/ServiceScheduler.Api/Infrastructure/Persistence/SchedulerDbContext.cs`, `SchedulerDbContextFactory.cs`, `Entities/Appointment.cs`, `Entities/AppointmentStatuses.cs`, `Entities/*.cs`, `Configurations/AppointmentConfiguration.cs`, `Configurations/SeedData.cs`, `Configurations/*.cs`.
- `src/ServiceScheduler.Api/Migrations/20261031092016_InitialCreate.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Setup/DatabaseSetupCommand.cs`, `DatabaseSetupRunner.cs`, `SchedulerSetupOptions.cs`, `RuntimePrincipalProvisioner.cs`, `FutureShiftSeeder.cs`.
- `src/ServiceScheduler.Api/Features/Availability/AvailabilityController.cs`, `AvailabilityService.cs`, `AvailabilityQueries.cs`, `StartsAtParser.cs`, `AvailabilityInterval.cs`, `OpeningCoverage.cs`, `ShiftCoverage.cs`, `HalfOpenOverlap.cs`, `Contracts/AvailabilityResponse.cs`.
- `src/ServiceScheduler.Api/Features/ServiceCatalog/ServiceCatalogService.cs`, `ServiceCatalogController.cs`, `ServiceCatalogCacheKeys.cs`; `Infrastructure/Caching/BoundedCache.cs`, `CacheOptions.cs`, `CacheHealthCheck.cs`.
- `src/ServiceScheduler.Api/Infrastructure/Errors/ProblemCodes.cs`, `ProblemResponse.cs`, `SqlFailureClassifier.cs`, `SqlDependencyExceptionHandler.cs`; `Infrastructure/OpenApi/ApiOperationTransformer.cs`, `ProblemDetailsSchemaTransformer.cs`; `Infrastructure/Health/HealthResponseWriter.cs`.
- `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs`, `SqlServerContainerFixture.cs`, `SqlServerTestBase.cs`, `SchedulingTestData.cs`, `SqlServerCollection.cs`, `SqlServerAndRedisCollection.cs`.
- `tests/ServiceScheduler.IntegrationTests/Startup/ProblemDetailsTests.cs`, `OpenApi/OpenApiTests.cs`, `Security/RuntimePermissionTests.cs`, `Persistence/MigrationAndSeedTests.cs`, `Persistence/ConstraintTests.cs`, `Availability/*.cs`.
- `tests/ServiceScheduler.UnitTests/Availability/*.cs`, `Errors/SqlFailureClassifierTests.cs`, `Configuration/*.cs`, `Caching/*.cs`.
- `compose.yaml`, `.env.example`, `Directory.Packages.props`, `global.json`, `ServiceScheduler.slnx`, `src/ServiceScheduler.Api/Dockerfile`.
- Research-phase probes outside the repository: pinned SQL Server 2022 CU23 container and a temporary `Microsoft.Data.SqlClient` 6.1.7 console project. Results are recorded in Part 2/Part 3; they are not ticket tests.

Proposed files/directories not yet existing: `src/ServiceScheduler.Api/Features/Appointments/`, `Infrastructure/Persistence/AppointmentBookingProcedure.cs` and the procedure interface, `Infrastructure/Setup/TimeZoneMappingValidator.cs`, `Infrastructure/Persistence/Entities/TimeZoneMapping.cs`, `Infrastructure/Persistence/Configurations/TimeZoneMappingConfiguration.cs`, a new migration (actual ID pending), booking unit/integration tests, `docs/test/3-test.md`, `docs/test/3-manual-test.md`, `docs/deploy-notes/3-deploy-notes.md`, and the ticket 3 plan under `docs/plans/3/`.

## Verification

- Command: `/orchestrator-ticket verify research 3`.
- Status: **Passed** for planning.
- Verified on: 2026-11-01.
- Checkout: HEAD `90a62840d04756e1ec3c28bdc64ba53365be282f` on `main`; the 2026-11-01 ticket 3 update and the five research files are staged. This verification's corrections are working-tree changes relative to the staged versions.
- Input fingerprints (SHA-256 of LF-normalized content; the overview fingerprint covers the research content before this Verification section):

| Input | Fingerprint |
| --- | --- |
| `docs/tickets/3 - Appointment Booking and Retrieval.md` | `7491ee3a8503690f420b191397deb094a67796f15f0ed1d716a8e8d6b3e0a3a9` |
| `docs/research/3/00-overview.md` (content before this section) | `e05f569734849c1123fce6b160cb7436255d33b1ad2ad5b403d2d1546f5bb149` |
| `docs/research/3/01-api-contracts-and-retrieval.md` | `3fa887b5e77e37a6242cb32523fc441774dcfe4a6a81c4c7062a38f69a87191c` |
| `docs/research/3/02-booking-procedure-and-concurrency.md` | `9927f7e2832e7afdc28207a11abeafd0b8d264f265a275e2b6f6af79bbb25350` |
| `docs/research/3/03-permissions-migrations-and-operations.md` | `5aa707f56826a883714eca94781585d258daed557b22a67394b509777c2e7dd8` |
| `docs/research/3/04-verification-and-documentation.md` | `d1328b949911de8e9800ad061fc20b0309f8144c64134ba56287330cd65ef08f` |

- Forward trace: every S3-01-S3-06 requirement, AC3-01-AC3-13 acceptance criterion, ticket-defined T-series case (T-01-T-11, T-13, T-14, T-16), and S3-V01-S3-V06 case has a research owner in Phase 0, the part documents, and Phase 7.1. The ticket's 13 unlabeled acceptance bullets were checked in order against AC3-01-AC3-13; the labels remain research-local and are not new ticket IDs.
- Reverse trace: every proposed change in Phase 6 and Phase 7.1 traces to a ticket requirement or a necessary dependency; exclusions preserve the ticket boundaries; no implementation, executed test, or acceptance is claimed.
- Requirement traceability (the test IDs are required checks; executed results belong to `docs/test/3-test.md` and are not established here):

| Requirements | Research coverage | Verification mapping |
| --- | --- | --- |
| S3-01, AC3-01, AC3-02, AC3-10 | Overview; Part 1 routes, DTOs, validation, Location, retrieval; Parts 2 and 4 | T-01, T-10, T-11, S3-V01, S3-V05 |
| S3-02, AC3-02, AC3-03, AC3-11 | Parts 1 and 2 eligibility, duration, and snapshot; Part 4 duration/eligibility/interval | T-02-T-06, S3-V02, S3-V05 |
| S3-03, AC3-03-AC3-06, AC3-12 | Part 2 procedure, locking, and atomicity; Part 3 write boundary; Part 4 concurrency | T-04-T-09, S3-V03, S3-V04 |
| S3-04, AC3-06, AC3-12 | Parts 1 and 2 retry/failure mapping; Part 4 failure boundaries | T-07-T-09, S3-V04 |
| S3-05, AC3-01, AC3-07, AC3-10 | Part 1 retrieval; Parts 3 and 4 restart/outage | T-01, T-11, T-13, T-14, S3-V01 |
| S3-06, AC3-07-AC3-13 | Parts 3 and 4 setup/migration/permissions/documentation | T-08, T-13, T-14, T-16, S3-V06 |

- Existing-evidence checks: inspected `Program.cs`, `ProblemCodes`/`ProblemResponse`, `SqlFailureClassifier`, `Appointment`/`AppointmentConfiguration`/`AppointmentStatuses`, `20261031092016_InitialCreate`, `SchedulerDbContext`, `RuntimePrincipalProvisioner`, `DatabaseSetupRunner`, `SeedData`, availability queries/service/helpers, OpenAPI transformers, test fixtures and existing tests, `compose.yaml`, `appsettings.json`, `global.json`, the architecture and database guides, and the staged ticket diff. The cited files, symbols, seed IDs, time zones, indexes, permission allowlist, fixed clock, fixture behavior, and absence of ticket 3 application code match the checkout; only minor line-range drift was observed in a few citations and is non-blocking.
- Impact checks: database impact is limited to `TimeZoneMappings`, the dealership FK, and `dbo.CreateAppointment`; no appointment table or index change is planned. Runtime permissions add procedure `EXECUTE` and appointment DML `DENY` while keeping `CONNECT`/schema `SELECT`. Cache impact is none because booking never reads Redis, and telemetry remains ticket 4.
- Corrections applied: clarified that the procedure checks the future start with the per-attempt instant before its transaction and locks (Part 2 and overview O-05/P3-05), matching the proposed body; clarified that `MaxCandidateAttempts` counts distinct candidate pairs and deadlock retries are separate; added the `.UtcDateTime` projection note that produces `Z` timestamps; set the research frontmatter to `verified` and updated the Phase 7.2 verification checklist. The affected forward/reverse traces were rechecked.
- Non-blocking observations for planning and implementation:
  - `51003` covers a caller-owned transaction, an internal misuse, but maps to `400 validation_error`. The API must call the procedure outside any EF transaction; if the API boundary can ever surface this condition, handle it as an internal failure rather than client validation.
  - The proposed procedure sets `XACT_ABORT ON` before its early validation; the implementation note already requires the final migration SQL to place early validation in `TRY`/`CATCH` or otherwise reset session state on every exit. Follow that note when writing the procedure.
  - EF's generated migration order must create and seed `TimeZoneMappings` before adding the `Dealerships` FK; the upgrade test is the gate (M3-04).
  - A true lost-commit-acknowledgment fault cannot be injected locally; S3-V04's unknown-commit policy is planned as a fake-boundary no-retry test plus code inspection, with the limitation documented. Planning must preserve that coverage decision.
  - The 405/415 Problem Details interpretation remains a documented interpretation rather than a ticket-mandated outcome; no booking requirement depends on it.
  - Research-phase SQL/.NET probes were not rerun during this verification; they remain non-ticket evidence and do not establish any T-series or S3-V result.
- Result: **Passed** for planning. This record does not establish implementation, executed tests, Swagger output, Compose behavior, or ticket acceptance; AC3-01-AC3-13 and the T/S3-V checks must not be marked Passed from this document. The next phase is `/orchestrator-ticket plan 3`.
