---
title: "2 - Service Discovery and Availability: Availability and Scheduling Rules"
tags: [research, service-scheduler, availability, scheduling, sql]
ticket: 2
status: verified
updated: 2026-11-01
---

# 2 - Availability and scheduling rules

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research overview: [[research/2/00-overview|Ticket 2 research overview]]
- Requirements: S2-02, S2-03, S2-04; AC2-02, AC2-03; T-02, T-03, T-04, T-10, T-11; project requirement A-02.
- Evidence baseline: HEAD `3f8ab1e` (2026-11-01), clean working tree.

## Existing data and index evidence

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| Service duration is dealership-scoped and positive. | `ServiceTypes` entity/config; `CK_ServiceTypes_DurationMinutes` (`docs/database/schema.md:49-52`); seeds at `SeedData.cs:109-132`. | Duration is read from SQL Server; the client cannot supply it. |
| Opening hours are local weekday `time(0)` intervals with Sunday 0. | `DealershipOpeningHour` entity/config; PK `(DealershipId, DayOfWeek, OpensAt)`; `CK_DealershipOpeningHours_Interval` (`docs/database/schema.md:29,47-48`); seeds at `SeedData.cs:68-71,232-244`. | Evaluate local wall-clock coverage against a single row; overnight intervals are impossible (`ClosesAt > OpensAt`). |
| Technicians have dealership scope, active flag, qualifications, and UTC shifts. | `Technician`, `TechnicianQualification`, `TechnicianShift` configurations; seeds `SeedData.cs:134-198`; `FutureShiftSeeder.cs:20-91`. | Qualification and shift coverage are SQL predicates; shifts are UTC and may be split. |
| Bays have dealership scope and active flag; all seeded bays support all services. | `ServiceBay` config; seeds `SeedData.cs:200-230`; architecture bay rule. | Bays require active/dealership/conflict checks only, no qualification. |
| Appointments store UTC start/end, duration snapshot, and `Confirmed` status. | `Appointment` config; checks `CK_Appointments_Interval`, `CK_Appointments_DurationSnapshot`, `CK_Appointments_Status`, `CK_Appointments_UtcOffsets` (`docs/database/schema.md:52-56`). | Conflict predicate uses UTC half-open intervals and `Status`; test probes must satisfy the snapshot check. |
| Existing indexes cover resource/time lookups. | `IX_TechnicianShifts_TechnicianId_StartUtc` (`EndUtc` included), `IX_Technicians_DealershipId`, `IX_TechnicianQualifications_ServiceTypeId_DealershipId`, `IX_Appointments_DealershipId_TechnicianId_StartUtc` (`EndUtc, Status` included), `IX_Appointments_DealershipId_ServiceBayId_StartUtc` (`EndUtc, Status` included), opening-hours PK, `UX_ServiceTypes_DealershipId_Name`, `UX_ServiceBays_DealershipId_Name` (`docs/database/schema.md:58-66`). | No new index is expected; generated SQL captured on 2026-11-01 confirmed the predicates; execution-plan/index use remains an implementation check. |
| The runtime identity has only `CONNECT` and `SELECT ON SCHEMA::dbo`. | `RuntimePrincipalProvisioner.cs`; `RuntimePermissionTests`. | Availability/catalog reads use `scheduler_app`; no new grant. |
| No appointment write path exists in ticket 2. | Ticket 2 verification boundaries; ticket 3 owns the procedure. | Tests seed appointments through the privileged setup connection and assert read-only behavior. |

## Proposed availability algorithm

