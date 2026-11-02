---
title: "2 - Review"
tags: [review, service-scheduler]
ticket: 2
status: complete
updated: 2026-11-02
---

# 2 - Review

## Scope and baseline

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research / plan: [[research/2/00-overview|Ticket 2 research overview]] with [[research/2/01-api-contracts-and-validation|API contracts and validation]], [[research/2/02-availability-and-scheduling|Availability and scheduling]], [[research/2/03-catalog-cache-and-resilience|Catalog cache and resilience]], and [[research/2/04-verification-and-documentation|Verification and documentation]]; [[plans/2/README|Ticket 2 implementation plan]] with phases 1-6.
- Reviewed revision or diff: checkout HEAD `3f8ab1e2cac26e54c5364f683d5df70cb1cfe5ff` plus the staged ticket 2 working tree; 112 changed paths (`git diff HEAD` fingerprint `3ed3134d9f1fb47ce0b100f31c6f0d33dd5400db9601be5e7ac0b982a38732f1`). The review covered staged additions/modifications; no separate committed ticket 2 revision exists.
- Scope, exclusions, and assumptions: reviewed `Program.cs`, `appsettings.json`, `Dockerfile`, the API project file, `Features/{Dealerships,ServiceCatalog,Availability}`, `Infrastructure/{Caching,Errors,Health,OpenApi}`, the ticket 2 unit/integration tests, `scripts/ticket2-demo.ps1`, Compose/`.env.example`/package pins, and the updated delivery docs. Ticket 3 booking writes, the booking procedure, and concurrency are excluded; ticket 4 telemetry is excluded; no schema migration is expected. The `dotnet format` using-directive reordering in ticket 1 files is formatting-only and is not reported as a ticket 2 defect.
- Review method: static inspection, re-execution of both test projects from the recorded build, and a runtime fetch of `/openapi/v1.json` from the built API with unreachable SQL and Redis. No repository files were modified during the review.

> [!IMPORTANT]
> The original review reproduced the then-recorded counts (98 unit and 90 integration tests) but did not rerun Compose, the demo script, `dotnet format --verify-no-changes`, an execution plan, or the human manual checklist. The post-fix re-review reran build, format, both suites, the runtime OpenAPI document, Compose outage/recovery, and the demo script; 98 unit and 92 integration tests passed. The human manual checklist and execution-plan capture remain pending, so ticket acceptance is not yet complete.

## Findings

> [!NOTE]
> All findings below were fixed and re-verified on 2026-11-01. The finding text records the review-time defect; the resolution evidence is in Re-review and resolution.

### R-01 - OpenAPI documents required availability query parameters as optional

- Severity / status: Medium / Verified resolved
- Requirement ID and file / symbol / line: S2-02, S2-06, AC2-01; `src/ServiceScheduler.Api/Features/Availability/AvailabilityController.cs:20-24` (`Guid serviceTypeId`, `string? startsAt`), `src/ServiceScheduler.Api/Infrastructure/OpenApi/ApiOperationTransformer.cs:45-47` (sets descriptions only), `tests/ServiceScheduler.IntegrationTests/OpenApi/OpenApiTests.cs:83-86` (asserts description text, not `required`).
- Triggering scenario and evidence: The ticket says `serviceTypeId` and `startsAt` are required. A runtime `GET /openapi/v1.json` from the built API returned HTTP 200; `paths./api/dealerships/{dealershipId}/availability.get.parameters` contains `serviceTypeId` and `startsAt` with no `required` entry, while `dealershipId` is `required: true`. The OpenAPI test only asserts the `%2B`/offset description; no `required` assertion exists.
- Expected versus actual behavior: Both query parameters should be documented as required; generated clients can treat them as optional even though the server returns `400 validation_error` when they are missing.
- Impact and recommended correction: The published contract does not match the ticket's required-input contract (AC2-01), and generated clients can omit the parameters and fail at runtime. Set the parameters' OpenAPI `Required` flag in the transformer (or bind a query DTO with required properties), keep the explicit validation, and assert `required: true` in `OpenApiTests`.

### R-02 - T-02 tests do not isolate inactive-technician and wrong-service qualification

