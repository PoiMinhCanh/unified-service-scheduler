---
title: "3 - Appointment Booking and Retrieval: Procedure and Concurrency"
tags: [research, service-scheduler, booking, sql, concurrency, time-zone]
ticket: 3
status: verified
updated: 2026-11-01
---

# 3 - Booking procedure and concurrency

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research overview: [[research/3/00-overview|Ticket 3 research overview]]
- Requirements: S3-02, S3-03, S3-04; AC3-02-AC3-06, AC3-11, AC3-12; T-02-T-10, T-13; S3-V02-S3-V05; project requirements A-02, A-03, P-01.
- Acceptance labels: `AC3-01`-`AC3-13` are research-local mappings to the ticket's 13 unlabeled acceptance bullets, defined in [[research/3/00-overview|Overview 0.2]]; they are not new ticket IDs.
- Evidence baseline: HEAD `90a6284` plus the staged 2026-11-01 ticket update; inspected appointment schema/indexes, initial migration, availability queries/rules, SQL error classifier, `UseSqlServer` configuration, and setup components. Research-phase probes used the pinned SQL Server 2022 CU23 container and a temporary `Microsoft.Data.SqlClient` 6.1.7 project.

> [!IMPORTANT]
> The procedure, time-zone mapping, allocator, and retry policy do not exist. The SQL below is a proposed contract, not inspected application code. Probe results are behavior evidence from isolated synthetic objects; they are not ticket tests.

## Existing evidence

| Fact | Inspected evidence | Consequence |
| --- | --- | --- |
| Appointments persist dealer/customer/vehicle/service/technician/bay IDs, UTC start/end, duration snapshot, `Confirmed` status, and creation time. | `Infrastructure/Persistence/Entities/Appointment.cs`; `AppointmentConfiguration.cs`. | The procedure inserts the existing shape; no appointment column change. |
| Checks enforce positive duration, end > start, `EndUtc = DATEADD(minute, DurationMinutes, StartUtc)`, `Status = Confirmed`, and zero UTC offsets. | `AppointmentConfiguration.cs` lines 13-22; initial migration lines 180-184. | Procedure duration/end/status/offsets must satisfy these checks exactly. |
| Composite FKs enforce vehicle/customer, service/dealership, technician/dealership, and bay/dealership consistency. | `AppointmentConfiguration.cs`; initial migration lines 185-220. | The procedure must validate the same relationships before insert; bad data still fails. |
| Resource conflict indexes exist: `IX_Appointments_DealershipId_TechnicianId_StartUtc` and `IX_Appointments_DealershipId_ServiceBayId_StartUtc`, both including `EndUtc, Status`. | `AppointmentConfiguration.cs` lines 78-84; initial migration lines 374-386. | No new appointment index is expected. |
| Technician qualification PK and shift index support eligibility; opening-hours PK supports coverage. | `TechnicianQualificationConfiguration.cs`, `TechnicianShiftConfiguration.cs`, `DealershipOpeningHourConfiguration.cs`. | The procedure predicates map to existing indexes. |
| Availability already uses dealership-scoped duration, active/qualified technician, single-shift coverage, active bay, and independent half-open `NOT EXISTS` conflicts in name/ID order. | `AvailabilityService.cs`, `AvailabilityQueries.cs`, `ShiftCoverage.cs`, `HalfOpenOverlap.cs`, `OpeningCoverage.cs`. | Booking can reuse query shapes/prechecks but must revalidate inside the procedure. |
| `SqlFailureClassifier` recognizes connectivity/timeout/deadlock numbers including `1205`, `1222`, `-2`. | `Infrastructure/Errors/SqlFailureClassifier.cs`. | Known procedure errors must be handled before the global 503 classifier. |
| `UseSqlServer` is configured without `EnableRetryOnFailure`; the design-time factory is also non-retrying. | `Program.cs` lines 87-90; `SchedulerDbContextFactory.cs`. | There is no automatic EF write replay today; do not add one for booking. |
| A new test database defaults to `is_read_committed_snapshot_on = 0` and `snapshot_isolation_state = OFF`; the procedure will set `SERIALIZABLE` explicitly. | SQL probe on 2026-11-01. | Use explicit isolation; do not rely on database defaults or `NOLOCK`. |
| No procedure, time-zone mapping, or runtime `EXECUTE` exists. | Initial migration; `RuntimePrincipalProvisioner.cs`; `Program.cs`. | Proposed additions. |

## Proposed procedure contract

### Name and parameters

Proposed name: `dbo.CreateAppointment` (same-owner, static, no dynamic SQL).

| Parameter | Direction | SQL type | Source / meaning |
| --- | --- | --- | --- |
| `@DealershipId` | Input | `uniqueidentifier` | Route dealership; required. |
| `@CustomerId` | Input | `uniqueidentifier` | Request customer; must exist. |
| `@VehicleId` | Input | `uniqueidentifier` | Request vehicle; must belong to `@CustomerId`. |
| `@ServiceTypeId` | Input | `uniqueidentifier` | Service type; must belong to `@DealershipId`. |
| `@TechnicianId` | Input | `uniqueidentifier` | Candidate technician; must belong to the dealership, be active, and be qualified. |
| `@ServiceBayId` | Input | `uniqueidentifier` | Candidate bay; must belong to the dealership and be active. |
| `@StartUtc` | Input | `datetimeoffset(7)` | Requested start; normalized to UTC inside the procedure. |
| `@EvaluatedAtUtc` | Input | `datetimeoffset(7)` | API-owned evaluation instant for the future-start check; direct callers supply an explicit instant. |
| `@AppointmentId` | Output | `uniqueidentifier` | Generated with `NEWID()` and inserted. |
| `@EndUtc` | Output | `datetimeoffset(7)` | `DATEADD(minute, DurationMinutes, StartUtc)`; inserted. |
| `@DurationMinutes` | Output | `int` | SQL service duration snapshot; inserted. |
| `@CreatedUtc` | Output | `datetimeoffset(7)` | UTC server creation time; inserted. |