1. Capture `evaluatedAt = timeProvider.GetUtcNow()` exactly once for the request.
2. Parse and validate `startsAt` (Part 1): explicit `Z`/offset, invariant ISO-8601, strict future, normalized to UTC.
3. Read the dealership-scoped service duration and dealership IANA zone in one EF query. If no row exists (unknown dealership, unknown service, or service from another dealership), return `404 not_found`.
4. Compute `endsAt = startsAt + DurationMinutes` in UTC using checked tick arithmetic; overflow returns `400 validation_error`.
5. Resolve the dealership time zone. A missing/invalid IANA zone is an unexpected data/configuration failure and becomes a sanitized `500 internal_error` (covered by the 500 test).
6. Convert `startsAt`/`endsAt` to that zone and evaluate opening coverage:
   - require `localStart.Date == localEnd.Date` so the interval cannot span local midnight;
   - derive `TimeOnly` start/end values;
   - query one opening row for `(DealershipId, DayOfWeek)` where `OpensAt <= localStartTime` and `ClosesAt >= localEndTime`.
   - If no single row covers the interval, return `200` with `available:false` (the request is valid but the dealership is not open for the full interval).
7. Query one eligible technician:
   - active technician in the requested dealership;
   - qualified for the requested service type with the same dealership;
   - one shift (`StartUtc <= startsAt` and `EndUtc >= endsAt`) covers the entire interval;
   - no confirmed appointment for that technician with `StartUtc < endsAt && EndUtc > startsAt`;
   - order by name then ID and take the first candidate.
8. Query one eligible bay independently:
   - active bay in the requested dealership;
   - no confirmed appointment for that bay with `StartUtc < endsAt && EndUtc > startsAt`;
   - order by name then ID and take the first candidate.
9. Return `200` with `available = technicianExists && bayExists`, the requested `startsAt`/`endsAt`, and the captured `evaluatedAt`. Do not include resource IDs, do not write, and do not touch Redis.

A valid request with no opening coverage, no shift, no qualified/active resource, or a conflict returns `200 available:false`; only invalid input, missing/out-of-scope references, dependency failures, and unexpected faults use non-200 statuses.

## SQL query shapes and index mapping

### Service and dealership lookup

```csharp
var service = await (
        from serviceType in dbContext.ServiceTypes.AsNoTracking()
        join dealership in dbContext.Dealerships.AsNoTracking()
            on serviceType.DealershipId equals dealership.Id
        where serviceType.Id == serviceTypeId
              && serviceType.DealershipId == dealershipId
        select new { serviceType.DurationMinutes, dealership.TimeZoneId })
    .SingleOrDefaultAsync(cancellationToken);
```

This predicates on `ServiceTypes.Id` and `DealershipId`, using the primary key/alternate key and the dealership PK. A service belonging to another dealership produces no row and therefore `404`.

### Opening coverage

```csharp
var localStart = TimeZoneInfo.ConvertTime(startsAt, zone);
var localEnd = TimeZoneInfo.ConvertTime(endsAt, zone);
if (localStart.Date != localEnd.Date)
{
    return unavailable;
}

var startTime = TimeOnly.FromDateTime(localStart.DateTime);
var endTime = TimeOnly.FromDateTime(localEnd.DateTime);
var withinOpeningHours = await dbContext.DealershipOpeningHours
    .AsNoTracking()
    .AnyAsync(
        hours => hours.DealershipId == dealershipId
                 && hours.DayOfWeek == (byte)localStart.DayOfWeek
                 && hours.OpensAt <= startTime
                 && hours.ClosesAt >= endTime,
        cancellationToken);
```

The predicate uses the opening-hours PK and requires one row to cover the whole interval; adjacent/split rows are not combined. The comparison is local wall-clock; see the DST policy below.

### Technician eligibility and conflict

```csharp
var technicianId = await (
        from technician in dbContext.Technicians.AsNoTracking()
        join qualification in dbContext.TechnicianQualifications.AsNoTracking()
            on new { TechnicianId = technician.Id, DealershipId = dealershipId }
            equals new { qualification.TechnicianId, qualification.DealershipId }
        join shift in dbContext.TechnicianShifts.AsNoTracking()
            on technician.Id equals shift.TechnicianId
        where technician.DealershipId == dealershipId
              && technician.IsActive
              && qualification.ServiceTypeId == serviceTypeId
              && shift.StartUtc <= startsAt
              && shift.EndUtc >= endsAt
              && !dbContext.Appointments.Any(appointment =>
                  appointment.DealershipId == dealershipId
                  && appointment.TechnicianId == technician.Id
                  && appointment.Status == AppointmentStatuses.Confirmed
                  && appointment.StartUtc < endsAt
                  && appointment.EndUtc > startsAt)
        orderby technician.Name, technician.Id
        select (Guid?)technician.Id)
    .FirstOrDefaultAsync(cancellationToken);
```