- Severity / status: Medium / Verified resolved
- Requirement ID and file / symbol / line: S2-03, AC2-02, T-02; `tests/ServiceScheduler.IntegrationTests/Availability/AvailabilityEligibilityTests.cs:44-106` (unqualified case) and `:108-160` (inactive case); `src/ServiceScheduler.Api/Infrastructure/Setup/FutureShiftSeeder.cs:24-31` (seeds shifts only for active technicians); `src/ServiceScheduler.Api/Infrastructure/Persistence/Configurations/SeedData.cs:143-149` and `:180-185` (seeded inactive Saigon technician has an Oil Change qualification and no shift); `src/ServiceScheduler.Api/Features/Availability/AvailabilityQueries.cs:64-66` (`IsActive` and service-qualification predicates).
- Triggering scenario and evidence: Removing `technician.IsActive` from `TechnicianCandidates` would not change `InactiveResourcesNeverSatisfyAvailability`: the seeded inactive technician has no shift, so the shift-coverage predicate excludes it anyway, and the test's appointment blocks the seeded active technician and active bay, so the test only proves the inactive-bay filter. Removing `qualification.ServiceTypeId == serviceTypeId` would not change `UnqualifiedTechnicianNeverSatisfiesAvailability`: the probe technician has no qualification row at all, so the inner join excludes it regardless of the service match.
- Expected versus actual behavior: The tests should fail if inactive-technician or wrong-service qualification filtering regresses; they can pass without exercising those predicates.
- Impact and recommended correction: T-02/AC2-02 evidence does not establish the inactive-technician or service-qualification rules. Seed an inactive technician that is qualified and has a covering shift (and block any active alternative), and a wrong-service-qualified active technician with a covering shift, then assert `available:false`; add an inactive-bay isolation case as well.

### R-03 - Cache budget tests do not verify the documented 250 ms / 500 ms bounds

- Severity / status: Medium / Verified resolved
- Requirement ID and file / symbol / line: S2-05, AC2-05, T-13; `tests/ServiceScheduler.UnitTests/Caching/BoundedCacheTests.cs:43-58` (20 ms budget asserted only as `< 2 s`), `tests/ServiceScheduler.IntegrationTests/Resilience/RedisOutageTests.cs:51` (catalog fallback asserted as `< 5 s`), `:112-114` (five concurrent stalled requests asserted as `< 20 s`); documented budget at `docs/database/cache.md` and `docs/architecture.md`.
- Triggering scenario and evidence: A regression that makes a cache get/set wait 4 s, or a stalled request take 19 s, would still pass every existing assertion, while the ticket requires the catalog result "within the documented cache budget" and the report claims it.
- Expected versus actual behavior: Tests should bound the wait near the configured 250 ms per operation and 500 ms read+populate (with a stated tolerance); actual assertions are one to two orders of magnitude looser.
- Impact and recommended correction: AC2-05/T-13 evidence is weaker than the report claims and cannot detect a budget regression. Tighten the unit assertion around the injected budget and the integration fallback/stall assertions to a small multiple of the configured budget, or measure the cache-attributable portion separately and record the observed budget.

### R-04 - Corrupt-payload repair exceeds the documented 500 ms cumulative cache wait

- Severity / status: Low / Verified resolved
- Requirement ID and file / symbol / line: S2-05, AC2-05; `src/ServiceScheduler.Api/Features/ServiceCatalog/ServiceCatalogService.cs:59-66` and `:92-96` (get, remove on `JsonException`, then set); `src/ServiceScheduler.Api/Infrastructure/Caching/BoundedCache.cs:26-85` (each operation uses `OperationTimeout`); `docs/database/cache.md` and `docs/architecture.md` state a 500 ms read+populate maximum.
- Triggering scenario and evidence: A cached corrupt JSON payload with Redis responsive for the get but slow or stalled for remove/set performs three bounded cache operations; the worst-case caller wait is 250 ms get + 250 ms remove + 250 ms set = 750 ms, not 500 ms.
- Expected versus actual behavior: The documented maximum cumulative cache wait is 500 ms; the repair path can consume 750 ms.
- Impact and recommended correction: The resilience budget is understated for a supported repair path. Either share one per-request cache deadline across get/remove/set and document the shared maximum, or document and test the 750 ms repair-path maximum.

### R-05 - SQL-shape tests and docs overstate the asserted predicates

