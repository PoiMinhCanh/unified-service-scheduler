---
title: "4 - Review"
tags: [review, service-scheduler, observability, telemetry, prometheus, grafana, tracing]
ticket: 4
status: complete
updated: 2026-11-02
---

# 4 - Review

## Scope and baseline

- Ticket: [[4 - Observability|Ticket 4]].
- Research / plan: [[research/4/00-overview|Ticket 4 research overview]] with [[research/4/01-instrumentation-and-correlation|instrumentation and correlation]], [[research/4/02-telemetry-stack-and-dashboards|telemetry stack and dashboards]], and [[research/4/03-verification-workload-and-operations|verification and operations]]; [[plans/4/README|Ticket 4 implementation plan]] with phases 1-6; executed evidence in [[test/4-test|the ticket 4 test report]].
- Reviewed revision or diff: checkout HEAD `92a81efde5dac1070669fff13193e452dc9b6529` plus the staged ticket 4 working-tree change; the initial review covered 90 paths and 8,130 insertions. Review fixes R-01-R-07 were then applied in the working tree and re-verified; see Re-review and resolution for the corrected revision.
- Scope: application logging/correlation, OpenTelemetry registration and span/metric contracts, cache/availability/booking instrumentation, request middleware and Problem Details trace IDs, Collector/Prometheus/Tempo/Grafana configuration and provisioning, telemetry tests and fixtures, and the updated README/architecture/setup/deploy/test/database records.
- Exclusions and assumptions: local demo only; no production hosting, centralized logs, alerting, telemetry-UI authentication, schema/migration/procedure/permission change, cache key/payload/TTL change, booking algorithm/locking/retry change, or application UI. Manual Grafana visual inspection and the generated manual checklist remain outside this review.

> [!IMPORTANT]
> The automated evidence recorded in [[test/4-test|the ticket 4 test report]] was reproduced during this review on 2026-11-02: build and format passed, and 188 unit plus 175 integration tests passed after the fixes against real SQL Server, Redis, Collector, Prometheus, Tempo, and Grafana. No Critical or High finding was identified. Two Medium and five Low findings were fixed and re-verified on 2026-11-02; no findings remain open. The manual checklist and human Grafana inspection have not been executed, so ticket acceptance is not claimed.

## Findings

### R-01 - Telemetry integration tests embed expiring booking dates while the API uses the system clock

- Severity / status: Medium / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-06; `tests/ServiceScheduler.IntegrationTests/Telemetry/TelemetryDeliveryTests.cs:54` (availability `2026-11-10`), `:61` and `:67` (booking `2026-11-10`), `:181`/`:186` (`2026-11-11`), `:221` (`2026-11-13`), `:262` (`2026-11-12`), `:331` (`2026-11-13`), `:381` (`2026-11-17`), `:449`/`:455` (`2026-11-16`); `CreateTelemetryFactory` at `:512-538` calls `CreateFactory` without a clock. `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs:48-65` replaces the clock only when one is supplied; `src/ServiceScheduler.Api/Program.cs:104` otherwise registers `TimeProvider.System`; `src/ServiceScheduler.Api/Features/Availability/AvailabilityInterval.cs:17` rejects a start that is not strictly future.
- Triggering scenario and evidence: The test fixture seeds shifts from the fixed clock `2026-11-02T00:00:00Z` (`SqlServerContainerFixture.FixedClockUtc`), but the API under test uses the real clock. After the real date passes `2026-11-10T02:00:00Z`, the availability request at `2026-11-10T09:00:00+07:00` and the booking requests at the hard-coded starts return `400 validation_error` instead of the expected `200`/`201`/`409`; later methods fail on their own dates. The suite passes on 2026-11-02, so the failure is future-triggered rather than observed during this review.
- Expected versus actual behavior: The telemetry suite should be repeatable with its fixed synthetic dataset and clock; actual behavior couples the test outcome to the wall-clock date and expires within days.
- Impact and recommended correction: The recorded 175-test baseline is not durable, and CI/regression runs after 2026-11-10 will fail for environmental rather than product reasons. Pass a shared `FakeTimeProvider(SqlServerContainerFixture.FixedClockUtc)` through `CreateTelemetryFactory`/`CreateFactory` (or generate starts relative to the fixed app clock), then re-run the telemetry and full integration suites and record the revision/date. The test report's "fixed test clock" wording at `docs/test/4-test.md:28` should state which clock applies to the API and which to the seed.

