---
title: "3 - Appointment Booking and Retrieval"
tags: [ticket, service-scheduler, api, booking, concurrency]
ticket: 3
status: specified
created: 2026-10-28
updated: 2026-11-01
---

# 3 - Appointment Booking and Retrieval

## Objective and baseline

Confirm and retrieve persistent appointments while preventing overlapping reservations for either technicians or bays, including under concurrent requests.

Read [README](../../README.md) and [[architecture]] for the complete booking algorithm, domain, assumptions, API/error contracts. Implement tests with the booking behavior, including real SQL Server concurrency tests.

### Inspected starting point

Tickets 1 and 2 provide the API host, `SchedulerDbContext`, initial migration `20261031092016_InitialCreate`, appointment entity/constraints/indexes, synthetic customer/vehicle/resource data, future-shift seeder, and real SQL Server/Redis test fixtures. Discovery, catalog, and advisory availability are implemented; booking/retrieval endpoints and the booking procedure remain missing. Existing verification is recorded in [[test/1-test]] and [[test/2-test]]; preparing this ticket does not rerun or establish those results.

Extend the existing `Features` structure, availability time/coverage rules, Problem Details handling, and SQL failure classification. Inspect their actual call paths before deciding what can be shared. Availability is advisory and must never become a reservation or replace transactional validation.

`RuntimePrincipalProvisioner` currently permits only `CONNECT` and schema-level `SELECT` and rejects other explicit permissions. Adding procedure execution and explicit write denials requires coordinated changes to provisioning, its permission allowlist, migration/setup order, and permission tests. Existing appointment indexes already cover dealership/technician/start and dealership/bay/start with end/status included; inspect them and the generated conflict queries before adding redundant indexes.

### Requirement traceability

Use these ticket-local IDs in research, plans, review, and execution evidence. The sections below define their required behavior.

| ID | Deliverable | Project requirements |
| --- | --- | --- |
| S3-01 | Validated appointment creation with a complete persisted response and retrieval Location. | A-01, A-03, D-02 |
| S3-02 | Authoritative full-interval eligibility, scoped relationships, duration snapshot, and independent resource conflict checks. | A-02, A-03 |
| S3-03 | Atomic SQL Server allocation under concurrency through a restricted procedure write boundary. | A-03, P-01 |
| S3-04 | Bounded candidate/deadlock handling, classified failures, cancellation, and unknown-commit safety. | P-01, D-02 |
| S3-05 | Dealership-scoped retrieval of persisted appointments independent of Redis. | A-03 |
| S3-06 | Automated verification, OpenAPI, and updated operational/database records. | D-01, D-02, D-03, D-04, P-04 |

### Research handoff and open decisions

Prepare research using [[research/README|Research guide]] and [[templates/research-template|Research Protocol]]. Choose one primary entry point, either `docs/research/3-research.md` or `docs/research/3/00-overview.md`. Research and implementation remain pending; this ticket is the scope baseline for that work.

Research must resolve the following before implementation planning, with inspected evidence, proposed contracts, and requirement-linked verification:

- Define the procedure name/signature, authoritative inputs and returned values, validation error numbers, transaction ownership, and behavior when called directly without API prechecks. Map persisted fields through the POST and GET DTOs; distinguish proposed names from existing schema names (S3-01-S3-03, S3-05).
- Define how SQL-side full opening-hour validation uses the persisted IANA dealership time zone and agrees with the existing .NET coverage/DST policy. Do not trust an API-supplied coverage flag or client-calculated end/duration; document any necessary mapping or configuration dependency (S3-02, S3-03).
- Choose stable candidate ordering, bounded candidate enumeration/attempts, and behavior when a first pair loses a race but another eligible pair remains. Document the limit's effect on `409` responses rather than claiming exhaustive capacity discovery (S3-03, S3-04).
- Select concrete candidate/deadlock retry limits, backoff, lock/command timeouts, and a total request budget. Inspect existing SQL error classification and EF execution strategies so automatic retries cannot replay a write after an unknown commit. Define request cancellation, rollback/connection cleanup, and safe error behavior when commit acknowledgement is lost (S3-04).
- Define future-start clock ownership and evaluation points for API and direct procedure calls, including requests that wait on resource locks. Keep deterministic clock fixtures consistent without weakening production validation (S3-01-S3-03).
- Coordinate migration creation/removal of the procedure, grants/denials, provisioner validation, repeated setup, and upgrade of an existing ticket 2 database. Specify recovery and compatibility limits, including preservation of existing appointments and the protected write boundary (S3-03, S3-06).
- Assess catalog-cache and availability compatibility: booking must read current SQL duration even with a stale catalog cache, and newly committed appointments must affect subsequent uncached availability. No new cache or telemetry infrastructure is required (S3-02, S3-06).