- Severity / status: Low / Verified resolved
- Requirement ID and file / symbol / line: S2-03, T-03, T-04, T-11; `tests/ServiceScheduler.IntegrationTests/Availability/AvailabilitySqlTests.cs:15-92`; `docs/database/schema.md`; `docs/test/2-test.md` TC2-25.
- Triggering scenario and evidence: `AvailabilitySqlTests` asserts table/column names, `NOT EXISTS`, `[Status]`, and `ORDER BY`, but does not assert the dealership equality predicates, `IsActive`, the half-open `<`/`>` comparisons, the shift `<=`/`>=` bounds, or the parameter values. The docs say the test asserts dealership scope, shift coverage, and the half-open comparisons. Behaviorally, no test isolates a shift that starts after the requested start (only the end side); the opening start side is only indirectly exercised by the adjacent-row test.
- Expected versus actual behavior: The documented SQL-shape evidence should assert those predicates; actual tests can pass if a predicate is removed or reversed as long as the referenced column names remain.
- Impact and recommended correction: T-04/T-11 evidence and the schema/test-report claims are overstated. Assert the comparison operators and key parameterized predicates in `ToQueryString()`, add a behavioral shift-start case, and correct the docs if the tests remain behavior-only.

### R-06 - Pure availability helpers are unused by production and duplicate the SQL rules

- Severity / status: Low / Verified resolved
- Requirement ID and file / symbol / line: S2-03; `src/ServiceScheduler.Api/Features/Availability/ShiftCoverage.cs`, `HalfOpenOverlap.cs`, `OpeningCoverage.cs` (`IsWithin`); production SQL at `src/ServiceScheduler.Api/Features/Availability/AvailabilityQueries.cs`.
- Triggering scenario and evidence: Reference search shows `ShiftCoverage.IsCovered`, `HalfOpenOverlap.Overlaps`, and `OpeningCoverage.IsWithin` are referenced only by unit tests; `AvailabilityService` uses `OpeningCoverage.TryGetLocalInterval` but not `IsWithin`, and the authoritative predicates are in the SQL queries. The unit tests can stay green while the SQL predicates drift.
- Expected versus actual behavior: The pure rules should either drive the production decision or be clearly test-only specifications linked to SQL assertions; actual helpers duplicate unused logic.
- Impact and recommended correction: False confidence and a maintenance hazard, with no current behavior defect observed. Remove the unused helpers, or keep them as documented specifications and add SQL-shape tests/comments tying them to `AvailabilityQueries`.

### R-07 - README's executable availability example embeds an expiring date

- Severity / status: Low / Verified resolved
- Requirement ID and file / symbol / line: S2-06, AC2-01, AC2-07; `README.md` availability example; also `src/ServiceScheduler.Api/Infrastructure/OpenApi/ApiOperationTransformer.cs` uses a fixed `2026-11-03` example in the parameter description.
- Triggering scenario and evidence: The README runs `.../availability?serviceTypeId=<serviceTypeId>&startsAt=2026-11-03T03%3A00%3A00Z` with placeholder IDs; after 2026-11-03 the timestamp is past and the server returns `400 validation_error`, so the inline example no longer demonstrates availability. The self-discovering script is documented separately, but the primary inline example still embeds an expiring date.
- Expected versus actual behavior: The ticket requires executable examples to generate a future weekday start rather than embed an expiring date; the inline example embeds one.
- Impact and recommended correction: A new user following the README after the date gets a validation failure. Replace the literal with the script-based example or a generated future start, and make the OpenAPI description example clearly illustrative or relative.

### R-08 - README documents a `pwsh` invocation the script's error handling does not support

- Severity / status: Low / Verified resolved for the documented path; PowerShell 7 remains unverified
- Requirement ID and file / symbol / line: S2-06, AC2-01; `README.md` demo-script commands, `scripts/ticket2-demo.ps1` error-body extraction, `docs/test/2-test.md` execution record.
- Triggering scenario and evidence: Under PowerShell 7, `Invoke-WebRequest` throws `HttpResponseException` whose `Response.Content` is an `HttpContent`; the script assigns that object to `$body` and later passes it to `ConvertFrom-Json` for the unknown-dealership and validation examples. The `GetResponseStream` fallback is not available on `HttpResponseMessage`, so the 404/400 checks are likely to fail. The recorded probe ran under Windows PowerShell 5.1; `pwsh` was unavailable in the review environment, so this is static analysis, not an executed failure.
- Expected versus actual behavior: The README advertises both `pwsh` and Windows PowerShell 5.1; only 5.1 is verified, and the PS7 error path is inconsistent.
- Impact and recommended correction: The documented executable example path is unverified and likely broken under PS7 (AC2-01/AC2-07). Use `$_.ErrorDetails.Message` when present or read `Content.ReadAsStringAsync()`, test under `pwsh`, or document PowerShell 5.1 only.