Each predicate maps to an existing index: technician dealership (`IX_Technicians_DealershipId`), qualification by technician/service/dealership (PK plus `IX_TechnicianQualifications_ServiceTypeId_DealershipId`), shift by technician/start with `EndUtc` included (`IX_TechnicianShifts_TechnicianId_StartUtc`), and confirmed appointments by dealership/technician/start with `EndUtc`/`Status` included (`IX_Appointments_DealershipId_TechnicianId_StartUtc`). The `!Any` becomes `NOT EXISTS`; no appointment rows are materialized.

### Bay eligibility and conflict

```csharp
var bayId = await (
        from bay in dbContext.ServiceBays.AsNoTracking()
        where bay.DealershipId == dealershipId
              && bay.IsActive
              && !dbContext.Appointments.Any(appointment =>
                  appointment.DealershipId == dealershipId
                  && appointment.ServiceBayId == bay.Id
                  && appointment.Status == AppointmentStatuses.Confirmed
                  && appointment.StartUtc < endsAt
                  && appointment.EndUtc > startsAt)
        orderby bay.Name, bay.Id
        select (Guid?)bay.Id)
    .FirstOrDefaultAsync(cancellationToken);
```

The bay query is independent of the technician query. It uses `UX_ServiceBays_DealershipId_Name` for dealership/order and `IX_Appointments_DealershipId_ServiceBayId_StartUtc` for the conflict check.

### Generated SQL verification (2026-11-01)

A temporary probe outside the repository built the proposed EF Core queries against `SchedulerDbContext` with the EF Core 10.0.13 SQL Server provider and printed `ToQueryString()`. No database was queried and no execution plan was captured.

| Query | Observed generated SQL (abridged) | Verified predicate |
| --- | --- | --- |
| Service/dealership | `SELECT [s].[DurationMinutes], [d].[TimeZoneId] FROM [dbo].[ServiceTypes] AS [s] INNER JOIN [dbo].[Dealerships] AS [d] ON [s].[DealershipId] = [d].[Id] WHERE [s].[Id] = @serviceTypeId AND [s].[DealershipId] = @dealershipId` | Dealership-scoped duration and IANA zone; cross-dealership service yields no row. |
| Opening coverage | `SELECT [d].[OpensAt] FROM [dbo].[DealershipOpeningHours] AS [d] WHERE [d].[DealershipId] = @dealershipId AND [d].[DayOfWeek] = @localDayOfWeek AND [d].[OpensAt] <= @localStartTime AND [d].[ClosesAt] >= @localEndTime` | One opening row covers the local interval; no row materialization. |
| Technician | `SELECT TOP(@p) [t].[Id] ... WHERE [t].[DealershipId] = @dealershipId AND [t].[IsActive] = CAST(1 AS bit) AND [t0].[ServiceTypeId] = @serviceTypeId AND [t1].[StartUtc] <= @startsAt AND [t1].[EndUtc] >= @endsAt AND NOT EXISTS (... [a].[Status] = N'Confirmed' AND [a].[StartUtc] < @endsAt AND [a].[EndUtc] > @startsAt) ORDER BY [t].[Name], [t].[Id]` | Active, qualified, one shift covering the full interval, no half-open confirmed conflict; bounded `TOP(1)` and stable order. |
| Bay | `SELECT TOP(@p) [s].[Id] FROM [dbo].[ServiceBays] AS [s] WHERE [s].[DealershipId] = @dealershipId AND [s].[IsActive] = CAST(1 AS bit) AND NOT EXISTS (... [a].[ServiceBayId] = [s].[Id] AND [a].[Status] = N'Confirmed' AND [a].[StartUtc] < @endsAt AND [a].[EndUtc] > @startsAt) ORDER BY [s].[Name], [s].[Id]` | Independent bay conflict check, dealership scope, stable order. |