The procedure returns no result set; callers read the output parameters after commit. The API builds the response from known inputs plus these outputs. A result set is not required and would add mapping complexity.

### Error numbers

| Number | Name / meaning | API mapping | Retry behavior |
| --- | --- | --- | --- |
| `51001` | Technician conflict: a confirmed appointment overlaps the requested interval for the technician. | Candidate retry; `409 capacity_unavailable` when exhausted. | Try another pair. |
| `51002` | Bay conflict: a confirmed appointment overlaps the requested interval for the bay. | Candidate retry; `409` when exhausted. | Try another pair. |
| `51003` | Invalid booking request: missing/null inputs, past start, missing customer/vehicle/service, ownership mismatch, invalid service duration, interval overflow, or a caller-owned transaction. | `400 validation_error`. | No candidate retry; another pair cannot fix it. |
| `51004` | Resource not eligible: missing/inactive technician or bay, or technician not qualified for the service/dealership. | Try next pair; `409` when exhausted. | Pair/resource retry within the attempt limit. |
| `51005` | Interval coverage unavailable: opening or technician shift does not cover the full interval, or the interval spans local dates. | Try next pair; `409` when exhausted. | Pair retry within the attempt limit. |
| `51006` | SQL time-zone mapping unavailable/invalid: no mapping for the dealership's IANA ID, or `AT TIME ZONE` rejects the Windows ID. | `500 internal_error`; sanitized. | No retry. |
| `1205` | Deadlock victim (SQL Server rolled back the transaction). | Bounded retry; `503 dependency_unavailable` after exhaustion. | Retry the whole attempt. |
| `1222` | Lock timeout. | `503 dependency_unavailable`. | No retry. |
| `-2` | Command/client timeout. | `503 dependency_unavailable`. | No retry. |
| Classified connectivity/timeout numbers | Existing `SqlFailureClassifier` set. | `503 dependency_unavailable`. | No retry. |
| Other SQL errors | Unexpected/fatal. | Sanitized `500 internal_error`. | No retry. |

Messages are short and sanitized; the API does not expose them to clients. The booking boundary logs the SQL number/context without request bodies, VINs, or customer contact details.

### Proposed procedure body

The actual migration SQL will be reviewed and tested; this is the contract-level shape.