## Requirement coverage

> [!NOTE]
> The gap column records the review-time gaps. All R-01-R-08 gaps were fixed and re-verified; see Re-review and resolution.

| Requirement ID | Evidence | Gap / remaining check |
| --- | --- | --- |
| S2-01 | `DealershipsController` and ordered dealership read; `ServiceCatalogController` and `ServiceCatalogService`; `DealershipDiscoveryTests`, `CatalogContractTests`, `CatalogCacheTests`; re-run integration suite passed. | None for behavior; R-01 concerns the availability OpenAPI parameters, not S2-01. |
| S2-02 | `StartsAtParser`, `AvailabilityInterval`, `OpeningCoverage`, `AvailabilityService`; `AvailabilityTimeTests`, `AvailabilityDstTests`, `AvailabilityDurationTests`; re-run. | R-01 required-parameter documentation; no test uses an ambiguous fall-back opening boundary, so the documented repeated-boundary policy is not directly asserted. |
| S2-03 | `AvailabilityQueries`; `AvailabilityEligibilityTests`, `AvailabilityCoverageTests`, `AvailabilityConflictTests`, `AvailabilityScopeTests`, `AvailabilityReadOnlyTests`; re-run. | R-02 inactive-technician/wrong-service isolation; R-05 SQL predicate and shift-start evidence; R-06 unused pure helpers. |
| S2-04 | `ProblemCodes`, `ProblemResponse`, `SqlDependencyExceptionHandler`, `SqlFailureClassifier`, `InvalidModelStateResponseFactory`; `ProblemDetailsTests`, `AvailabilityFailureTests`, `SqlOutageTests`; re-run. | None observed; SQL 258 classification is a documented deviation. |
| S2-05 | `BoundedCache`, `CacheOptions*`, `RedisCacheConfiguration`, `CacheHealthCheck`, `ServiceCatalogService`, `ServiceCatalogCacheKeys`; `CatalogCacheTests`, `RedisOutageTests`, `CacheHealthTests`, `CacheConfigurationTests`; re-run. | R-03 budget assertions; R-04 repair-path budget; the key/TTL are exercised through the production builder and configuration. |
| S2-06 | Updated README/setup/architecture/database/cache/schema/migration/deploy records; script; OpenAPI; re-run counts. | R-05 documentation overstatement; R-07 expiring README example; R-08 PS7 path; manual checklist pending. |
| AC2-01 | Runtime `/openapi/v1.json` contains the three operations and DTO/ProblemDetails schemas; OpenAPI tests; script report. | R-01 required flags; R-07 and R-08; the test does not assert DTO schemas even though the runtime document contains them. |
| AC2-02 | Unit, API, and SQL tests; re-run. | R-02 and R-05; DST ambiguous-boundary limitation. |
| AC2-03 | `AvailabilityDurationTests`, `AvailabilityReadOnlyTests`, response assertions; re-run. | None observed. |
| AC2-04 | `CatalogCacheTests` miss/hit/expiry/invalidation/isolation against real Redis and SQL Server; re-run. | The key is asserted through the production builder, not an independent literal; code/docs match. |
| AC2-05 | `RedisOutageTests`, `CacheHealthTests`, `CacheConfigurationTests`, `CombinedOutageTests`; Compose report. | R-03 and R-04; Compose checks not rerun. |
| AC2-06 | `SqlOutageTests`, `CombinedOutageTests`, `CacheHealthTests`; re-run. | None observed. |
| AC2-07 | Docs and test report updated; re-run counts. | R-05, R-07, R-08; manual checklist pending; the report marks acceptance pending. |
| T-02 | `AvailabilityEligibilityTests`; re-run. | R-02. |
| T-03 | `AvailabilityCoverageTests`; re-run. | R-05: shift-start side not behaviorally isolated; opening start side only indirect. |
| T-04 | `AvailabilityConflictTests`, `HalfOpenOverlapTests`, `AvailabilitySqlTests`; re-run. | R-05 SQL-shape assertions. |
| T-10 | Parser/interval/opening unit tests; `AvailabilityTimeTests`, `AvailabilityDstTests`; re-run. | Ambiguous fall-back opening boundary not directly asserted; R-01 for required-parameter docs. |
| T-11 | `AvailabilityScopeTests`, `CatalogContractTests`, `CatalogCacheTests`; re-run. | No foreign-technician candidate can be seeded as a valid row because the qualification composite FKs tie the technician and service type to the same dealership; bay scope is indirectly covered; catalog keys remain dealership scoped. |
| T-12 | `CatalogCacheTests`; re-run. | The post-recovery request is checked only as a 200 response, not proven to avoid SQL; key literal/TTL not independently asserted. |
| T-13 | `RedisOutageTests`, `CacheHealthTests`, `CacheConfigurationTests`; re-run. | R-03; Compose not rerun. |
| A-02 | Availability read path, SQL duration/opening/shift/conflict rules; re-run. | None observed beyond the S2-02/S2-03 test-evidence gaps. |
| P-02 | Redis cache-aside, fallback, and health behavior; re-run. | R-03 and R-04. |

