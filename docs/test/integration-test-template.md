---
title: "Scheduler Integration Test Template"
tags: [test, integration, template, service-scheduler]
---

# Integration test template

Use this checklist with [[test/backend-test-template|Backend test template]] when a ticket affects API persistence, resource allocation, migrations, cache behavior, or operational dependencies. It describes required evidence to collect as the application is implemented; it contains no executed test results.

## Setup and isolation

- Run xUnit API integration tests using WebApplicationFactory and real SQL Server through Testcontainers, plus Redis for cache cases. Pin compatible dependency versions during scaffolding and record the actual versions and startup commands.
- Apply the real EF migrations and synthetic seeds using the setup identity. Exercise API and booking procedure paths with the restricted runtime identity; an administrator connection would hide permission defects.
- Isolate each test's data and resources, freeze or control time, and use future intervals relative to the test clock. Record dealership IANA zones and any DST case explicitly.
- For concurrent cases, use independent connections/contexts and synchronization barriers rather than arbitrary sleeps. Make resource eligibility deterministic and inspect final committed state.
- Document cleanup that affects only the test's own resources. Use local isolated dependencies for outage and restart checks.

## Scenarios to adapt

The cases below are reusable examples. Select the applicable cases and requirement/check IDs from the current ticket; each ticket owns its verification scope.

| Area | Setup / action | Expected evidence | Example check ID |
| --- | --- | --- | --- |
| Booking persistence | Submit a valid future booking, then retrieve it | `201`, correct duration and complete persisted associations | T-01 |
| Resource eligibility | Use unqualified/inactive resources or insufficient shift/opening coverage | Candidates rejected; no unintended confirmation | T-02, T-03 |
| Interval boundaries | Try adjacent, partial-overlap, containing, and identical intervals | Adjacent succeeds; conflicting confirmed resource intervals are rejected | T-04 |
| Individual resource conflicts | Same technician with different bays, then same bay with different technicians | Either resource conflict independently prevents overlap | T-05, T-06 |
| Concurrent competition | Exactly one eligible pair; 20 distinct simultaneous POSTs | One `201`, nineteen `409`s, one stored appointment, no overlap | T-07 |
| Database boundary | Call the procedure through independent runtime connections, bypassing API prechecks; attempt direct appointment writes | Procedure rejects overlaps; valid execution succeeds; direct insert/update/delete denied | T-08 |
| Atomic failure | Trigger a controlled failure before commit | No partial booking; rollback releases both resources | T-09 |
| Time and scope | Offset equivalence, past/offset-free starts, local opening/DST boundaries, mismatched ownership/dealership | Ticket-defined validation and correct UTC persistence; invalid requests create no booking | T-10, T-11 |
| Cache lifecycle | Miss, hit, expiry, and explicit invalidation | Correct catalog values and observable cache behavior; fresh SQL read when required | T-12 |
| Redis outage | Stop isolated Redis; retrieve catalog and create a booking | Bounded SQL fallback with correct results and degradation signals | T-13 |
| Restart / SQL outage | Retrieve a booking after API restart; separately interrupt SQL Server | Persistence survives restart; no confirmation during outage; readiness fails | T-14 |
| Observability | Generate booking/catalog requests; inspect logs and provisioned metric/trace backends | Correlated request evidence, bounded metric labels, working exports | T-15 |
| Clean checkout | Execute documented dependency startup, restore, build, migration, seed, and test commands | Reproducible execution with actual commands and recorded results | T-16 |

Use a check ID only when the current ticket defines that case, and record its ticket context in the test report. New tickets add or revise scenarios against their own requirements; this table is not a claim of implemented test coverage.

## Additional concurrency evidence

Cover an initially empty appointment table, a lock wait followed by a fresh overlap check, rollback and subsequent resource reuse, and bounded deadlock/timeout behavior. Exercise competing API instances where practical. Verify the ticket's distinction between capacity conflicts and dependency failures, and do not blindly retry a write whose commit outcome is unknown.

Record the database isolation configuration, procedure version, runtime permissions, test connection arrangement, and actual query/assertion used to prove final state. SQL Server locking and permissions require real SQL Server verification; mocks, SQLite, or an in-memory provider cannot establish those guarantees.

## Evidence record

Add these fields to the ticket test report for each relevant integration case:

```markdown
### <Case ID> - <integration scenario>

- Requirement IDs:
- Reviewed revision and test file / symbol:
- Dependency versions, migrations, seeds, and identity used (no secrets):
- Setup, coordination, and failure injection:
- Actual command / working directory:
- Expected API results and final database/cache state:
- Observed results and evidence links:
- Status: Not run / Passed / Failed / Blocked
- Cleanup, limitations, and follow-up:
```