```sql
CREATE PROCEDURE dbo.CreateAppointment
    @DealershipId uniqueidentifier,
    @CustomerId uniqueidentifier,
    @VehicleId uniqueidentifier,
    @ServiceTypeId uniqueidentifier,
    @TechnicianId uniqueidentifier,
    @ServiceBayId uniqueidentifier,
    @StartUtc datetimeoffset(7),
    @EvaluatedAtUtc datetimeoffset(7),
    @AppointmentId uniqueidentifier OUTPUT,
    @EndUtc datetimeoffset(7) OUTPUT,
    @DurationMinutes int OUTPUT,
    @CreatedUtc datetimeoffset(7) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT > 0
        THROW 51003, 'The booking procedure must own its transaction.', 1;

    IF @DealershipId IS NULL OR @CustomerId IS NULL OR @VehicleId IS NULL
       OR @ServiceTypeId IS NULL OR @TechnicianId IS NULL OR @ServiceBayId IS NULL
       OR @StartUtc IS NULL OR @EvaluatedAtUtc IS NULL
        THROW 51003, 'Required booking inputs are missing.', 1;

    SET @StartUtc = SWITCHOFFSET(@StartUtc, 0);
    SET @EvaluatedAtUtc = SWITCHOFFSET(@EvaluatedAtUtc, 0);
    IF @StartUtc <= @EvaluatedAtUtc
        THROW 51003, 'The requested start must be in the future.', 1;

    SET LOCK_TIMEOUT 5000; -- 5 seconds; command timeout is 10 seconds
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
    BEGIN TRANSACTION;

    BEGIN TRY
        DECLARE @TechnicianActive bit;
        SELECT @TechnicianActive = t.IsActive
        FROM dbo.Technicians AS t WITH (UPDLOCK, HOLDLOCK)
        WHERE t.Id = @TechnicianId AND t.DealershipId = @DealershipId;
        IF @TechnicianActive IS NULL OR @TechnicianActive = 0
            THROW 51004, 'The technician is not active for this dealership.', 1;

        DECLARE @BayActive bit;
        SELECT @BayActive = b.IsActive
        FROM dbo.ServiceBays AS b WITH (UPDLOCK, HOLDLOCK)
        WHERE b.Id = @ServiceBayId AND b.DealershipId = @DealershipId;
        IF @BayActive IS NULL OR @BayActive = 0
            THROW 51004, 'The service bay is not active for this dealership.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId)
            THROW 51003, 'The customer was not found.', 1;
        IF NOT EXISTS (
            SELECT 1 FROM dbo.Vehicles
            WHERE Id = @VehicleId AND CustomerId = @CustomerId)
            THROW 51003, 'The vehicle does not belong to the customer.', 1;

        DECLARE @ServiceDuration int;
        SELECT @ServiceDuration = st.DurationMinutes
        FROM dbo.ServiceTypes AS st
        WHERE st.Id = @ServiceTypeId AND st.DealershipId = @DealershipId;
        IF @ServiceDuration IS NULL OR @ServiceDuration <= 0
            THROW 51003, 'The service type is invalid for this dealership.', 1;
        SET @DurationMinutes = @ServiceDuration;

        IF DATEDIFF_BIG(
                minute,
                @StartUtc,
                CONVERT(datetimeoffset(7), '9999-12-31T23:59:59.9999999+00:00')) < @DurationMinutes
            THROW 51003, 'The service interval is outside the supported range.', 1;
        SET @EndUtc = DATEADD(minute, @DurationMinutes, @StartUtc);

        DECLARE @IanaId nvarchar(100), @WindowsId nvarchar(100);
        SELECT @IanaId = d.TimeZoneId FROM dbo.Dealerships AS d WHERE d.Id = @DealershipId;
        IF @IanaId IS NULL
            THROW 51003, 'The dealership was not found.', 1;
        SELECT @WindowsId = m.WindowsId
        FROM dbo.TimeZoneMappings AS m
        WHERE m.IanaId = @IanaId;
        IF @WindowsId IS NULL
            THROW 51006, 'No SQL time-zone mapping exists for the dealership.', 1;

        DECLARE @LocalStart datetimeoffset(7), @LocalEnd datetimeoffset(7);
        BEGIN TRY
            SET @LocalStart = @StartUtc AT TIME ZONE @WindowsId;
            SET @LocalEnd = @EndUtc AT TIME ZONE @WindowsId;
        END TRY
        BEGIN CATCH
            THROW 51006, 'The SQL time-zone mapping is invalid.', 1;
        END CATCH;

        IF CAST(@LocalStart AS date) <> CAST(@LocalEnd AS date)
            THROW 51005, 'The interval must fit within one local opening interval.', 1;

        DECLARE @LocalStartTime time(7) = CAST(@LocalStart AS time(7));
        DECLARE @LocalEndTime time(7) = CAST(@LocalEnd AS time(7));
        DECLARE @LocalDay tinyint = CAST(
            ((DATEDIFF(day, CONVERT(date, '19000107', 112), CAST(@LocalStart AS date)) % 7) + 7) % 7
            AS tinyint);

        IF NOT EXISTS (
            SELECT 1 FROM dbo.DealershipOpeningHours AS h
            WHERE h.DealershipId = @DealershipId
              AND h.DayOfWeek = @LocalDay
              AND h.OpensAt <= @LocalStartTime
              AND h.ClosesAt >= @LocalEndTime)
            THROW 51005, 'The interval is outside the dealership opening hours.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM dbo.TechnicianQualifications AS q
            WHERE q.TechnicianId = @TechnicianId
              AND q.ServiceTypeId = @ServiceTypeId
              AND q.DealershipId = @DealershipId)
            THROW 51004, 'The technician is not qualified for the service.', 1;

        IF NOT EXISTS (
            SELECT 1 FROM dbo.TechnicianShifts AS s
            WHERE s.TechnicianId = @TechnicianId
              AND s.StartUtc <= @StartUtc
              AND s.EndUtc >= @EndUtc)
            THROW 51005, 'The interval is outside the technician shift.', 1;

        IF EXISTS (
            SELECT 1 FROM dbo.Appointments AS a
            WHERE a.DealershipId = @DealershipId
              AND a.TechnicianId = @TechnicianId
              AND a.Status = N'Confirmed'
              AND a.StartUtc < @EndUtc
              AND a.EndUtc > @StartUtc)
            THROW 51001, 'The technician has a conflicting appointment.', 1;

        IF EXISTS (
            SELECT 1 FROM dbo.Appointments AS a
            WHERE a.DealershipId = @DealershipId
              AND a.ServiceBayId = @ServiceBayId
              AND a.Status = N'Confirmed'
              AND a.StartUtc < @EndUtc
              AND a.EndUtc > @StartUtc)
            THROW 51002, 'The service bay has a conflicting appointment.', 1;

        SET @AppointmentId = NEWID();
        SET @CreatedUtc = CAST(SYSUTCDATETIME() AS datetimeoffset(7));

        INSERT INTO dbo.Appointments (
            Id, DealershipId, CustomerId, VehicleId, ServiceTypeId, TechnicianId, ServiceBayId,
            StartUtc, EndUtc, DurationMinutes, Status, CreatedUtc)
        VALUES (
            @AppointmentId, @DealershipId, @CustomerId, @VehicleId, @ServiceTypeId, @TechnicianId, @ServiceBayId,
            @StartUtc, @EndUtc, @DurationMinutes, N'Confirmed', @CreatedUtc);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET LOCK_TIMEOUT -1;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
        SET XACT_ABORT OFF;
        THROW;
    END CATCH;

    SET LOCK_TIMEOUT -1;
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
    SET XACT_ABORT OFF;
END;
```

Implementation notes:

- The procedure owns the transaction and rejects a caller-owned transaction (`@@TRANCOUNT > 0`). The API calls it on a dedicated connection; direct callers/tests must not wrap it in a transaction.
- Output parameters are assigned from validated local variables after the corresponding row is found; do not rely on `SELECT @output = ...` leaving a caller-supplied value when no row exists. `@DurationMinutes` in particular is assigned from `@ServiceDuration` only after the positive-duration check.
- `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE` is set before `BEGIN TRANSACTION`; the procedure resets to `READ COMMITTED` on normal and error exits. `SET LOCK_TIMEOUT` is connection-scoped and reset to `-1`; the booking boundary uses a dedicated connection per attempt so pooled session settings do not leak into other work. The final migration SQL should place early validation inside the same `TRY`/`CATCH` (or otherwise guarantee reset) so an early `THROW` cannot leave `XACT_ABORT`/`LOCK_TIMEOUT` set on a pooled session.
- `UPDLOCK, HOLDLOCK` is applied to the technician row first, then the bay row, before eligibility/coverage/conflict revalidation. The resource-row locks serialize writers for the same resource; `SERIALIZABLE` range locks protect the conflict queries, including an initially empty appointment table.
- The conflict predicates are exactly `existing.StartUtc < requested.EndUtc AND existing.EndUtc > requested.StartUtc` with `Status = Confirmed`. Adjacent intervals do not conflict.
- The opening query uses `time(7)` local values against `time(0)` columns; SQL type precedence preserves fractional-second comparisons instead of rounding to `time(0)`.
- The local weekday expression uses `1900-01-07` (Sunday) so `0` = Sunday through `6` = Saturday independently of `DATEFIRST`. The proposed expression is `((DATEDIFF(day, '19000107', local_date) % 7) + 7) % 7`.
- `@CreatedUtc` is `CAST(SYSUTCDATETIME() AS datetimeoffset(7))`, which has offset zero; the appointment check constraint requires zero offsets.
- `@AppointmentId` and `@CreatedUtc` are server-generated. The client cannot supply them.
- `51006` is a safety net for a missing/invalid mapping. Setup validates mappings before normal operation (Part 3).
- The procedure does not use `NOLOCK`, snapshot reads, Redis/process locks, a unique start key, or an EF concurrency token.

### Direct-call behavior

The procedure is executable by `scheduler_app` and is called without API prechecks by tests and internal callers. It must enforce every booking invariant itself:

- input presence and UTC normalization;
- future start relative to `@EvaluatedAtUtc`;
- dealership/customer/vehicle/service relationships and vehicle ownership;
- positive SQL duration and checked end derivation;
- active/qualified technician and active bay;
- one opening interval and one shift covering the full interval;
- independent technician and bay conflicts;
- atomic insert/commit or rollback.

Direct callers must not open an outer transaction; a caller-owned transaction returns `51003`. The runtime principal has no direct appointment DML (Part 3), so the procedure is the only write path.

## SQL-side time-zone mapping

### Why a mapping is required

SQL Server 2022 `AT TIME ZONE` accepts Windows time-zone IDs, not IANA IDs. A research probe against the pinned image returned:

```text
Msg 9820, Level 16, State 1
The time zone parameter 'Asia/Ho_Chi_Minh' provided to AT TIME ZONE clause is invalid.
```

The same query with `SE Asia Standard Time` returned `2026-11-03 10:00:00 +07:00`. The persisted `Dealerships.TimeZoneId` remains the canonical IANA ID used by .NET; SQL needs an explicit mapping.

### Proposed mapping persistence

Add `dbo.TimeZoneMappings`:

| Column | Type | Constraints | Purpose |
| --- | --- | --- | --- |
| `IanaId` | `nvarchar(100)` | PK `PK_TimeZoneMappings` | IANA ID matching `Dealerships.TimeZoneId`. |
| `WindowsId` | `nvarchar(100)` | `NOT NULL`; unique `UX_TimeZoneMappings_WindowsId` | SQL Server `AT TIME ZONE` identifier. |

Seed rows:

| `IanaId` | `WindowsId` |
| --- | --- |
| `Asia/Ho_Chi_Minh` | `SE Asia Standard Time` |
| `Europe/London` | `GMT Standard Time` |

Add `FK_Dealerships_TimeZoneMappings_TimeZoneId` from `Dealerships.TimeZoneId` to `TimeZoneMappings.IanaId` (`ON DELETE NO ACTION`). The existing seeded dealerships match the mapping. A dealership with an unmapped IANA ID cannot be created/upgraded until a mapping row is added; document this compatibility limit. The migration must create/seed the mapping table before adding the FK, and the upgrade test must verify it.

The procedure joins `Dealerships.TimeZoneId` to `TimeZoneMappings.IanaId` and uses `AT TIME ZONE @WindowsId`. A scalar variable or column expression is accepted by SQL Server (probed). If the mapping is missing or invalid, the procedure returns `51006`; setup validation catches it earlier.

### Setup validation and DST parity

`TimeZoneMappingValidator` (proposed) runs during `--db-setup` after migrations and before/around provisioning:

1. Verify every dealership's `TimeZoneId` has a mapping row (the FK enforces this; still check for a clear message).
2. Verify `TimeZoneInfo.FindSystemTimeZoneById(IanaId)` resolves on the host.
3. Verify SQL Server accepts each `WindowsId` with `AT TIME ZONE`; invalid IDs fail setup.
4. Compare .NET and SQL local conversions for representative instants, including a spring-forward and fall-back fixture, and fail setup if they disagree. This is the executable guard for the .NET/SQL DST parity gap.

Research-phase probe results (2026-11-01) match the ticket 2 .NET DST fixtures:

| Fixture | .NET/ticket 2 policy | SQL `AT TIME ZONE 'GMT Standard Time'` probe |
| --- | --- | --- |
| `2027-03-28T01:30:00Z` (Europe/London spring forward) | local `02:30 +01:00` | `2027-03-28 02:30:00 +01:00` |
| `2027-03-28T03:00:00Z` | local `04:00 +01:00` | `2027-03-28 04:00:00 +01:00` |
| `2027-10-31T00:30:00Z` (fall back) | local `01:30 +01:00` | `2027-10-31 01:30:00 +01:00` |
| `2027-10-31T02:00:00Z` | local `02:00 +00:00` | `2027-10-31 02:00:00 +00:00` |
| `2026-11-03T03:00:00Z` (Asia/Ho_Chi_Minh) | local `10:00 +07:00` | `2026-11-03 10:00:00 +07:00` |
| Sunday/Tuesday weekday check | `0` Sunday, `2` Tuesday | `DATEDIFF` expression returned `0` and `2` |

The local interval policy remains the ticket 2 wall-clock policy: require the same local date, require one opening row to cover `[localStart, localEnd]`, allow an end exactly at closing, and do not combine adjacent rows. Shifts and conflicts remain UTC half-open. The SQL procedure uses `time(7)` local values; a probe confirmed that `17:00:00` (`time(0)`) is not treated as greater than `17:00:00.4` (`time(7)`), so fractional seconds are not rounded into a false closing boundary.

### Mapping risk and resolution

The IANA and Windows databases can diverge for historical or future rule changes. The mapping table makes the dependency explicit; setup parity checks and DST tests catch current divergences. If a new dealership uses an unsupported zone, setup fails until a mapping and parity fixture are added. This is a documented configuration dependency, not an API-supplied coverage flag; the API cannot bypass it.

## Candidate selection and bounded allocation

### API-side precheck and candidate reads

1. Read the dealership-scoped service duration and IANA time zone; unknown/cross-dealership service -> `404`.
2. Read customer/vehicle and ownership; missing -> `404`; mismatch -> `400`.
3. Capture `evaluatedAt` once and validate future start/checked end; failure -> `400`.
4. Evaluate one opening row covering the local interval; failure -> `409` (valid request, no capacity).
5. Read technicians with the existing availability predicates: active, dealership, qualified, one shift covering the UTC interval, no confirmed half-open conflict; order by name then ID; bound the read to `MaxCandidateAttempts` (5) to avoid unbounded candidate lists.
6. Read bays with active/dealership and no confirmed half-open conflict; order by name then ID; bound to `MaxCandidateAttempts`.
7. If either list is empty -> `409` without invoking the procedure.
8. Enumerate pairs with technicians in the outer loop and bays in the inner loop, preserving the SQL order.

Availability is advisory; the procedure revalidates every predicate. The API never uses a cached availability result or catalog duration for confirmation.

### Pair/attempt policy

- `MaxCandidateAttempts = 5` distinct candidate-pair attempts per request; a deadlock retry re-executes the same attempt and is bounded separately.
- On `51001` (technician conflict), mark that technician as conflicted for this request and skip its remaining pairs. A technician overlap is independent of the bay, so no other bay with that technician can succeed for the interval.
- On `51002` (bay conflict), mark that bay and skip its remaining pairs. A bay overlap is independent of the technician.
- On `51004`/`51005`, mark only the failed pair and continue; the error does not identify which resource caused it. This may waste an attempt but avoids over-skipping a resource that another pair could use.
- Stop at the first confirmed appointment. If the attempt limit is reached with only conflicts/ineligibility, return `409 capacity_unavailable`.
- The limit deliberately does not guarantee exhaustive capacity discovery. A request may receive `409` while an unexamined eligible pair exists beyond the limit or behind a skipped resource. Document this in API/architecture text.
- Stable order makes tests and logs deterministic. The planner/allocator is a pure component so the order/skip/limit behavior can be unit-tested without SQL.

### Alternative-pair behavior

