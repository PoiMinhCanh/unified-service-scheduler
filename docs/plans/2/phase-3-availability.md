---
title: "2 - Phase 3: Availability"
tags: [plans, service-scheduler, api, availability, sql]
ticket: 2
phase: 3
status: planned
---

# Phase 3: Availability

## Requirements and scope

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/01-api-contracts-and-validation|API contracts and validation]], [[research/2/02-availability-and-scheduling|availability and scheduling]], [[research/2/04-verification-and-documentation|verification and documentation]].
- Requirement IDs: S2-02, S2-03, S2-04; AC2-02, AC2-03; T-02, T-03, T-04, T-10, T-11; project A-02; test IDs TC2-12-TC2-25, TC2-29, TC2-34.
- Intended outcome: deliver `GET /api/dealerships/{dealershipId}/availability`, including raw explicit-offset parsing, strict future-start validation, authoritative SQL duration/end computation, dealership-local opening coverage, independent SQL-side technician and bay eligibility/conflict checks, deterministic ordering, advisory `available:false` behavior, and documented error responses.
- Exclusions: booking writes/reservations and retrieval (ticket 3); cache use for availability (never); generated OpenAPI transformers (Phase 5); schema migration unless a missing index is proven.

## Dependencies

- Phase 1: `TimeProvider` registration, Problem Details `code`/`traceId` contract, SQL failure classification, test factory clock replacement.
- Phase 2: feature-folder conventions, catalog fixture patterns, combined SQL + Redis test base, and test report entries.
- Verified research decision: wall-clock local opening comparison with same-local-date requirement; half-open UTC overlap predicates; stable name/ID ordering.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S2-02, S2-04; T-10 | `src/ServiceScheduler.Api/Features/Availability/StartsAtParser.cs`, `AvailabilityInterval.cs` | Bind `startsAt` as a raw string; trim and require a trailing `Z`/`z` or explicit `±HH:MM`/`±HHMM` offset; parse with invariant culture; normalize to UTC; capture `evaluatedAt` once per request; require `startsAt > evaluatedAt`; compute `endsAt` with checked tick arithmetic and reject overflow as `400 validation_error`. | Unit tests cover `Z`, positive/negative offsets, whitespace, offset-free, date-only, unparseable, past/equal starts, and maximum-range overflow; API returns the same normalized instant for `Z` and `%2B07:00` inputs. | Planned |
| 2 | S2-02, S2-03; AC2-02 | `src/ServiceScheduler.Api/Features/Availability/OpeningCoverage.cs`, `ShiftCoverage.cs`, `HalfOpenOverlap.cs` | Add pure rules: convert UTC to dealership wall clock, require the same local date, compare `TimeOnly` boundaries against one opening row; shift coverage uses UTC start/end inclusive boundaries; overlap rule is `existing.StartUtc < requested.EndUtc && existing.EndUtc > requested.StartUtc`. | Unit tests cover normal/before/after/equal boundaries, cross-midnight rejection, adjacent/split rows not combined, split-shift rejection, the five-row overlap truth table, and DST spring/fall fixtures. | Planned |
| 3 | S2-02, S2-04; AC2-03 | `src/ServiceScheduler.Api/Features/Availability/AvailabilityService.cs` | Read the dealership-scoped service duration and IANA zone in one `AsNoTracking` query; unknown dealership, unknown service, or cross-dealership service returns `404 not_found`; resolve the zone; invalid data/zone becomes a sanitized unexpected failure, not availability. | API tests prove `404` scope behavior and the sanitized-`500` path for an invalid zone (TC2-29); duration always comes from SQL Server, never the client or cache. | Planned |
| 4 | S2-02; AC2-02, AC2-03 | `src/ServiceScheduler.Api/Features/Availability/AvailabilityService.cs` | Evaluate opening coverage with the local `TimeOnly` SQL `Any` query against one opening row; return `200 available:false` when no single row covers the interval. | Partial-opening, before/after-hours, adjacent-row, and cross-midnight cases return `available:false`; exact closing boundary is accepted. | Planned |
| 5 | S2-03; AC2-02, T-02, T-03 | `src/ServiceScheduler.Api/Features/Availability/AvailabilityService.cs` | Query one eligible technician: active, dealership-scoped, qualified for the service/dealership, one shift covering `[startsAt, endsAt]`, and no confirmed appointment overlapping by the half-open predicate; order by `Name`, `Id`; bounded `FirstOrDefault`. | Generated SQL contains the active/qualification/shift/`NOT EXISTS`/half-open predicates; unqualified, inactive, partial-shift, and conflicting technicians never satisfy availability. | Planned |
| 6 | S2-03; AC2-02, T-02, T-04 | `src/ServiceScheduler.Api/Features/Availability/AvailabilityService.cs` | Query one eligible bay independently: active, dealership-scoped, and no confirmed appointment overlapping; order by `Name`, `Id`; bounded `FirstOrDefault`. | Technician-only and bay-only conflicts behave independently; a free pair differing from existing appointments remains available; adjacent intervals are allowed. | Planned |
| 7 | S2-04; AC2-01, AC2-03 | `src/ServiceScheduler.Api/Features/Availability/Contracts/AvailabilityResponse.cs`, `AvailabilityController.cs` | Return `{ dealershipId, serviceTypeId, available, startsAt, endsAt, evaluatedAt }` for both outcomes with UTC `DateTime` values serialized with `Z`; bind `dealershipId`/`serviceTypeId` as raw `Guid` query/route values (no `:guid` constraint) so malformed or missing values reach model validation as `400 validation_error`; accept no resource/duration/end inputs; perform no writes and no Redis calls; add `[ProducesResponseType]` for `200`/`400`/`404`/`503`/`500`. | Response contains exactly the six fields for both outcomes; `available` is `technicianExists && bayExists`; malformed/missing IDs return `400`; no technician/bay IDs, writes, or cache keys appear. | Planned |
| 8 | S2-03, S2-04; TC2-15-TC2-24, TC2-29 | `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulingTestData.cs`, `Availability/AvailabilityTimeTests.cs`, `AvailabilityEligibilityTests.cs`, `AvailabilityCoverageTests.cs`, `AvailabilityConflictTests.cs`, `AvailabilityReadOnlyTests.cs`, `AvailabilityDurationTests.cs`, `AvailabilityDstTests.cs`, `AvailabilityScopeTests.cs`, `AvailabilityFailureTests.cs` | Add setup-identity helpers for temporary appointments, technicians, qualifications, shifts, bays, opening hours, dealerships, and service types, satisfying existing checks; implement the requirement-linked availability matrix with deterministic clock and cleanup. | TC2-12-TC2-24 and TC2-29 pass against real SQL Server (and Redis where stale-duration isolation is used); final stored state and cleanup are asserted. | Planned |
| 9 | S2-03; AC2-02; TC2-25 | `tests/ServiceScheduler.IntegrationTests/Availability/AvailabilitySqlTests.cs` | Capture EF-generated SQL for the service, opening, technician, and bay queries through logging/`ToQueryString`; assert dealership scope, `Status = Confirmed`, half-open comparisons, `NOT EXISTS`, and stable ordering; document the index mapping. | Captured SQL contains the required predicates; the index mapping is recorded in the test report and Phase 6 schema/migration records; no migration is added unless a concrete missing index is proven. | Planned |
| 10 | S2-04; AC2-01, AC2-06; TC2-34 | `tests/ServiceScheduler.IntegrationTests/Startup/ProblemDetailsTests.cs` | Extend error-path assertions for missing/malformed parameters, unknown routes, and consistent `code`/`traceId` across validation and not-found responses. | `400`/`404` Problem Details carry stable codes/trace IDs and no stack/password/connection text; existing unknown-route behavior still passes. | Planned |
| 11 | S2-06; P-04 | `docs/test/2-test.md` | Append Phase 3 revision, commands, observed outcomes, SQL/index findings, and unexecuted checks. | Report preserves prior phases and records actual availability evidence. | Planned |