### R-02 - Client cancellation is recorded as an internal 5xx request outcome

- Severity / status: Medium / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-01, S4-05, AC4-05, AC4-06; `src/ServiceScheduler.Api/Infrastructure/Telemetry/RequestLoggingMiddleware.cs:32-50` catches every exception, logs at `Error`, and records `ProblemCodes.InternalError` with the 5xx status class; `src/ServiceScheduler.Api/Infrastructure/Errors/SqlDependencyExceptionHandler.cs:19-21` deliberately declines to handle a cancellation when `RequestAborted` is cancelled.
- Triggering scenario and evidence: A client disconnects while a request is awaiting EF Core, Redis, or the booking procedure. The endpoint's `CancellationToken` is the request-aborted token, so the await throws `OperationCanceledException`; `SqlDependencyExceptionHandler` returns `false`, and the exception reaches the outer `RequestLoggingMiddleware` (registered before `UseExceptionHandler` at `Program.cs:140-143`) or is converted to a 500 Problem Details response. In either path, the completion event records `outcome="internal_error"` and `status_class="5xx"` and logs the cancellation as an Error with the exception; no test exercises this path.
- Expected versus actual behavior: Project conventions say "Treat cancellation separately from application faults"; actual behavior classifies an expected client disconnect as a server error.
- Impact and recommended correction: The 5xx/error-rate panel and outcome series are inflated by abandoned requests, and logs contain exception stack traces for normal client disconnects. Add a distinct branch for `OperationCanceledException when (context.RequestAborted.IsCancellationRequested)` that does not record an error-class outcome (or records a bounded cancellation outcome), logs at Information/Debug without the exception, and rethrows; add a unit test for the cancellation branch and an integration check if practical.

### R-03 - Telemetry integration tests leave created appointments in the shared class database

- Severity / status: Low / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-06; `tests/ServiceScheduler.IntegrationTests/Telemetry/TelemetryDeliveryTests.cs:61`, `:181`, `:186`, `:262`, `:331`, `:381` create appointments through the API and never delete them; `tests/ServiceScheduler.IntegrationTests/Fixtures/SqlServerTestBase.cs:7-9` requires mutating tests to remove their own rows; `docs/plans/4/phase-4-automated-telemetry-verification.md:62` states tests delete their own appointments.
- Triggering scenario and evidence: The class database is shared by all test methods in `TelemetryDeliveryTests`; each successful booking persists a row. The methods currently use distinct start times, so they pass, but a future method or repeated run that reuses a start can observe prior rows, and the plan's cleanup guarantee is not met. The database is dropped after the class, limiting the blast radius.
- Expected versus actual behavior: Tests should clean up their own appointments through the setup identity, as the test base and plan require; actual tests leave rows until the class database is dropped.
- Impact and recommended correction: Reduced test isolation and a plan deviation; final-state assertions such as `UnreachableSqlIsClassified...` compare a count captured at the start of that method, so they remain valid, but the leak is avoidable. Capture the returned appointment IDs and delete them in a `finally` block or class cleanup, and keep the final count assertion if needed.

### R-04 - Procedure-attempt metric misses unexpected exceptions from command execution