## Functions to implement

| Endpoint | Behavior |
| --- | --- |
| `POST /api/dealerships/{dealershipId}/appointments` | Accept customer, vehicle, service type, and desired start; atomically allocate a qualified technician and bay and persist confirmation. |
| `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}` | Retrieve the complete persisted appointment; return `404` for missing or other-dealership appointments. |

POST accepts `customerId`, `vehicleId`, `serviceTypeId`, and `startsAt`. Reject invalid customer/vehicle ownership, missing or out-of-dealership entities, past/offset-free starts, and invalid service intervals using the shared contract. This is data integrity in an unauthenticated local demo, not authorization.

On success return `201 Created`, a retrieval `Location`, all associated IDs, UTC start/end, service-duration snapshot, status, and creation time. Resource IDs, end time, and duration are chosen by the server. Repeating POST is a new booking attempt; idempotency is outside scope.

### API and time contracts

- Document the exact POST/GET JSON property names and types in research and OpenAPI. Both responses represent the same persisted appointment: appointment, dealership, customer, vehicle, service type, technician, and bay IDs; start/end; duration snapshot; confirmed status; and creation time. All response timestamps use UTC with `Z`. GET must use stored duration/end rather than recomputing from a later catalog value; expanded customer/vehicle profiles are not required.
- Return `400 validation_error` for malformed/missing required input, malformed UUIDs, unparseable or offset-free timestamps, non-future starts, interval overflow, and mismatched customer/vehicle ownership. Return `404 not_found` for missing entities or a service/appointment outside the requested dealership. Customers and vehicles have ownership relationships; do not invent a dealership relationship for them.
- Return `409` for a valid request whose interval cannot obtain an eligible resource pair under the documented bounded allocation policy, including insufficient opening/shift coverage. Research must select a stable conflict code. Preserve `503 dependency_unavailable` for classified dependency/lock failures and `500 internal_error` for unexpected failures; all Problem Details include `code` and `traceId`, including framework validation responses.
- Validate the raw `startsAt` offset before conversion and normalize to UTC. Use authoritative positive service duration and half-open intervals `[start, end)`. Equivalent explicit offsets identify the same instant; the input offset need not match the dealership zone. The complete interval must fit one opening interval and one technician shift, without joining adjacent rows; an end exactly at closing/shift end is allowed. Follow AS-03-AS-06 and the established DST policy, with any SQL/.NET parity gap resolved in research.
- Booking and retrieval read authoritative SQL Server state and do not require Redis. Document success, validation, not-found, conflict, dependency, and server outcomes in OpenAPI; GET has no capacity-conflict outcome.

## Transaction and persistence