The observed SQL confirms provider translation and the required predicates. Index-use/execution-plan confirmation on representative data remains an implementation check; the generated statements match the selected index inventory by predicate and key order.

### Stable order and independence

- Order technicians by `Name`, then `Id`, and bays by `Name`, then `Id`. The response does not expose the chosen IDs, but the order is deterministic for future booking reuse and test expectations.
- Technician and bay conflicts are evaluated independently. A technician conflict does not imply a bay conflict, and vice versa; a free pair whose resources differ from existing appointments remains available.
- The endpoint accepts no resource IDs, so foreign-dealership resources are excluded by the dealership predicates rather than by request parameters.

### Half-open interval truth table

| Existing appointment | Requested interval | Overlap? |
| --- | --- | --- |
| `[09:00, 10:00)` | `[10:00, 11:00)` | No — adjacent is allowed. |
| `[09:30, 10:30)` | `[10:00, 11:00)` | Yes — partial overlap. |
| `[09:00, 12:00)` | `[10:00, 11:00)` | Yes — requested is contained. |
| `[10:00, 11:00)` | `[10:00, 11:00)` | Yes — identical. |
| `[09:00, 11:00)` | `[10:00, 10:30)` | Yes — existing contains requested. |

The SQL predicate is always `existing.StartUtc < requested.EndUtc AND existing.EndUtc > requested.StartUtc`, with `Status = Confirmed`.

## Opening hours, shifts, and DST policy

### Coverage semantics

- Opening coverage is evaluated in dealership-local wall-clock terms: the UTC instants are converted to the dealership zone, the local calendar date must be the same for start and end, and one configured opening row must satisfy `OpensAt <= localStartTime` and `ClosesAt >= localEndTime`.
- Shift coverage is evaluated in UTC: one shift row must satisfy `StartUtc <= startsAt` and `EndUtc >= endsAt`.
- An end exactly at closing/shift end is allowed; a start exactly at opening/shift start is allowed.
- A single row must cover the full interval. Two adjacent shifts, two adjacent opening rows, or a split shift that covers only part of the interval do not satisfy coverage.
- Opening hours and shifts cannot span local midnight through this rule: opening rows are same-day (`ClosesAt > OpensAt`), and the same-local-date check rejects cross-day intervals.
- Holidays are outside the initial schema and are not evaluated; the weekday opening rows describe the recurring local operating calendar only. Do not add a holiday calendar as part of ticket 2.

### Deterministic DST policy

The API does not construct local opening boundaries as UTC instants and does not accept local-time booking input. It converts the requested UTC instants to local wall-clock values and compares `TimeOnly` values. This makes the policy deterministic:

- **Fall-back repeated hour:** a local wall-clock time that occurs twice is compared identically in both occurrences. The UTC shift and conflict checks remain authoritative for real elapsed availability.
- **Spring-forward nonexistent local time:** a requested UTC instant never converts to a nonexistent local time. If a configured opening boundary itself lies in the gap, it is compared as the stored wall-clock value. For example, an opening row `01:00-04:00` on a spring-forward day accepts a request whose local start/end are `02:30-04:00`; the interval is treated as the configured wall-clock window even though `01:00` did not occur.
- The policy avoids `TimeZoneInfo.ConvertTimeToUtc` ambiguity for local boundary values and is documented in architecture/cache/availability docs during implementation.

Research-phase probe on 2026-11-01 confirmed the fixture arithmetic with .NET 10 and the host IANA database: `2027-03-28T01:30Z` converted to `02:30+01:00` and `03:00Z` to `04:00+01:00`; `2027-10-31T00:30Z` converted to `01:30+01:00` and `02:00Z` to `02:00+00:00`. These are conversion facts, not executed API/DST tests.

### DST verification fixtures