- Severity / status: Low / Verified resolved by inspection (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-03; `docs/plans/4/README.md:39` defines the `unexpected` procedure-attempt vocabulary for "an unmapped exception raised by the procedure call"; `src/ServiceScheduler.Api/Infrastructure/Persistence/SqlAppointmentBookingProcedure.cs:77-97` surrounds `ExecuteNonQueryAsync` only with `OperationCanceledException` and `SqlException` branches.
- Triggering scenario and evidence: A non-`SqlException` failure from `ExecuteNonQueryAsync` (for example an `InvalidOperationException` or driver state failure) propagates without recording `scheduler.booking.procedure.attempts{result="unexpected"}`; `BookingAllocator` then records the allocation as `internal_error` and the request is handled by the exception pipeline. The current tests cover a missing connection string and an unmapped `SqlException`, but not a non-SQL exception from the command.
- Expected versus actual behavior: The declared `unexpected` vocabulary should capture an unmapped exception raised at the procedure boundary; actual behavior records an attempt only for the connection-open and mapped/unmapped `SqlException` paths.
- Impact and recommended correction: The attempt counter undercounts the very failures the vocabulary is meant to expose; request/allocation metrics still show an internal error, so this is a diagnostic gap rather than a booking-correctness defect. Add a catch for non-cancellation `Exception` after the `SqlException` branches that records `unexpected` and rethrows, and cover it with a focused test or a documented boundary test.

### R-05 - Ticket 4 modifies prior ticket wording and a workflow template outside its scope

- Severity / status: Low / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: Scope and documentation ownership in `.claude/rules/ticket-workflow.md`; `docs/tickets/1 - Init Project.md:28` removes "demo scripts" from seeded-data wording; `docs/tickets/2 - Service Discovery and Availability.md:109` changes "Executable examples must discover actual seeded IDs and generate a future weekday start..." to "Automated API tests must..."; `docs/templates/research-template.md:141` changes "Actual scripts, commands" to "Actual commands and recorded results".
- Triggering scenario and evidence: The reviewed diff includes these edits even though no S4-01-S4-06 requirement or necessary dependency requires changing tickets 1 or 2 or the shared research template. The ticket 2 rewrite narrows a prior requirement, which can be read as adjusting earlier acceptance language rather than preserving the prior record.
- Expected versus actual behavior: A ticket change should keep findings and edits tied to its own scope and leave prior requirement records intact; actual ticket 4 changes prior ticket text without a recorded justification.
- Impact and recommended correction: Requirement history is harder to audit and prior acceptance language is weakened outside ticket 4. Revert these edits, or record an explicit ticket 4 justification and keep the original requirement wording intact; do not use a later ticket to alter an earlier ticket's contract.

### R-06 - Plan and documentation index report a blocked PowerShell script that the executed record says is not required

- Severity / status: Low / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-04, S4-06 documentation consistency; `docs/plans/4/README.md:17` and `:283` state "operational PowerShell script execution (`pwsh` unavailable) remain pending/Blocked" and "PowerShell script Blocked"; `docs/README.md:36` repeats the blocked status; `docs/test/4-test.md:140`, `docs/deploy-notes/4-deploy-notes.md` (Verification record), and `docs/setup-guide.md:24` state that no standalone verification script is required and the operational checks were executed manually.
- Triggering scenario and evidence: A reader checking the ticket status cannot tell whether a required operational check is blocked, omitted, or replaced by direct Compose checks. The test report and deploy notes describe the executed checks; the plan overview and index still carry the earlier blocked-script wording.
- Expected versus actual behavior: Status records should agree on whether a required check is blocked; actual records disagree.
- Impact and recommended correction: The conflicting records weaken the evidence handoff and may cause an unnecessary re-run or an incorrect completion judgment. Update the plan overview and documentation index to the test report's actual status (direct Compose checks executed; no standalone script required; human visual inspection and manual checklist pending).

### R-07 - Health-exclusion smoke check has no positive control and can pass before the Collector flushes

- Severity / status: Low / Verified resolved (2026-11-02); see Re-review and resolution.
- Requirement ID and file / symbol / line: S4-03, S4-V03; `tests/ServiceScheduler.IntegrationTests/Telemetry/TelemetryDeliveryTests.cs:484-509` sends health requests, waits 2 seconds, and asserts empty health series; the test factory exports metrics every 500 ms (`:526`) and `telemetry/collector.yaml:13-16` batches for up to 2 seconds.
- Triggering scenario and evidence: The test waits only 2 seconds after the health requests, while a health datapoint could take roughly 2.5 seconds to pass the metric export interval and the Collector batch. If the test runs before any health datapoint reaches Prometheus, the empty assertion passes even if the Collector `filter/health` processor were absent or broken; the test order within the class is not a contractual guarantee, and there is no positive control showing that a non-health datapoint from the same host has arrived.
- Expected versus actual behavior: A smoke check should prove a signal is exported and then prove the health signal is absent; actual behavior can prove absence before the pipeline has had time to deliver anything.
- Impact and recommended correction: The automated health-exclusion evidence can be a false positive, weakening S4-V02/S4-V03 and the "health traffic exclusion Passed" claim. Send a non-health request and wait until its metric/spans are queryable in Prometheus/Tempo, then query the health series; alternatively poll beyond the batch/export window and assert repeatedly with diagnostics.

## Non-blocking observations

- `src/ServiceScheduler.Api/Infrastructure/Telemetry/TelemetryRegistration.cs:64,67` sets `RecordException = true` for ASP.NET Core and SqlClient spans, but S4-V02 assertions inspect span attributes only, not span events. Current procedure/API error messages do not contain the synthetic identifiers, but exception messages and stack traces are unbounded; consider disabling exception recording or asserting span events as part of the data contract.
- `src/ServiceScheduler.Api/Infrastructure/Telemetry/LoggingSetup.cs` hard-codes Serilog minimum levels and category overrides while `appsettings.json` also defines `Logging:LogLevel`; changing the configuration file does not change Serilog's minimum level. Document the split or bind the Serilog levels to configuration.
- `src/ServiceScheduler.Api/Features/ServiceCatalog/ServiceCatalogService.cs:57` and `:66` use the literal `"service_catalog"` while `ServiceSchedulerTelemetry.CacheName` owns the same value; use the constant to prevent drift.
- `README.md:121` says the integration suite uses "one container per collection"; the setup guide (`docs/setup-guide.md:245`) correctly states one telemetry stack per telemetry test class. Align the README wording.

## Requirement coverage

> [!NOTE]
> Evidence below combines re-review automated checks (build, format, 188 unit, 175 integration) with recorded operational evidence in [[test/4-test|the test report]]. Human manual checks remain Not run.

| Requirement ID | Evidence | Gap / remaining check |
| --- | --- | --- |
| S4-01 | Serilog setup, `ActivityLogEnricher`, `ScopePropertyFilter`, request middleware, client-cancellation tests and Problem Details trace-ID tests; Compose JSON log correlation recorded | R-02 resolved; human log inspection pending |
| S4-02 | Tempo span inventory for HTTP/SQL/`availability.evaluate`/`booking.allocate`/cache; server-span and SQL sanitization assertions | No defect found; conflict trace legitimately lacks `booking.allocate` after the precheck |
| S4-03 | Prometheus metric names/labels/outcomes; unit listener tests; dashboard PromQL/provisioning checks | R-07 resolved; visual dashboard inspection pending |
| S4-04 | Pinned Compose stack, Collector/Prometheus validation, Grafana API provisioning, deploy notes/setup instructions | Visual Grafana inspection Not run; R-06 resolved |
| S4-05 | Telemetry outage/recovery, Redis degradation, unreachable-SQL checks; bounded export configuration; sanitization tests; client cancellation records `client_cancelled`/499 | R-02 resolved; span events not asserted (non-blocking) |
| S4-06 | 53 telemetry unit tests, 16 telemetry integration tests, Compose operational record | R-01/R-03 resolved; manual checklist pending |
| AC4-01-AC4-08 | All mapped to the tests above and to [[test/4-test\|the executed report]]; review-run automated suites passed | Manual Grafana/log inspection Not run for AC4-01/AC4-03/AC4-04/AC4-08; R-01/R-02 resolved |
| T-13, T-15 | Redis pause/degradation test and Compose check; delivery/backend/log-correlation tests | Human inspection pending |
| S4-V01-S4-V03 | Outage/recovery tests and Compose checks; provisioning/zero-traffic tests | R-01 resolved; repeatability re-verified; visual inspection pending |
| P-01, P-02 constraints | Booking/cache/procedure/locking/retry/permission code unchanged except instrumentation; full regression passed; cache key/TTL/budgets unchanged | None found |

## Verification

| Command / check | Evidence source / revision | Observed result | Limitation |
| --- | --- | --- | --- |
| `dotnet build --no-restore` | Review run 2026-11-02, HEAD `92a81ef` plus staged ticket 4 diff | Build succeeded; 0 warnings, 0 errors | Restore was not re-run; test report records the prior restore |
| `dotnet format --verify-no-changes --no-restore` | Review run 2026-11-02 | No formatting changes required | None |
| `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build` | Re-review run 2026-11-02 after fixes | 188 passed, 0 failed | Includes the new client-cancellation tests |
| `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Telemetry"` | Re-review run 2026-11-02 after fixes | 53 passed, 0 failed | Matches the updated telemetry subset count |
| `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` | Re-review run 2026-11-02 after fixes | 175 passed, 0 failed, 3 m 25 s | Real SQL Server/Redis and the pinned telemetry stack; the fixed-clock run exercises the R-01 correction |
| Static inspection of R-01-R-07 | Reviewed source/tests/config/docs before and after fixes | Findings R-01-R-07 fixed and re-checked; see Re-review and resolution | R-04's non-SQL command-failure branch is verified by code inspection; no deterministic test injects that exception |
| Compose startup/outage checks | Existing [[test/4-test\|ticket 4 test report]] | Recorded Passed for startup, provisioning, Redis/SQL/telemetry outages, log correlation, | Not re-executed during this review; manual Grafana visual inspection remains Not run |
| Manual checklist and human dashboard inspection | [[test/4-manual-test\|Checklist]] generated 2026-11-02; not executed | Not run | Required before ticket acceptance; human execution of the generated checklist remains pending |

## Re-review and resolution

- Re-review date and revision: 2026-11-02; HEAD `92a81efde5dac1070669fff13193e452dc9b6529` plus the working-tree ticket 4 change and review fixes. The fixes are unstaged relative to the staged ticket 4 diff.
- Commands and results: `dotnet build --no-restore` succeeded with 0 warnings/errors; `dotnet format --verify-no-changes --no-restore` reported no changes; the unit suite passed 188/188; the telemetry unit subset passed 53/53; the full integration suite passed 175/175 in 3m25s against real SQL Server/Redis and the pinned Collector/Prometheus/Tempo/Grafana stack.
- R-01: `TelemetryDeliveryTests.CreateTelemetryFactory` now passes `FakeTimeProvider(SqlServerContainerFixture.FixedClockUtc)`; the fixed-clock integration run passed. The test report now records the API/seed clock distinction.
- R-02: added `ClientCancellationExceptionHandler` and the `RequestLoggingMiddleware` cancellation branch; aborted requests record `client_cancelled`/499/`4xx` at Information, with no exception payload or 5xx outcome; new handler and middleware unit tests pass.
- R-03: `TelemetryDeliveryTests` tracks created appointment IDs in `PostBookingAsync` and deletes them in its `DisposeAsync` override through the setup identity; `CacheSqlServerTestBase.DisposeAsync` is virtual to permit the cleanup.
- R-04: `SqlAppointmentBookingProcedure` records `unexpected` for a non-cancellation, non-SQL exception from `ExecuteNonQueryAsync` and rethrows; verified by build and the existing procedure tests, with no deterministic non-SQL command-injection test.
- R-05: prior ticket 1/2 wording and the research template were restored; the working-tree diff contains no ticket 1/2/template changes relative to HEAD.
- R-06: the plan overview and documentation index now state that direct Compose checks passed and no standalone PowerShell script is required.
- R-07: the health-exclusion test now waits for a positive-control catalog request and its request/server-duration series before asserting health absence; the integration run passed.
- Remaining limitations: span events are still not asserted for S4-V02; log-level policy duplication and the cache-name literal remain non-blocking observations; manual checklist and human Grafana/log inspection are Not run.

## Outcome and follow-up

- Review conclusion: The ticket 4 implementation is substantially complete and consistent with the documented telemetry contract; the automated suites pass after the review fixes. No Critical or High defect was found. All seven findings (R-01-R-07) were fixed and re-verified on 2026-11-02; no findings remain open.
- Corrections applied: R-01 fixed-clock telemetry tests; R-02 client-cancellation handling; R-03 appointment cleanup; R-04 unexpected procedure-attempt catch; R-05 prior-ticket/template reverts; R-06 documentation status alignment; R-07 positive-control health exclusion. See Re-review and resolution.
- Remaining risks: S4-V02 span-event coverage is not asserted; log-level policy duplication and the cache-name literal remain non-blocking observations; telemetry-disabled behavior is covered by the full regression but not by a dedicated comparison test.
- Ticket acceptance is not established. The generated manual checklist and human Grafana/log inspection remain Not run; run `/orchestrator-ticket test 4` to generate the human checklist and complete the required manual evidence after the fixes are accepted.