- Implement the shared SQL Server booking procedure through EF migration SQL. Use one short `SERIALIZABLE` transaction, `SET XACT_ABORT ON`, rollback handling, and `UPDLOCK, HOLDLOCK` on the selected technician row followed by the bay row. Hold both locks until commit/rollback, including when no appointments exist.
- Revalidate entity relationships, dealership scope, service duration, active resources, qualifications, and full opening/shift coverage inside the transaction. Availability reads never substitute for this check.
- Recheck technician and bay conflicts independently using half-open intervals. Insert one confirmed appointment containing both resources and the duration snapshot, then commit before returning success. Failed allocation leaves no partial booking.
- Grant the runtime principal EXECUTE on the same-owner static booking procedure and retain its read access. Deny direct appointment INSERT/UPDATE/DELETE, schema changes, and owner/admin roles. Migration/seed credentials remain separate.
- Maintain separate dealership/resource/start indexes including end for technician and bay queries. Verify the isolation configuration; do not use stale snapshot reads, `NOLOCK`, Redis/process locks, unique start keys, or an EF concurrency token as the overlap safeguard.
- Map technician/bay conflicts to documented procedure errors, initially `51001` and `51002`; try alternative candidates within a documented bounded limit. No available pair returns `409`.
- Retry a whole rolled-back deadlock-victim attempt (`1205`) only within a bounded policy. Exhausted deadlocks/lock timeouts and classified database outages return `503`; unexpected failures return controlled `500` responses. Do not turn every SQL exception into a conflict or blindly retry writes with an unknown commit outcome.

## Acceptance criteria

- [ ] Valid booking returns `201` and complete retrievable associations; confirmation survives an API restart.
- [ ] Invalid ownership, dealership scope, qualification, time, shift/opening coverage, and resource availability are rejected without unintended writes.
- [ ] Adjacent appointments succeed; partial overlaps, containment, and identical intervals conflict for either resource independently.
- [ ] With one eligible pair and 20 distinct coordinated concurrent POSTs, exactly one returns `201`, nineteen return `409`, and one appointment is stored.
- [ ] Independent runtime connections calling the procedure without API prechecks reject overlapping technician-only and bay-only bookings; valid execution succeeds and direct writes are denied.
- [ ] Tests cover an initially empty appointment table, lock waiting followed by a fresh overlap check, atomic rollback/released locks, and bounded deadlock/timeout behavior. Exercise competing API instances where practical.
- [ ] Booking still works during Redis outage; SQL Server outage prevents confirmation and fails readiness.
- [ ] Booking/retrieval and concurrency checks use real seeded UUIDs and generated future times within seeded shifts.
- [ ] Local end-to-end startup and demo commands work from a clean checkout; README, design, and database/procedure documentation include actual verification evidence.
- [ ] POST/GET DTOs, Location, UTC formatting, all Problem Details outcomes, and strict input handling match the documented contract (S3-01, S3-04, S3-05).
- [ ] A changed catalog duration does not alter an existing appointment; a stale Redis catalog cannot influence a new booking's authoritative duration, and subsequent availability sees committed reservations (S3-02, S3-05).
- [ ] Alternative-pair allocation, request cancellation, and unknown-commit handling follow the documented bounds without unsafe write replay (S3-03, S3-04).
- [ ] Fresh setup, repeated setup, and upgrade from the ticket 2 database apply the procedure and intended runtime permissions successfully; recovery is documented and verified against isolated SQL Server data (S3-03, S3-06).

## Verification and scope boundaries

