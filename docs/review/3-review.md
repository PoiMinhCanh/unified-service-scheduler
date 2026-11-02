---
title: "3 - Review"
tags: [review, service-scheduler, booking, concurrency, permissions]
ticket: 3
status: complete
updated: 2026-11-02
---

# 3 - Review

## Scope and baseline

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research / plan: [[research/3/00-overview|Ticket 3 research overview]] with [[research/3/01-api-contracts-and-retrieval|API contracts and retrieval]], [[research/3/02-booking-procedure-and-concurrency|Booking procedure and concurrency]], [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], and [[research/3/04-verification-and-documentation|Verification and documentation]]; [[plans/3/README|Ticket 3 implementation plan]] with phases 1-6.
- Reviewed revision or diff: commit `a66692752aa41a494246edc274287d9fda410f33` (HEAD, `main`); the ticket 3 change is `git diff 90a6284..a666927`, 80 paths and 9,356 insertions. The working tree was clean during review.
- Scope, exclusions, and assumptions: reviewed the appointment controller/service/DTOs, candidate planner, allocator, SQL procedure boundary, migration and model snapshot, runtime principal provisioner, time-zone mapping validator, setup runner, Problem Details/OpenAPI changes, program registration, booking unit/integration tests, and the updated README/architecture/setup/database/deploy/test records. Ticket 3 does not change the appointment table or indexes, the catalog cache, or telemetry; authentication, cancellation/rescheduling, idempotency, vehicle-overlap protection, and a custom UI remain excluded.
- Review method: static inspection of the implementation and documentation, re-execution of the full unit and integration suites from the reviewed revision, a build, and `dotnet format --verify-no-changes`. No application or test files were modified during the review; this report is the only new file.

> [!IMPORTANT]
> The automated evidence recorded in [[test/3-test|the ticket 3 test report]] was reproduced during this review: 133 unit tests and 154 integration tests passed against real SQL Server 2022 CU23 and Redis containers, and build/format were clean. The findings below are policy and validation defects identified by code inspection; the existing automated tests do not exercise their triggering conditions. The human manual checklist has not been generated or executed, so ticket acceptance is not claimed.

## Findings

### R-01 - Deadlock retry limit is per candidate pair instead of per request

- Severity / status: Medium / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S3-04, AC3-06, AC3-12; `src/ServiceScheduler.Api/Features/Appointments/BookingAllocator.cs:71` (`var deadlockRetries = 0;` is reset inside the candidate-pair loop) and `:124-147` (deadlock branch); documented global bound in `docs/plans/3/phase-3-booking-orchestration.md:33` ("at most `MaxDeadlockRetries` times globally") and `:44`, `docs/plans/3/phase-5-concurrency-and-operations.md:49`, and `docs/research/3/02-booking-procedure-and-concurrency.md` ("2 (global per request)"; "Maximum procedure executions = 5 candidate attempts + 2 deadlock retries = 7").
- Triggering scenario and evidence: With five candidate pairs, suppose each pair's first and second procedure calls return `1205` and the third call returns `51001`/`51004`/`51005`, so the allocator skips to the next pair. Because `deadlockRetries` is declared inside `foreach (var pair in pairs)`, each pair receives up to `MaxDeadlockRetries` (2) retries. The request can therefore execute up to 15 procedure calls (`5 pairs x 3 calls`) instead of the documented maximum of 7. `BookingAllocatorTests` covers deadlock retries only for a single pair, so no executed test detects the request-wide deviation.
- Expected versus actual behavior: The research/plan bound is two deadlock retries for the whole request, with at most seven procedure executions; the implementation applies the retry allowance separately to every pair, so the request-wide bound is not enforced.
- Impact and recommended correction: S3-04 and AC3-12's bounded-retry requirement is not met, and a deadlock storm can multiply lock waits and procedure executions beyond the documented policy. Move the retry counter outside the pair loop or enforce a request-wide procedure-execution cap while keeping same-pair retry semantics; add a unit test with multiple pairs that returns deadlocks across pair transitions and asserts the global retry/execution limit.

### R-02 - Request budget is not rechecked immediately before a retried procedure call