If the first pair loses a race (the API's candidate query saw it free, but another transaction commits first), the procedure returns `51001`/`51002`; the allocator marks the resource and tries the next pair. If a pair remains free, the request succeeds with that pair. When no pair remains or the attempt limit is exhausted, it returns `409`. This behavior is tested deterministically with a fake procedure boundary (controlled conflict then success) and integration-tested with blocked candidates; the concurrency test covers the real race with one eligible pair.

## Clock ownership and future-start evaluation

- The API owns the clock through the injected `TimeProvider`. The initial request validation captures `evaluatedAt` once and requires a strictly future start; equality is rejected (AS-05, no minimum lead time).
- Before each procedure attempt, the allocator captures a fresh `procedureEvaluatedAt = timeProvider.GetUtcNow()` and passes it as `@EvaluatedAtUtc`. This accounts for candidate-query and prior-attempt elapsed time.
- The procedure rechecks `@StartUtc > @EvaluatedAtUtc` before it begins its transaction and locks, using the per-attempt instant; this matches the proposed body. A request that waited on a resource lock still uses the evaluation instant captured immediately before that attempt, so a start that becomes past during the bounded lock wait may still commit; rechecking after locks with the same caller-supplied instant would not change that outcome. This is a documented limitation of the no-minimum-lead-time policy, bounded by the 5 s lock timeout/10 s command timeout/20 s request budget. If a later ticket introduces a minimum lead time, this gap can be closed without changing the procedure contract.
- Direct procedure calls supply an explicit `@EvaluatedAtUtc`; tests use the fixed fixture clock. The procedure is an internal same-owner boundary executable only by `scheduler_app`; API clients cannot supply this parameter. This preserves deterministic fixtures without letting clients control production validation.
- `@CreatedUtc` uses SQL Server time (`SYSUTCDATETIME`), intentionally independent of the API clock. Tests assert it is present/UTC, not an exact fixed value. The response `createdAt` may differ from the evaluation clock; document that creation time is server-authoritative.

## Retry, timeout, budget, and unknown-commit policy

| Setting | Proposed value | Rationale |
| --- | --- | --- |
| `MaxCandidateAttempts` | 5 | Bounds pair exploration; short transactions; documents the `409` limit. |
| `MaxDeadlockRetries` | 2 (global per request) | SQL Server rolls back a deadlock victim, so a whole-attempt replay is safe; global bound prevents unbounded retries. |
| `RetryDelayMilliseconds` | 50 ms per retry (50, 100) | Short deterministic backoff; no random jitter needed for the demo. |
| Procedure lock timeout | `SET LOCK_TIMEOUT 5000` (5 s) | Bounds lock waiting separately from the client command timeout. |
| Command timeout | 10 s (`SqlCommand.CommandTimeout`) | Exceeds the lock timeout so a lock wait reports `1222` rather than `-2`; bounds a stuck command. |
| Total request budget | 20 s | Hard upper bound across reads, candidate attempts, and retries; returns `503` on internal expiry. |
| Maximum procedure executions | 5 candidate attempts + 2 deadlock retries = 7 | Documented worst-case write attempts. |

- The total budget starts at the service boundary and covers EF reads plus procedure attempts. Use a linked cancellation source with the request-aborted token; internal budget expiry maps to `503 dependency_unavailable`, while client abort propagates as `OperationCanceledException` and is not converted into a dependency failure.
- Capture the per-attempt evaluation instant immediately before the procedure call; if the budget is already exhausted, return `503` without another write.
- Retry only `1205` (deadlock victim), which SQL Server has already rolled back. Never retry `1222`, `-2`, classified connectivity errors, or an unknown commit outcome. `51001`/`51002`/`51004`/`51005` move to another pair, not a blind write replay.
- Do not enable EF `EnableRetryOnFailure` for the write path. The procedure boundary uses a direct `SqlCommand` and its own policy; any future global retry configuration must exclude booking writes. The current `UseSqlServer` has no retrying execution strategy.
- Request cancellation: pass the linked token to EF reads and `SqlCommand.ExecuteNonQueryAsync`; dispose the connection/command; SQL Server attention/rollback releases locks. If cancellation occurs after commit but before acknowledgment, the appointment may exist; do not replay. A subsequent GET may reveal it, but idempotency is out of scope (AS-11).
- Unknown commit: a connection failure during or after `COMMIT` is classified as a dependency failure and returns `503`; the API does not retry. Log the attempt/context without claiming success.
- The procedure's CATCH rolls back on any error (`XACT_STATE() <> 0`) before rethrowing; `SET XACT_ABORT ON` plus explicit rollback prevents partial rows and holds locks until commit/rollback.

## Isolation and locking verification

- The procedure sets `SERIALIZABLE` explicitly before `BEGIN TRANSACTION`; it does not rely on the database default. A probe of a new database showed `is_read_committed_snapshot_on = 0` and `snapshot_isolation_state = OFF`; the explicit isolation remains correct even if a deployment enables RCSI.
- No `NOLOCK`, stale snapshot read, Redis/process lock, unique start key, or EF concurrency token is used as the overlap defense.
- The technician row is locked before the bay row in every booking attempt. All booking writers must preserve this order; future mutation paths must use the same protocol.
- Locking favors correctness over maximum write concurrency; 20 requests for one pair may wait briefly and then recheck conflicts. Transactions are short and predicates are indexed.

## Concurrency and failure verification design

| Case | Proposed test | Expected |
| --- | --- | --- |
| Initially empty table / one pair / 20 POSTs | Two `WebApplicationFactory` hosts sharing one SQL database; 20 tasks release from a `TaskCompletionSource` barrier and POST the same interval; assert HTTP statuses and final row count. | Exactly one `201`, nineteen `409`, one stored appointment; no `503`/`500`. |
| Technician-only conflict | Direct runtime procedure call for a different bay but the same technician, overlapping an existing appointment. | `51001`; no new row. |
| Bay-only conflict | Direct runtime procedure call for a different technician but the same bay, overlapping. | `51002`; no new row. |
| Adjacent / partial / contained / identical | Direct procedure and API cases against a blocked resource. | Adjacent succeeds; overlaps conflict. |
| Alternative pair | Fake procedure boundary returns `51001` for pair 1 and success for pair 2; separately block the first ordered candidate in SQL and verify the API selects the next. | Success on the second pair; exhaustion -> `409`. |
| Atomic failure / released locks | Count rows before/after a conflict/validation failure; then call a valid non-overlapping pair. | Row count unchanged; the later call succeeds, proving locks released. |
| Lock wait then fresh overlap check | 20-way race plus a direct-call conflict test. | Waiting request rechecks and returns `409`, not a duplicate. |
| Lock timeout | Hold the technician row with `UPDLOCK, HOLDLOCK` in a test connection; call the procedure directly with a runtime connection; release after. Use a fake procedure result for the API `503` mapping. | `1222` -> `503` mapping; no row; no retry. |
| Command timeout | Direct procedure call with `SqlCommand.CommandTimeout = 1 s` while a lock is held; use a fake procedure result for the API `503` mapping. | `-2` -> `503` mapping; no row. |
| Deadlock | Unit-test allocator with fake `1205` results; optionally induce with competing lock order in SQL. | Two retries then `503`; no blind replay. |
| Cancellation | Direct procedure call with a cancellation token while a lock is held; cancel, release, then issue a valid call. Separately, test API token propagation with a fake boundary that waits on the token. | No row from the canceled attempt; later request succeeds (locks released); API cancellation propagates. |
| Unknown commit | Unit-test that non-`1205` dependency errors are not retried; inspect the no-retry policy. | One procedure call; `503`; no replay. |
| Redis outage | Stop Redis; run a valid booking and retrieval. | Unaffected; SQL used. |
| SQL outage | Stop SQL; POST and readiness. | POST `503`; readiness `503`; no confirmation row. |
| Restart | Book, dispose the API host, start a new host on the same database, GET. | Appointment retrievable. |

The hold-lock timeout/command-timeout/cancellation cases target the procedure boundary directly: the API's candidate read uses the same resource rows and would block before invoking the procedure, so a held row is not a clean API-level injection point. The API mapping of a dependency/lock result to `503` is tested with a fake `IAppointmentBookingProcedure`; the real `1222`/`-2` and SQL attention/rollback behavior is tested with direct runtime `SqlCommand` calls. The 20-way test uses one eligible pair and the same interval so conflict is the only successful outcome for 19 requests. Use independent HTTP clients/factories and assert final SQL state through the setup connection. Do not use arbitrary sleeps as the coordination mechanism; use barriers/`TaskCompletionSource` and poll only bounded conditions.

## Cache and availability compatibility

- Booking never invokes `ServiceCatalogService` or Redis. The API precheck and procedure read `ServiceTypes.DurationMinutes` from SQL; a stale catalog cache cannot change the booking duration or snapshot.
- The procedure inserts into `Appointments`; availability queries confirmed appointments directly, so a subsequent uncached availability read sees the committed row. No cache invalidation is required.
- A service duration change after booking does not alter the stored appointment; GET returns the snapshot.
- Redis outage does not affect booking/retrieval; SQL outage prevents confirmation and fails readiness.

## Research-phase probe evidence

| Probe | Command / object | Observed result | Relevance |
| --- | --- | --- | --- |
| IANA `AT TIME ZONE` | `CONVERT(datetimeoffset(7),'2026-11-03T03:00:00+00:00') AT TIME ZONE 'Asia/Ho_Chi_Minh'` | `Msg 9820`: invalid time zone parameter. | Requires an explicit Windows mapping. |
| Windows mapping, scalar/column | `AT TIME ZONE 'SE Asia Standard Time'`; table variable and scalar `@tz` | `2026-11-03 10:00:00 +07:00`; column/table-variable/variable forms accepted. | Procedure can use `@WindowsId`. |
| London DST | Spring/fall instants with `'GMT Standard Time'` | `02:30 +01:00`, `04:00 +01:00`, `01:30 +01:00`, `02:00 +00:00`. | Matches ticket 2 .NET fixtures. |
| Weekday expression | `DATEDIFF(day,'19000107',date) % 7` | Tuesday `2026-11-03` = `2`; Sunday `2027-03-28` = `0`. | Sunday-0 mapping independent of `DATEFIRST`. |
| `time(0)` vs `time(7)` | `CAST('17:00:00' AS time(0)) >= CAST('17:00:00.4' AS time(7))` | `0`. | No fractional-second rounding into a false closing boundary. |
| Invalid time zone inside procedure TRY/CATCH | `dbo.TzProbe` with `Asia/Ho_Chi_Minh` | `51006` (not raw `9820`); valid Windows ID returned the local value. | `51006` is catchable/mappable. |
| `THROW` number through SqlClient | Temporary .NET 10 console, `Microsoft.Data.SqlClient` 6.1.7, `EXEC dbo.ConflictProc` | `SqlException.Number = 51001`; inserted probe row rolled back. | Error-number mapping and rollback are viable. |
| Lock timeout | Session A holds `UPDLOCK, HOLDLOCK` on a row; session B sets `LOCK_TIMEOUT 1000` and reads the row | `Msg 1222`: lock request time out. | Lock waits and `1222` classification are observable. |
| Snapshot defaults | `sys.databases` for a new database | `is_read_committed_snapshot_on = 0`; `snapshot_isolation_state = OFF`. | Explicit `SERIALIZABLE` is the selected protection. |

These probes used simplified synthetic objects in an isolated container. They do not replace the ticket's real-schema concurrency, permission, migration, or API tests.

## Code audit for this concern

| Classification | File / symbol, or proposed component | Current behavior / explicit gap | Proposed action | Requirements |
| --- | --- | --- | --- | --- |
| Must Change | New migration/procedure | No booking write path. | Add `dbo.CreateAppointment` with the contract above. | S3-02-S3-04 |
| Must Change | Time-zone mapping entity/configuration/seed/FK | No SQL mapping. | Add `TimeZoneMappings`; update model/snapshot; validate setup. | S3-02, S3-03, S3-06 |
| Must Change | `Infrastructure/Setup/TimeZoneMappingValidator.cs` | No mapping/DST validation. | Validate mappings, Windows IDs, and .NET/SQL parity during setup. | S3-02, S3-06 |
| Must Change | `Features/Appointments/` allocator/planner/service | No orchestration/retry policy. | Add bounded candidate/pair allocation with fake-boundary tests. | S3-03, S3-04 |
| Must Change | `Infrastructure/Persistence/AppointmentBookingProcedure.cs` | No procedure boundary. | Add `SqlCommand` caller with output parameters, timeout, and error mapping. | S3-03, S3-04 |
| Need Review | `AvailabilityQueries.cs` | Candidate queries are ordered but unbounded. | Reuse with `Take(MaxCandidateAttempts)` or add booking-specific reads; keep availability unchanged. | S3-02 |
| Need Review | `SqlFailureClassifier.cs` | Classifies 1205/1222/-2 but not procedure numbers. | Handle 51001-51006 before the global handler. | S3-04 |
| Need Review | Existing appointment indexes | Cover resource/start/end/status; no unique start. | No new index unless implementation proves a gap; verify predicates/plan. | S3-02, S3-03 |
| No Change | `AppointmentConfiguration.cs`, initial migration | Existing checks/FKs/indexes. | Use as-is; no appointment schema change. | S3-01-S3-03 |
| No Change | `SchedulerDbContextFactory.cs`, `UseSqlServer` | No retry strategy. | Keep non-retrying; do not enable write retries. | S3-04 |

## Gap analysis for this concern

| ID | Type | Question / risk | Proposed answer / mitigation | Status |
| --- | --- | --- | --- | --- |
| P3-01 | Decision | Procedure name/signature/error numbers? | `dbo.CreateAppointment`; explicit IDs + `@StartUtc`/`@EvaluatedAtUtc`; outputs ID/end/duration/created; 51001-51006. | Resolved. |
| P3-02 | Decision | SQL opening validation with IANA? | `TimeZoneMappings` + `AT TIME ZONE` + setup parity validation. | Resolved; probed. |
| P3-03 | Decision | Candidate order/limits/skip/409? | Name/ID order; 5 attempts; skip conflicted technician/bay; pair-only skip for 51004/51005; 409 when exhausted; non-exhaustive capacity documented. | Resolved. |
| P3-04 | Decision | Retry/timeout/budget values? | 5 candidates; 2 deadlock retries; 50 ms backoff; 5 s lock; 10 s command; 20 s total. | Resolved. |
| P3-05 | Decision | Clock ownership/evaluation? | API `TimeProvider`; initial + per-attempt instants; procedure checks the per-attempt instant before its transaction/locks; direct calls explicit; lock-wait limitation documented. | Resolved. |
| P3-06 | Risk | Deadlock/unknown-commit tests may be flaky. | Fake procedure boundary for policy; real lock timeout/cancellation; unknown commit asserted by no-retry policy/code inspection. | Resolved; Part 4. |
| P3-07 | Risk | IANA/Windows DST divergence. | Setup parity check + DST fixtures; document mapping dependency. | Resolved; probe matches. |
| P3-08 | Decision | Which isolation? | Explicit `SERIALIZABLE`, no snapshot/NOLOCK; verify RCSI state. | Resolved; probed. |
| P3-09 | Decision | Are new appointment indexes needed? | No; existing indexes cover; mapping table gets its own PK/unique. | Resolved. |
| P3-10 | Risk | Procedure holds locks while validating; long wait. | Short transaction, indexed predicates, 5 s lock timeout, candidate/budget bounds; measure only if needed. | Resolved; no performance claim. |
| P3-11 | Risk | Request cancellation after commit leaves an appointment. | Document; no idempotent replay; a later GET may reveal it. | Resolved with limitation. |
| P3-12 | Decision | Does the procedure trust `@EvaluatedAtUtc`? | Internal runtime-principal boundary; API derives it from the injected clock; clients cannot supply it. | Resolved. |
| P3-13 | Decision | How are candidate conflicts distinguished from ineligibility? | 51001/51002 skip the resource; 51004/51005 skip only the pair; 51003/51006 stop. | Resolved. |
| P3-14 | Risk | Stale catalog could change duration. | Booking never reads cache; API/procedure read SQL. | Resolved; S3-V02 test planned. |
| P3-15 | Risk | Committed appointment not visible to availability. | Availability queries SQL directly; no cache. | Resolved; S3-V02 test planned. |

## Verification mapping for this concern

| Requirement | Proposed checks | Level |
| --- | --- | --- |
| S3-02, AC3-02, AC3-11 | Qualification/active, opening/shift full coverage, ownership/scope, SQL duration, snapshot, stale-cache independence. | Unit + API + SQL + Redis |
| S3-03, AC3-03, AC3-05 | Procedure insert/locks/atomicity, adjacent/overlap boundaries, technician-only/bay-only, direct-call parity, denied DML. | SQL Server + API |
| S3-04, AC3-06, AC3-12 | Candidate retry, deadlock retry, lock/command timeout, budget, cancellation, unknown-commit no-replay, unexpected-error classification. | Unit + API + SQL |
| T-07, AC3-04 | 20 concurrent POSTs, one pair, two factories, one `201`, nineteen `409`, one row. | API + SQL |
| T-09, AC3-06 | Failed allocation count unchanged; subsequent valid call succeeds (locks released). | SQL Server |
| T-13, AC3-07 | Booking/retrieval during Redis outage. | API + SQL + Redis |
| S3-V03 | Alternative pair within the limit; exhaustion -> `409`. | Unit + API + SQL |
| S3-V05 | Direct procedure validation without API prechecks, including offset/DST fixtures. | SQL Server |

All checks are **Not run**. Detailed test files/commands are in [[research/3/04-verification-and-documentation|Part 4]].