Owns A-01 through A-03 and P-01. Use xUnit, WebApplicationFactory, independent SQL Server connections, controllable time, and coordination barriers; assert final committed state. Mocks, EF in-memory, and SQLite cannot prove SQL Server locking or permissions.

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-01 Valid future booking | API + SQL Server | `201`, correct duration, qualified technician and bay, retrievable persisted associations. | A-01, A-02, A-03 |
| T-02 Qualification or inactive resource | Unit + integration | Unqualified/inactive candidates never satisfy availability or booking. | A-02 |
| T-03 Partial shift/opening coverage | Unit + integration | Resources free at the start but unavailable later cannot be booked. | A-02 |
| T-04 Interval boundaries | Unit + SQL Server | Adjacent reservations succeed; partial overlap, containment, and identical intervals conflict. | A-02, P-01 |
| T-05 Technician conflict only | SQL Server | Same technician with a different bay still conflicts. | P-01 |
| T-06 Bay conflict only | SQL Server | Same bay with a different technician still conflicts. | P-01 |
| T-07 Concurrent competition | API + SQL Server | With exactly one eligible pair, 20 distinct concurrent POSTs produce one `201`, nineteen `409`s, and one stored appointment. | P-01 |
| T-08 Database write boundary | SQL Server | Independent connections using the runtime principal call the procedure without API pre-checks: overlapping technician-only and bay-only bookings are rejected. Direct appointment inserts, updates, and deletes are denied; procedure execution succeeds for a valid booking. | P-01 |
| T-09 Atomic failure | SQL Server | Failed allocation leaves no partial or confirmed booking. | A-03, P-01 |
| T-10 Time handling | Unit + API | Reject past/offset-free starts; accept equivalent explicit-offset instants; evaluate opening boundaries in dealership local time, including a DST example. | A-01, A-02 |
| T-11 Relationship and scope | API + SQL Server | Reject mismatched customer/vehicle, cross-dealership service/resource references, and retrieval through another dealership. | A-01, A-03 |
| T-13 Booking during Redis outage | API + SQL Server + Redis | Booking and retrieval remain correct while Redis is unavailable; final allocation uses SQL Server. | P-01 |
| T-14 Restart and database outage | Integration/smoke | Booking survives API restart; database outage prevents confirmation and fails readiness. | A-03, P-04 |
| T-16 Booking clean checkout | Local smoke | Documented initialization, migration, seed, startup, booking/retrieval, and concurrency checks work from a clean checkout using synthetic data and generated future times. | D-02, D-03, D-04, P-04 |

Supplement the existing T-series cases with the following ticket-local checks; retain the shared T-series IDs for cross-ticket traceability.

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| S3-V01 API contract | API + OpenAPI | Exact success fields, followable Location, scoped retrieval, UTC timestamps, malformed/missing input, and documented Problem Details codes/trace IDs. | S3-01, S3-04, S3-05 |
| S3-V02 Duration and read compatibility | API + SQL Server + Redis | A stale catalog cannot change booking duration; later service changes preserve stored snapshots; uncached availability observes the committed reservation. | S3-02, S3-05 |
| S3-V03 Alternative candidates | API + SQL Server | Controlled conflict on the first pair permits another free eligible pair within the configured limit; exhaustion follows documented bounds. | S3-03, S3-04 |
| S3-V04 Failure boundaries | API + SQL Server | Deadlock/lock/command timeout limits and cancellation release resources; unknown commit outcome is not blindly replayed; unexpected SQL errors are not reported as capacity conflicts. | S3-03, S3-04 |
| S3-V05 Direct-call validation parity | SQL Server | Runtime procedure calls without API checks enforce ownership, scope, duration, eligibility, opening/shift coverage, future-start policy, and both conflicts, including offset/DST boundary fixtures. | S3-02, S3-03 |
| S3-V06 Setup and recovery | SQL Server + local smoke | Fresh/repeated setup and ticket 2 upgrade retain data, apply exact permitted grants/denials, reject excessive privileges, and satisfy the documented recovery checks. | S3-03, S3-06 |

Use controllable time, explicit seeds, and coordination barriers for concurrency tests rather than arbitrary sleeps. Assert final database state as well as HTTP responses. Run concurrency tests with independent contexts/connections using the runtime principal, including competing API instances where practical. Cover an initially empty appointment table, lock waiting followed by a fresh overlap check, rollback releasing both resources, and bounded deadlock/timeout handling. EF's in-memory provider and SQLite cannot substitute for SQL Server locking, stored-procedure, and permission tests. Distinguish executed automated tests, manual inspection, and unexecuted cases in reports.

Record results in `docs/test/3-test.md` using [[test/README|Testing guidance]]. Cancellation, rescheduling, authentication, idempotency, and vehicle overlap protection remain separately scoped extensions. Telemetry infrastructure is outside this ticket's scope.