- Severity / status: Low / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S3-04, AC3-06, AC3-12; `src/ServiceScheduler.Api/Features/Appointments/BookingAllocator.cs:59-63` (budget check per pair) and `:124-147` (deadlock check, backoff, then `continue` without another budget check); required pre-call check in `docs/plans/3/phase-3-booking-orchestration.md:33` ("before each procedure attempt/retry, return `DependencyUnavailable` if the total budget is exhausted"), `docs/plans/3/phase-5-concurrency-and-operations.md:51` ("No write starts after the request budget is exhausted"), and `docs/research/3/02-booking-procedure-and-concurrency.md` ("Hard upper bound ... if the budget is already exhausted, return `503` without another write").
- Triggering scenario and evidence: A deadlock occurs when the request budget is nearly exhausted. The post-deadlock budget check passes, the configured backoff (default 50 ms; up to 1,000 ms by validator) elapses, and the budget expires during that delay. The `while (true)` loop then captures a fresh evaluation instant and calls the procedure again without checking the budget immediately before the call. The existing `BudgetExpiryMapsTo503AndStopsBeforeTheNextWrite` test advances the clock inside the procedure handler, so the post-deadlock check catches expiry before the backoff; no test covers expiry during backoff.
- Expected versus actual behavior: The documented total budget is a hard upper bound and no write should start after it expires; the implementation can start one more write after the backoff, exceeding the budget by the backoff plus up to the command timeout (10 seconds by default).
- Impact and recommended correction: The S3-04 budget boundary is not strictly enforced. Add an `IsBudgetExhausted` check at the start of each `while` iteration after the backoff (or immediately before every `ExecuteAsync`), and add a deterministic fake-clock test that advances time during the backoff and asserts that the procedure is not called again. A linked cancellation source with a budget deadline would also bound in-flight work while preserving the `503 dependency_unavailable` mapping.

### R-03 - README and guides reference booking smoke/concurrency harnesses in a nonexistent scripts directory

- Severity / status: Medium / Verified resolved (2026-11-02). The current docs point to Swagger UI and the automated tests, and historical harness evidence is labeled as removed.
- Requirement ID and file / symbol / line: S3-06, AC3-09, T-16; `README.md:77` ("booking smoke/concurrency harnesses are in `scripts/`"), `docs/architecture.md:139` ("the executable harnesses in `scripts/`"), and `docs/setup-guide.md:266` and `:268` (implemented "booking smoke/concurrency scripts"; "use the real SQL Server tests and harnesses"); the removal is recorded in `docs/test/3-test.md:112-113` ("former PowerShell smoke check (removed from the implementation)").
- Triggering scenario and evidence: `ls scripts` reports no such directory and `git ls-files scripts` is empty. A reader following the README, architecture, or setup guide cannot run the referenced booking smoke/concurrency harnesses; the only remaining concurrency check is the integration-test command recorded in the test report, not the documented harness. The test report's operational evidence records runs of scripts that are no longer present, so that evidence is historical rather than reproducible from this checkout.
- Expected versus actual behavior: Documented manual/demo checks should point to commands or files that exist in the reviewed revision; the docs claim harnesses exist in `scripts/`, but the directory and files are absent.
- Impact and recommended correction: The documented clean-checkout/demo instructions are incomplete and misleading for the booking/concurrency portion of AC3-09/T-16. Restore the harnesses, or update README, architecture, and setup-guide to point to the automated integration tests and the manual Swagger/PowerShell booking and retrieval steps; state explicitly that the recorded smoke/concurrency runs used harnesses that were removed, and provide a reproducible concurrency command or script if human verification of T-16 is required.

### R-04 - Setup permission validation does not reject runtime database/object ownership

- Severity / status: Medium / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S3-03, S3-06, AC3-05, AC3-13; `src/ServiceScheduler.Api/Infrastructure/Setup/RuntimePrincipalProvisioner.cs:182-242` (`ValidateUserAsync` checks user type, SID, and role membership) and `:244-294` (`ValidateDatabasePermissionsAsync` checks only explicit `sys.database_permissions` rows); no query inspects database, schema, or object ownership. Required rule: `docs/research/3/03-permissions-migrations-and-operations.md:75` ("Reject ... database/object ownership, and role membership") and `docs/plans/3/phase-2-runtime-write-boundary.md:30` ("Reject ... ownership"); the ticket requires "no direct appointment writes, schema-altering privileges, or owner/admin role."
- Triggering scenario and evidence: A privileged operator runs `ALTER AUTHORIZATION ON OBJECT::dbo.CreateAppointment TO [scheduler_app]` (or makes the runtime principal the owner of `dbo.Appointments` or the database) and reruns setup. The runtime user remains a SID-matched SQL user with no role membership, and no explicit permission row falls outside the allowlist, so setup succeeds instead of rejecting the ownership. If the procedure is owned by `scheduler_app` while the table remains dbo-owned, ownership chaining is broken; the procedure's insert can then be blocked by the table `DENY`, surfacing later as a booking failure rather than a clear setup failure. The existing `RuntimePermissionTests` cover unexpected grants and role membership (`:242-306`) but not ownership.
- Expected versus actual behavior: Setup should reject runtime database/object ownership as part of the protected write boundary; actual validation accepts it because it inspects only permissions and roles.
- Impact and recommended correction: The setup-time guarantee that the runtime principal has no owner/admin state is incomplete, and an ownership change can silently break the procedure's write path. Add explicit checks that `scheduler_app` does not own the database, the `dbo` schema, `dbo.CreateAppointment`, or `dbo.Appointments` (for example via `sys.objects.principal_id`, `sys.schemas.principal_id`, and database owner metadata), and add fresh/repeated setup tests that apply and revoke `ALTER AUTHORIZATION` and assert a clear failure without leaving probes behind.