## Verification

| Command / check | Evidence source / revision | Observed result | Limitation |
| --- | --- | --- | --- |
| `git status --porcelain`; `git diff HEAD --name-status` | HEAD `3f8ab1e` plus staged worktree | 112 changed paths; the set matches the requested ticket 2 scope. | Every documentation line was not audited; findings cite the inspected claims. |
| `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --nologo` | Existing Debug build; build artifact postdates the sources | 98 passed, 0 failed, 0 skipped. | Existing build, not a clean rebuild. |
| `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --nologo` | Existing Debug build; Docker 25.0.3; real SQL Server and Redis Testcontainers | 90 passed, 0 failed, 0 skipped. | Existing build; the passing suite is evidence for the R-02/R-03 false-pass analysis. |
| Start built API with unreachable `ConnectionStrings:Runtime`/`ConnectionStrings:Cache`; `GET /openapi/v1.json` | Built API DLL, Development | HTTP 200; three operations and DTO/ProblemDetails schemas present; `serviceTypeId` and `startsAt` lack `required: true` (R-01). | No SQL/Redis needed for the OpenAPI fetch; not a Compose run. |
| `docs/test/2-test.md` execution record | Existing report | Claims 98/90 and the Compose, SQL/Redis, and demo checks; counts match the re-run. | Compose, demo, format, execution plan, and manual checklist were not rerun in the review. |
| `dotnet format --verify-no-changes` | Not run during review | — | Not run; the implementation report claims it passed. |
| Compose `up`/`stop redis`/`stop sqlserver`/`start` checks | Report only | Claimed passed | Not rerun during review. |
| `scripts/ticket2-demo.ps1` | Report only; Windows PowerShell 5.1 | Claimed exit 0 | Not rerun; `pwsh` unavailable; R-08 is static only. |
| Post-fix `dotnet build --no-restore`; `dotnet format --verify-no-changes` | Re-review build | Build succeeded with 0 warnings/errors; format verification passed. | — |
| Post-fix `dotnet test` unit suite | Re-review | 98 passed, 0 failed, 0 skipped. | Existing build; no clean rebuild. |
| Post-fix `dotnet test` integration suite | Re-review; real SQL Server and Redis Testcontainers | 92 passed, 0 failed, 0 skipped. | — |
| Post-fix runtime `/openapi/v1.json` | Rebuilt Compose API | `dealershipId`, `serviceTypeId`, and `startsAt` reported `required: true`; the three response DTO and `ProblemDetails` schemas were present. | — |
| Post-fix Compose Redis stop/start and SQL stop/start | Re-review; `docker compose stop/start` | Redis down: readiness `200` Degraded, catalog/availability `200`; recovery `Healthy`. SQL down: discovery/availability `503 dependency_unavailable`, catalog hit `200`, readiness `503`, liveness `200`; recovery `Healthy`/`200`. | Local Compose only. |
| Post-fix `powershell -File scripts/ticket2-demo.ps1` | Re-review; Windows PowerShell 5.1 | Discovered IDs, computed a future weekday start, verified the equivalent offset, printed `not_found`/`validation_error`, exited `0`. | PowerShell 7 not available. |
| Manual checklist | Not generated/run | — | Ticket acceptance remains open. |

## Re-review and resolution

- Re-review revision: HEAD `3f8ab1e` plus the post-fix working tree/index; final tracked `git diff HEAD` fingerprint `fe1df1c78f1dd3b87d49d965648109bc4474df820f6d425ee5251e3d9f78468f`; 112 tracked changed paths plus this review file.
- Re-review date: 2026-11-01.
- Commands: `dotnet build --no-restore`; `dotnet format --verify-no-changes`; both `dotnet test` projects; rebuilt Compose API and `/openapi/v1.json` fetch; Compose Redis/SQL stop/start checks; PowerShell 5.1 demo script. All passed as recorded in the Verification table above.
- Follow-up check 2026-11-02: no finding is open. The `scripts/ticket2-demo.ps1` file cited by R-08 is no longer present in the checkout after the ticket 3 harness cleanup; its recorded PowerShell 5.1 run remains historical evidence, and the current setup guide directs manual checks to Swagger UI.