| Case | Fixture | Request | Expected |
| --- | --- | --- | --- |
| Spring-forward after the gap | London (`Europe/London`) Sunday 2027-03-28; add Sunday opening `02:00-04:00`; add active technician shift `2027-03-28T01:00:00Z`-`03:00:00Z`; active London bay; annual service 90 min | `startsAt=2027-03-28T01:30:00Z` | `200`, `available:true`; local interval `02:30-04:00`; end exactly at closing/shift end. |
| Nonexistent opening boundary | Same day; opening `01:00-04:00`; same shift | `startsAt=2027-03-28T01:30:00Z` | `200`, `available:true` per wall-clock policy; document that `01:00` is a spring-forward gap boundary. |
| Fall-back repeated hour | London Sunday 2027-10-31; add Sunday opening `00:00-04:00`; add shift `2027-10-31T00:00:00Z`-`02:00:00Z`; annual service 90 min | `startsAt=2027-10-31T00:30:00Z` | `200`, `available:true`; local start `01:30 BST`, local end `02:00 GMT` after 90 minutes; shift covers the UTC interval and the end equals shift end. |
| Fall-back outside window | Same fall-back fixture | `startsAt=2027-10-31T03:00:00Z` | `200`, `available:false`; local end is after opening and the shift ends at `02:00Z`. |

The API test inserts the Sunday opening/shift rows through the privileged setup connection in its own class database and removes them afterward. Unit tests cover the same policy with `TimeZoneInfo.FindSystemTimeZoneById` without SQL.

## Failure behavior for availability reads

- SQL unavailability (connection failure, timeouts, classified deadlock/lock timeout) maps to `503 dependency_unavailable`; it must never return `200 available:false`.
- A time-zone resolution failure from unexpected data/configuration is a sanitized `500 internal_error`; it is not a capacity/availability result.
- Request cancellation propagates; it is not converted into `503`.
- No cache is consulted, so availability remains correct during a Redis outage/stall.
- The endpoint performs no writes and creates no reservation; a subsequent booking may still fail if capacity changes (BR-08/AS-07). The response does not promise capacity.

## Database impact and migration decision

**Database impact: none expected.** Ticket 2 reads existing tables and uses existing indexes; it adds no columns, keys, checks, relationships, seeds, procedures, or grants. The required index predicates map as follows:

| Query | Predicate | Supporting index/object |
| --- | --- | --- |
| Service/dealership | `ServiceTypes.Id`, `ServiceTypes.DealershipId`; join `Dealerships.Id` | `PK_ServiceTypes`, `AK_ServiceTypes_Id_DealershipId`, `PK_Dealerships` |
| Opening hours | `DealershipId`, `DayOfWeek`, `OpensAt`, `ClosesAt` | `PK_DealershipOpeningHours` |
| Technicians | `DealershipId`, `IsActive` | `IX_Technicians_DealershipId`, `PK_Technicians` |
| Qualifications | `TechnicianId`, `ServiceTypeId`, `DealershipId` | `PK_TechnicianQualifications`, composite FKs, `IX_TechnicianQualifications_ServiceTypeId_DealershipId` |
| Shifts | `TechnicianId`, `StartUtc <= startsAt`, `EndUtc >= endsAt` | `IX_TechnicianShifts_TechnicianId_StartUtc` (`EndUtc` included) |
| Bays | `DealershipId`, `IsActive`, order by name | `UX_ServiceBays_DealershipId_Name` |
| Technician conflicts | `DealershipId`, `TechnicianId`, `StartUtc < end`, `EndUtc > start`, `Status` | `IX_Appointments_DealershipId_TechnicianId_StartUtc` (`EndUtc, Status` included) |
| Bay conflicts | `DealershipId`, `ServiceBayId`, `StartUtc < end`, `EndUtc > start`, `Status` | `IX_Appointments_DealershipId_ServiceBayId_StartUtc` (`EndUtc, Status` included) |

Required implementation check: capture the EF-generated SQL (`ToQueryString` or command logging) for both eligibility queries and the opening query, review that the half-open predicates, `Status`, and dealership scope are present, and document the index mapping. Add a migration only if a concrete missing index is proven on representative synthetic data; if so, follow [[database/migration]] and update schema/deploy records. No migration is planned for ticket 2.