## Requirement coverage

> [!NOTE]
> The gap column records the review-time state. The 2026-11-02 re-review resolved R-01-R-04; see Re-review and resolution.

| Requirement ID | Evidence | Gap / remaining check |
| --- | --- | --- |
| S3-01 | POST/GET controller, DTOs, `Location`, UTC response mapping; `AppointmentApiTests`, `AppointmentValidationTests`, `AppointmentDstTests`; re-executed suites passed | Manual checklist pending |
| S3-02 | Procedure revalidation, eligibility/interval/duration tests, direct-call parity; re-executed suites passed | R-04 setup ownership validation gap; manual checklist pending |
| S3-03 | Same-owner procedure, technician-then-bay locking, runtime `EXECUTE`/DML denies; real SQL tests passed | R-04 |
| S3-04 | Candidate/deadlock/timeout/budget/no-replay tests; re-executed suites passed | R-01 (per-pair retry bound); R-02 (budget check after backoff) |
| S3-05 | Dealership-scoped GET, stored end/duration, restart and outage checks; re-executed suites passed | Manual checklist pending |
| S3-06 | OpenAPI/Problem Details, migration/setup/upgrade tests, docs and deploy notes | R-03 (nonexistent scripts references); manual checklist pending |
| AC3-01-AC3-13 | Automated and operational evidence recorded in [[test/3-test|the test report]] and reproduced for the automated suites during review | Manual execution remains pending for every acceptance label; R-01/R-02/R-04 affect the claimed S3-04/S3-03 bounds; R-03 affects AC3-09/T-16 documentation |

## Verification

| Command / check | Evidence source / revision | Observed result | Limitation |
| --- | --- | --- | --- |
| `dotnet restore`; `dotnet build --no-restore` | reviewed revision `a666927`; review run 2026-11-02 | Build succeeded; 0 warnings, 0 errors | None |
| `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | reviewed revision; review run 2026-11-02 | 133 passed, 0 failed | Matches the recorded count |
| `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | reviewed revision; review run 2026-11-02 | 154 passed, 0 failed, 2 m 04 s | Real SQL Server 2022 CU23 and Redis Testcontainers |
| `dotnet format --verify-no-changes` | reviewed revision; review run 2026-11-02 | No formatting changes required | None |
| `BookingAllocator.cs` inspection for R-01/R-02 | reviewed revision; static inspection | Retry counter resets per pair; no pre-call budget check after backoff | Findings are not covered by an executed failing test; they are policy deviations |
| `RuntimePrincipalProvisioner.cs` inspection for R-04 | reviewed revision; static inspection | No database/object ownership query; only permission rows and role membership | Ownership change not executed during review |
| Procedure session-state reset | `docs/plans/3/phase-1-database-foundation.md` step 3; `20261101161530_AddBookingProcedureAndTimeZoneMappings.cs` CATCH/success paths | Code resets `XACT_ABORT`, `LOCK_TIMEOUT`, and isolation on catchable errors and success | No executed assertion on a reused connection; plan step criterion is code-inspected only |
| `scripts/` and documentation inspection for R-03 | reviewed revision; static inspection | `scripts/` absent; README/architecture/setup-guide still reference harnesses | Historical operational evidence was not rerun |
| Compose startup, Redis/SQL outage, restart, clean-directory checks | [[test/3-test|Ticket 3 test report]] operational record | Recorded as Passed; not rerun during this review | Existing evidence; scripts used for the smoke/concurrency checks are no longer present (R-03) |
| Human manual checklist and Swagger UI/browser checks | [[test/3-test|Ticket 3 test report]] "Not run and limitations" | Not run | Generated later by `/orchestrator-ticket test 3`; ticket acceptance not claimed |
| Post-fix `dotnet build --no-restore` | Re-review; HEAD `ad774fb` plus fixes | Succeeded; 0 warnings, 0 errors | — |
| Post-fix unit suite | Re-review | 135 passed, 0 failed, 0 skipped | — |
| Post-fix integration suite | Re-review; real SQL Server 2022 CU23 and Redis Testcontainers | 157 passed, 0 failed, 0 skipped, 1 m 50 s | — |
| Post-fix `dotnet format --verify-no-changes` | Re-review | No formatting changes required | — |
| Post-fix retry, budget, and ownership tests | Re-review | `DeadlockRetryLimitIsGlobalAcrossCandidatePairs`, `BudgetExpiringDuringDeadlockBackoffStopsBeforeTheNextProcedureCall`, `SetupFailsWhenRuntimeUserOwnsAnApplicationObject` (table and procedure), and `SetupFailsWhenRuntimeUserOwnsTheApplicationDatabase` passed | `dbo` schema ownership cannot be altered in SQL Server; that branch is code-inspected |
| Post-fix script-reference inspection | Re-review | README/architecture/setup-guide/docs index no longer claim current harnesses; historical evidence is labeled removed | `scripts/` remains absent |