| Finding | Correction | Resolution evidence | Status |
| --- | --- | --- | --- |
| R-01 | `ApiOperationTransformer` marks `dealershipId`, `serviceTypeId`, and `startsAt` required; `OpenApiTests` asserts the flags and response DTO schemas. | Runtime OpenAPI check reported all three `required: true`; `OpenApiTests` passed. | Verified resolved |
| R-02 | Eligibility tests now use a wrong-service qualification, an inactive technician with a covering shift, and an inactive-bay-only scenario. | `AvailabilityEligibilityTests` passed in the 92-test integration run. | Verified resolved |
| R-03 | Unit budget assertion tightened to 15-750 ms; closed-port fallback under 3 s; single paused request under 3 s; five concurrent requests under 10 s. | Unit and integration suites passed. | Verified resolved |
| R-04 | `docs/database/cache.md` and `docs/architecture.md` state the 750 ms corrupt-payload repair maximum alongside the 500 ms normal path. | Documentation review; corrupt-payload API test passed. | Verified resolved |
| R-05 | `AvailabilitySqlTests` asserts the dealership/active/service predicates and shift-bound/half-open operators; a shift-start behavioral test was added. | SQL and coverage tests passed in the integration suite. | Verified resolved |
| R-06 | Pure helpers are documented as specification mirrors of the SQL predicates; `AvailabilityQueries` cross-references them. | Comments reviewed; SQL-shape tests passed. | Verified resolved |
| R-07 | README uses a future-start placeholder and the self-discovering script; the OpenAPI `startsAt` description no longer embeds a dated example. | Documentation review; script rerun exited `0`. | Verified resolved |
| R-08 | Script error extraction handles `ErrorDetails.Message`, PowerShell 7 `HttpContent.ReadAsStringAsync()`, and the stream fallback; README documents the verified Windows PowerShell 5.1 command. | Demo script rerun under Windows PowerShell 5.1 exited `0`; PowerShell 7 remains unverified. | Verified resolved for the documented path; PS7 unverified |

## Pre-existing and out-of-scope observations

- The first `/health/ready` call immediately after an API restart can be `Unhealthy` while the existing SQL `DbContext` health check warms; this is ticket 1 behavior noted in the ticket 2 report, not introduced by ticket 2.
- The `dotnet format` using-directive reordering in ticket 1 files is formatting-only and was excluded.
- No CI workflow exists, and the ticket does not require one. Execution-plan capture on representative volume and the human manual checklist remain unrun, as the report states.
- T-11's foreign-technician case cannot be constructed as a valid row because the qualification composite FKs tie the technician and service type to the same dealership; the current scope test relies on the seeded London bay for indirect bay-scope coverage and does not isolate a foreign technician.
- The DST tests exercise spring-forward/nonexistent and fall-back intervals, but no test uses an ambiguous fall-back opening boundary, so the documented repeated-boundary policy is not directly asserted.
- TC2-32's test asserts that the configuration key appears in the startup error, not explicitly that no credential value appears; the inspected validator message contains only the key name.

## Outcome and follow-up

- Review conclusion and remaining risks: No Critical or High findings. The core S2-01-S2-06 behavior is present, and the post-fix suites pass (98 unit and 92 integration tests); AC2-03, AC2-04, and AC2-06 are well supported. All eight review findings were fixed and re-verified; the only documented residual is that PowerShell 7 execution of the demo script remains unverified while the verified Windows PowerShell 5.1 path passes.
- Required corrections or checks: none outstanding for the automated/operational scope. Ticket acceptance still requires the human manual checklist generated by `/orchestrator-ticket test 2`; an execution-plan capture remains an explicit non-blocking limitation.
- Re-review revision, finding IDs, and resolution evidence: post-fix working tree (final tracked diff fingerprint `fe1df1c78f1dd3b87d49d965648109bc4474df820f6d425ee5251e3d9f78468f`), R-01-R-08 verified resolved as recorded in Re-review and resolution. The post-fix commands and observed results are also recorded in [[test/2-test|the ticket 2 test report]].