Runtime permissions remain unchanged: the API uses `scheduler_app` with `SELECT` on `dbo`; tests insert appointments and custom resources through the privileged setup connection. Direct runtime appointment writes remain denied.

## Proposed unit-level rules

Keep the following pure calculations independently testable; SQL remains authoritative for reservation/eligibility checks:

| Rule | Responsibility | Unit tests |
| --- | --- | --- |
| `StartsAtParser` | Trim, require explicit offset, parse invariant, normalize UTC | `Z`, positive/negative offsets, offset-free, invalid, date-only, whitespace. |
| `AvailabilityInterval.Create` | Strict future, checked duration addition, end overflow | Equal clock, past, positive duration, max-range overflow. |
| `OpeningCoverage.IsCovered` | Same local date, wall-clock start/end within one row, end boundary | Normal day, before/after hours, equal boundaries, cross-midnight, DST spring/fall fixtures. |
| `ShiftCoverage.IsCovered` | UTC start/end within one shift, end boundary | Before/after, exact boundaries, adjacent shifts not combined, split shift. |
| `HalfOpenOverlap.Overlaps` | Existing start < requested end && existing end > requested start | Truth table above. |
| `AvailabilityResult` composition | `available = technicianExists && bayExists` | Both, technician-only, bay-only, neither. |

## Test data design for availability

Use the fixed test clock `2026-11-02T00:00:00Z` (`SqlServerContainerFixture.cs:20`) and the seeded 28-day shift horizon. Saigon's seeded weekday shift is 09:00-17:00 local (`02:00-10:00Z` in November); London is 09:00-17:00 local (`09:00-17:00Z`). Use Saigon for normal cases and London for DST cases.

| Scenario | Setup via privileged setup connection | Request | Expected |
| --- | --- | --- | --- |
| Qualified/active | Seeded Saigon Oil Change (60 min), active technician/bay, no appointments | `2026-11-03T03:00:00Z` (10:00 local) | `available:true`; `endsAt=04:00Z`. |
| Unqualified | Add an active Saigon technician without the service qualification; block the seeded active technician with an appointment; bay free | Same | `available:false`; unqualified technician not used. |
| Inactive technician/bay | Block seeded active technician and bay; seeded inactive technician is qualified, inactive bay exists | Same | `available:false`. |
| Partial shift | Add active qualified technician with a shift ending mid-interval; block seeded technician; bay free | 10:00-11:00 local | `available:false`; a single shift does not cover the end. |
| Partial opening | Add a Sunday opening row ending mid-interval plus a covering shift; active technician/bay | Sunday 10:00-11:00 local | `available:false`; opening does not cover the end. |
| Technician-only conflict | Add a second active bay; appointment uses active technician + second bay | Requested interval overlaps | `available:false`; bay is free but technician conflicts. |
| Bay-only conflict | Add a second active qualified technician; appointment uses that technician + active bay | Requested interval overlaps | `available:false`; technician free but bay conflicts. |
| Free pair differs from appointments | Appointment uses second technician + second bay; seeded active technician/bay free | Requested interval overlaps the appointment | `available:true`. |
| Adjacent | Appointment uses active technician + active bay, ending at requested start | Request starts exactly at appointment end | `available:true`. |
| Partial/contained/identical | Appointment uses active technician + active bay | Requested interval overlaps | `available:false` for either resource. |
| Foreign dealership | Block Saigon active technician/bay; London resources are free and cannot be qualified for Saigon service | Saigon service | `available:false`; no foreign resource satisfies. |
| No eligible pair | Block both seeded active resources; leave no other eligible pair | Valid interval | `200`, `available:false`. |
| Read-only | Count appointments before/after availability; inspect Redis keys | Any valid request | Counts unchanged; no Redis key created. |
| Unqualified/inactive direct check | Unit-level pure rule tests | N/A | Candidate excluded. |