> [!NOTE]
> The test report records the implementation as a working tree on HEAD `90a6284`; the reviewed content is now committed as `a666927`. The recorded test counts and the reviewed revision match the executed suites. The 2026-11-02 re-review reran the build, format check, and both automated suites after the fixes; it did not rerun the Compose/operational checks, the removed smoke harnesses, or the human manual checklist.

## Re-review and resolution

- Re-review revision: working tree based on HEAD `ad774fb` with the review fixes; the reviewed ticket 3 implementation remains `a666927`.
- Re-review date: 2026-11-02.
- Commands: `dotnet build --no-restore`; `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build`; `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` against real SQL Server 2022 CU23 and Redis Testcontainers; `dotnet format --verify-no-changes`.
- Observed results: build succeeded with 0 warnings and 0 errors; 135 unit tests and 157 integration tests passed with 0 failed or skipped; formatting required no changes.

| Finding | Correction | Resolution evidence | Status |
| --- | --- | --- | --- |
| R-01 | `deadlockRetries` moved outside the candidate-pair loop so the retry allowance is request-wide. `DeadlockRetryLimitIsGlobalAcrossCandidatePairs` exercises a deadlock after a pair transition. | Unit suite passed; code inspection shows one request-wide counter. | Verified resolved |
| R-02 | The request budget is rechecked at the top of every procedure attempt, including after a deadlock backoff. `BudgetExpiringDuringDeadlockBackoffStopsBeforeTheNextProcedureCall` advances the fake clock during the backoff and asserts no second call. | Unit suite passed. | Verified resolved |
| R-03 | README, architecture, setup guide, docs index, deployment notes, and test records now point to Swagger UI and the automated tests; historical harness evidence is labeled as removed. | Documentation inspection; `scripts/` remains absent as intended. | Verified resolved |
| R-04 | `RuntimePrincipalProvisioner.ValidateOwnershipAsync` rejects runtime ownership of the application database and of `dbo.CreateAppointment`/`dbo.Appointments`. Setup tests apply and revoke database and object `ALTER AUTHORIZATION`. The `dbo` schema branch uses the same query but cannot be exercised because SQL Server rejects altering `dbo` ownership. | Integration suite passed, including both object cases and the database case. | Verified resolved |

## Outcome and follow-up

- Review conclusion: the booking endpoints, procedure, restricted write boundary, migration/setup validation, and automated test coverage are substantially implemented, and the re-review reproduced 135 unit / 157 integration passing tests after the fixes. Core API, time, interval, duration, retrieval, concurrency, permission, and outage behavior showed no data-integrity or contract failure in the executed checks.
- Open findings: none. R-01-R-04 were fixed and re-verified on 2026-11-02; the R-04 schema-ownership branch is code-inspected because SQL Server rejects altering `dbo` ownership.
- Required corrections: none outstanding for the review scope. The generated manual checklist remains unexecuted, so ticket acceptance is not claimed.
- Remaining verification: all human manual cases remain pending; the Compose outage/restart/clean-directory operational record is existing evidence and was not rerun here.
- Next phase: run `/orchestrator-ticket test 3` to regenerate or execute `docs/test/3-manual-test.md` for human verification, then record acceptance evidence.
- Re-review revision, finding IDs, and resolution evidence: 2026-11-02, working tree based on HEAD `ad774fb`; R-01-R-04 verified resolved in Re-review and resolution. The re-review changed implementation, tests, and documentation.