## Verification

| Test ID / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Unit rules (S2-02, S2-03) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Availability"` | Parser, interval, opening, shift, overlap, and composition tests pass, including DST fixtures. | Not run |
| TC2-12-TC2-14 (S2-02, T-10) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Availability.AvailabilityTime"` | Equivalent `Z`/`%2B07:00` instants normalize identically; offset-free/past/equal/overflow inputs return `400 validation_error`; timestamps end with `Z`. | Not run |
| TC2-15-TC2-17 (S2-03, T-02) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityEligibility"` | Seeded eligible pair returns `available:true`; unqualified/inactive candidates never satisfy availability. | Not run |
| TC2-18 (S2-02, S2-03, T-03) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityCoverage"` | Partial shift/opening returns `available:false`; adjacent/split rows are not combined. | Not run |
| TC2-19-TC2-21 (S2-03, T-04) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityConflict|FullyQualifiedName~Availability.AvailabilityReadOnly"` | Independent technician/bay conflicts; free different pair available; adjacent allowed; overlaps unavailable; no writes or cache keys. | Not run |
| TC2-22 (S2-02, AC2-03) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityDuration"` | Stale Redis catalog duration is ignored; `endsAt` uses SQL duration. | Not run |
| TC2-23 (S2-02, T-10) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityDst"` | Spring-forward, nonexistent-boundary, fall-back repeated-hour, and outside-window cases follow the documented wall-clock policy. | Not run |
| TC2-24 (S2-03, T-11) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityScope"` | Cross-dealership/unknown references return `404`; foreign resources never satisfy availability. | Not run |
| TC2-25 (S2-03) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilitySql"` | Generated SQL contains all required predicates; index mapping recorded. | Not run |
| TC2-29 (S2-04) | Same project, `--filter "FullyQualifiedName~Availability.AvailabilityFailure"` | Invalid-zone dealership/service returns sanitized `500 internal_error` with no secrets. | Not run |
| TC2-34 (S2-04) | Same project, `--filter "FullyQualifiedName~Startup.ProblemDetails"` | `400`/`404` contracts carry `code`/`traceId`; unknown route unchanged. | Not run |

## Exit criteria

- Availability parser and pure rules are unit-tested; the endpoint obeys the documented contract for both outcomes.
- TC2-12-TC2-25, TC2-29, and TC2-34 pass against real SQL Server, with Redis used only for the stale-duration isolation case.
- Generated SQL and index mapping are recorded; no migration is added unless a concrete missing index is proven and documented as a deviation.
- Availability performs no writes and no cache reads; runtime permissions remain `SELECT` only.
- `docs/test/2-test.md` contains actual Phase 3 evidence and explicit remaining checks.

## Impacts and remaining decisions

- Database/migrations: none planned; the generated-SQL/index review is the deciding evidence.
- Cache: availability bypasses Redis by design; only the stale-duration test reads/writes cache keys.
- Configuration: no new names; the fixed test clock is supplied through the factory.
- Deployment: no compose change in this phase.
- Remaining decisions: none blocking. Query composition may be implemented as service methods or reusable `IQueryable` builders; the SQL/behavior contract must remain as specified.