All probe rows use `Guid.NewGuid()` and cleanup removes only the test's own rows. Appointment probes include a positive duration, UTC offsets, and `EndUtc = DATEADD(minute, DurationMinutes, StartUtc)` to satisfy the schema checks. Test methods in a class run sequentially; each class has its own database. To block only the technician, add a second active bay and an appointment using the seeded technician plus that second bay; to block only the bay, add a second active qualified technician and an appointment using that technician plus the active bay. This keeps the independent technician-only and bay-only conflict cases unambiguous.

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement | Status |
| --- | --- | --- | --- | --- | --- |
| B-01 | Decision | Should opening coverage be evaluated in SQL or memory? | SQL `Any` query with local `TimeOnly` parameters, after C# conversion; no opening rows loaded. | S2-02, S2-03 | Resolved and verified 2026-11-01: generated SQL contains the dealership/day/opening predicates; API test deferred. |
| B-02 | Decision | What is the DST/ambiguous-boundary policy? | Wall-clock local comparison with same-local-date requirement; repeated times identical; nonexistent boundaries compared as stored; shifts UTC. | S2-02, T-10 | Resolved and conversion-verified 2026-11-01 (see the DST note); API/DST tests deferred to TC2-23. |
| B-03 | Decision | How to select candidates? | One eligible technician and one eligible bay, each ordered by name/ID; independent `NOT EXISTS` conflict predicates. | S2-03 | Resolved and verified 2026-11-01: generated SQL uses `TOP(1)`, name/ID ordering, and independent `NOT EXISTS`; API tests deferred. |
| B-04 | Risk | Existing indexes may not be used by generated SQL. | Capture generated SQL and review predicates; use representative synthetic volume if needed; add a migration only if a gap is proven. | S2-03, AC2-02 | Partially verified 2026-11-01: generated SQL confirms all predicates; execution-plan/index-use confirmation on representative data remains implementation-time. No migration planned. |
| B-05 | Risk | Test probes may violate appointment snapshot/FK checks. | Use the existing `ConstraintTests` insert pattern, positive duration, UTC offsets, `DATEADD` equality, seeded/added consistent resources. | S2-03, AC2-02 | Deferred: implementation tests use the existing insert pattern; no pre-implementation execution. |
| B-06 | Risk | A bad seeded IANA zone could break availability. | Setup resolves zones for dealerships with active technicians and fails on an invalid zone; availability treats an unresolved zone as unexpected `500`. The test inserts a temporary invalid-zone dealership plus a service type (otherwise the endpoint returns `404` before zone resolution). | S2-04, AC2-06 | Corrected and deferred to TC2-29. |
| B-07 | Decision | Should a cross-midnight interval be unavailable or invalid? | `200 available:false` when local dates differ; AS-06 excludes overnight opening, so no valid row can cover it. | S2-02, S2-03 | Resolved; API test deferred. |
| B-08 | Decision | Are adjacent opening/shift rows combined? | No; a single row must cover the interval. | S2-02, S2-03 | Resolved; API tests deferred. |
| B-09 | Decision | Does availability cache or write? | No Redis, no appointment writes, no duration snapshot; response is advisory. | S2-04, AC2-03 | Resolved; state assertions deferred to TC2-21. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S2-02 | Strict future, explicit offset equivalence, UTC/local conversion, end derivation, overflow, DST spring/fall. | Unit + API |
| S2-03 | Active/qualified, shift/opening full coverage, single-row coverage, independent technician/bay conflicts, half-open boundaries, dealership scope, stable order, SQL-side predicates. | Unit + API + SQL Server |
| S2-04 | No writes/reservations, no cache, `available:false` for valid no-pair, SQL outage `503` not false availability. | API + SQL Server |
| AC2-02 | T-02/T-03/T-04/T-10/T-11 pass. | Unit + API + SQL Server |
| AC2-03 | Both response outcomes, SQL duration with stale cache, no mutation. | API + SQL Server + Redis |
| T-02/T-03/T-04/T-10/T-11 | Scenario matrix above. | As listed |

Research-phase probes on 2026-11-01 verified the DST conversion arithmetic and the generated SQL predicates; all ticket-level unit/API/SQL checks are **Not run**. Detailed test file/command assignments are in [[research/2/04-verification-and-documentation|Part 4]].
