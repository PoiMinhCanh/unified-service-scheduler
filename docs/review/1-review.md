---
title: "1 - Review"
tags: [review, service-scheduler]
ticket: 1
status: complete
updated: 2026-11-02
---

# 1 - Review

## Scope and baseline

- Ticket: [[1 - Init Project]].
- Research: [[research/1/00-overview|Foundation research overview]], with [[research/1/01-platform-foundation|platform]], [[research/1/02-persistence-and-identity|persistence and identity]], and [[research/1/03-testing-and-verification|testing and verification]].
- Plan: [[plans/1/README|Ticket 1 implementation plan]] and phases 1-5.
- Reviewed revision or diff: working tree based on HEAD `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` with the uncommitted ticket 1 implementation; the API/setup images were built from the implementation tree at 2026-10-31T09:40Z (the migration file was edited at 09:45Z), and the current compiled API assembly (09:49Z) was used for a final setup re-check. No review of historical commits is implied.
- Scope, exclusions, and assumptions: ticket 1 foundation only. Booking endpoints and the booking procedure, Redis, Serilog/OpenTelemetry, authentication, and administrative CRUD are excluded and were not reviewed as implemented behavior. The formal test matrix remains the test phase's responsibility; review-executed checks below do not replace [[test/1-test|the ticket 1 test report]].
- Prerequisite check: research verification is **Passed** for the ticket and research-part fingerprints. Plan verification is **Passed** for the pre-implementation plan; implementation then updated the plan phase statuses and the README/setup-guide documentation cited by both verification records, so their recorded fingerprints no longer match. Those updates are implementation records required by the plan, not changes to the verified requirements or plan design; this review uses the verified research and plan as the design baseline and does not claim a fresh verification result.
- Input freshness:

| Input | Current check | Note |
| --- | --- | --- |
| `docs/tickets/1 - Init Project.md` | SHA-256 `2181f6ec...` matches both verification records | Ticket unchanged. |
| `docs/research/1/01-platform-foundation.md`, `02-persistence-and-identity.md`, `03-testing-and-verification.md` | SHA-256 matches the plan-verification record | Research parts unchanged. |
| `docs/research/1/00-overview.md` | SHA-256 `7022c907...` matches the post-research-verification state recorded by plan verification | Research overview unchanged since plan verification. |
| `docs/plans/1/README.md`, phases 1-5, `README.md`, `docs/setup-guide.md` | SHA-256 differs from the plan-verification record | Post-verification implementation status and documentation updates; see the prerequisite note. |

> [!NOTE]
> If the workflow requires fingerprint-level freshness for the changed README/setup-guide inputs, run `/orchestrator-ticket verify research 1` and then `/orchestrator-ticket verify plan 1` before treating this review as gate-compliant. This review did not silently re-verify either phase.

## Findings

### R-01 - Verification claims in the database record and plan exceed the recorded execution evidence

- Severity / status: Low / Verified resolved (2026-11-02). The ticket 1 test phase executed the matrix; see Re-review and resolution.
- Requirement ID and file / symbol / line: S11, AC-9, T-16; `docs/database/schema.md:9,81`; `docs/plans/1/README.md:184-188`; `docs/test/1-test.md:5,12`; `docs/deploy-notes/1-deploy-notes.md:79`.
- Triggering scenario and evidence: `docs/database/schema.md:9` says the migration was applied "in the ticket 1 integration suite" and line 81 says invalid inserts and catalog assertions were executed during implementation. The plan progress table says phases 1-4 were "Executed during implementation." The test report status is `not-run`, its note says no case is marked Passed or Failed, and its execution record is empty. The deploy notes record a manual local run (setup exit `0`, endpoint checks, runtime read and denied writes, migration history, row counts) but no `dotnet test`, `dotnet format`, or invalid-insert/index assertion output.
- Expected versus actual behavior: Records should distinguish manual implementation checks from formal xUnit suite execution and should not imply the suite ran before [[test/1-test|the test report]] records it; the current wording can be read as the integration suite having executed and passed.
- Impact and recommended correction: Evidence traceability is weakened for AC-2, AC-3, and T-16, and a reader may overstate verification. Reword the schema and plan statements to "manual implementation checks recorded in deploy notes; formal suite Not run", or let the test phase's execution record replace the claims. Do not mark any case Passed until it is recorded.

### R-02 - Architecture retains pre-implementation statements about version compatibility

- Severity / status: Low / Verified resolved (2026-11-02). The architecture now records completed version pinning and the remaining verification gaps.
- Requirement ID and file / symbol / line: S3, S11, AC-9; `docs/architecture.md:66,293`.
- Triggering scenario and evidence: `docs/architecture.md:66` says the design "does not claim that version compatibility has already been checked", and line 293 still says to "Resolve and pin compatible stable SDK, NuGet, and container versions during scaffolding." The same document's status and implemented-foundation section (lines 72-84), `global.json`, `Directory.Packages.props`, and the Dockerfile/Compose pins show that scaffolding is complete and documented as locally exercised.
- Expected versus actual behavior: The architecture should distinguish the current implemented and pinned state from remaining unverified compatibility checks; the text repeats design-time caveats without a current status.
- Impact and recommended correction: Minor contradiction under AC-9/S11. Update the two statements to record completed scaffolding and list the compatibility checks still outstanding, such as the clean-checkout command run and any unexecuted image/package combinations.

### R-03 - Documented `dotnet ef migrations list` command lacks a usable setup connection or `--no-connect`

- Severity / status: Low / Verified resolved (2026-11-02). The documented command now uses `--no-connect` and explains the applied-status alternative.
- Requirement ID and file / symbol / line: S4, S11, AC-1; `docs/setup-guide.md:197-201`; `src/ServiceScheduler.Api/Infrastructure/Persistence/SchedulerDbContextFactory.cs:13-26`.
- Triggering scenario and evidence: The setup guide's migration-inspection block runs `dotnet ef migrations list` without exporting `ConnectionStrings__Setup` and without `--no-connect`. The design-time factory falls back to `Server=localhost,1433; ... Password=DesignTimeOnly1!; Connect Timeout=3` when no environment value is present. `migrations list` connects by default to report applied migrations, so a developer who has not exported the setup connection gets a connection or login failure; `migrations script` does not have this problem.
- Expected versus actual behavior: The documented command should work from the documented state; the fallback placeholder identity is not the local SQL credential.
- Impact and recommended correction: An optional development command fails in the common path; low practical impact. Add `--no-connect` to `migrations list`, or instruct exporting `ConnectionStrings__Setup` and using the local SA password, noting that `.env` is not loaded by `dotnet ef`.

### R-04 - `.env.example` duplicates credentials without telling the user to update every occurrence

- Severity / status: Low / Verified resolved (2026-11-02). The example and both setup instructions now call out all four occurrences, with a troubleshooting row.
- Requirement ID and file / symbol / line: S4, S8, AC-1, AC-8; `.env.example:8-13`; `README.md:122-126`; `docs/setup-guide.md:105-109`.
- Triggering scenario and evidence: The two placeholder passwords appear in the top-level `MSSQL_SA_PASSWORD` and `Scheduler__RuntimePassword` values and again inside `ConnectionStrings__Setup` and `ConnectionStrings__Runtime`. The README and setup guide say to "replace both placeholder passwords"; a user who edits only the two variable lines leaves the connection strings with the old placeholders. Compose then starts SQL with the new SA password while `db-setup` connects with the old one, and the API uses the old runtime password, producing setup/login and readiness failures.
- Expected versus actual behavior: Instructions should update every credential occurrence or use a single source of truth; the current instruction is ambiguous and the troubleshooting table does not mention a mismatch.
- Impact and recommended correction: Local setup can fail with a misleading login error; low severity but a concrete documented setup path. State explicitly that both placeholder values also appear in the connection strings and must be replaced everywhere, or restructure the example to avoid duplicated secrets.

### R-05 - TC-08's automated rerun test does not assert login/user/grant idempotency

- Severity / status: Low / Verified resolved (2026-11-02). The rerun test now asserts principal and permission counts; see Re-review and resolution.
- Requirement ID and file / symbol / line: S7, S10, AC-4; `docs/test/1-test.md:131-141`; `tests/ServiceScheduler.IntegrationTests/Persistence/MigrationAndSeedTests.cs:278-289`.
- Triggering scenario and evidence: TC-08's expected result includes "no duplicate user/login/grants." The test asserts shift IDs/counts and migration history after a setup rerun, but not server/database principal or permission counts. Duplicates are structurally unlikely (unique principal names and idempotent `GRANT`), and this review's independent run found one login, one user, and two permission rows after two setups; a regression such as an extra grant would not be caught by the automated assertion.
- Expected versus actual behavior: The automated test should cover the stated rerun expectation, or the test report should narrow the expectation; currently the mapping claims coverage that the test does not assert.
- Impact and recommended correction: Test-coverage gap for the runtime principal surface; low risk. Extend the rerun test with counts from `sys.server_principals`, `sys.database_principals`, and `sys.database_permissions`, or record the narrower expectation.

## Requirement coverage

> [!NOTE]
> The gap column records the review-time state. The 2026-11-02 re-review resolved R-01-R-05, and the formal ticket 1 test report now records the executed matrix; see Re-review and resolution.

| Requirement ID | Evidence | Gap / remaining check |
| --- | --- | --- |
| S1 | `Program.cs` registers controllers, Problem Details, DI, options validation, and endpoints; unknown-route review probe returned `404` `application/problem+json` with a trace ID and no stack trace or password. No business endpoint exists by design (G-10). | Controller/DTO/async/cancellation behavior remains structural; tickets 2 and 3 exercise endpoints. |
| S2 | `ServiceScheduler.slnx` contains one API project and separate unit/integration test projects; no generic repository, mediator, or runtime AI dependency found. | None observed for ticket 1. |
| S3 | `global.json` pins SDK `10.0.401`; `Directory.Packages.props` pins packages; Dockerfile and Compose use pinned image references; the pinned SQL image was available and used. | A fresh clean-checkout restore/build on the review host was not possible; see AC-1 and T-16. |
| S4 | Runtime/setup validators, `.env.example`, configuration split, and startup validation exist; missing and malformed runtime settings failed startup with the key name and no value in review probes; 17 unit tests passed. | `dotnet format --verify-no-changes` was Not run during review. |
| S5 | Mappings and migration match [[database/schema\|Schema]]; temporary real SQL Server showed 10 tables, 10 checks, 14 foreign keys, and the `InitialCreate` history row. | Invalid-insert and generated-SQL assertions exist in tests but are Not run formally; see R-01 and the test phase. |
| S6 | Setup created `scheduler_app`; runtime read returned `CURRENT_USER=scheduler_app` and two dealerships; no database/server roles; schema `SELECT` allowed; appointment INSERT, UPDATE, DELETE and CREATE/ALTER/DROP denied; final probes absent and appointments empty. | None observed for ticket 1. |
| S7 | Static seeds and future shifts were present: 2 dealerships, 2 customers, 2 vehicles, 3 service types, 4 technicians, 5 qualifications, 4 bays, 40 shifts, 0 appointments; two setup runs completed idempotently. | Formal seed and constraint suite Not run; TC-08 principal idempotency assertion gap in R-05. |
| S8 | Dockerfile runtime/setup targets exist; Compose config validates; the documented `up -d --wait` path reached SQL healthy, setup exit `0`, and API healthy; the named volume survived `down`/`up`; `down -v` removed it. | Clean-checkout build and the formal T-16 record remain pending. |
| S9 | `/health/live` returned `200` with unreachable and initialized SQL; `/health/ready` returned `503` with unreachable SQL and `200` with the initialized database; `/openapi/v1.json` returned valid JSON; Swagger UI and its initializer script referenced the document. | SQL stop/restart recovery was not re-executed during review; see T-14. |
| S10 | The built unit test assembly executed 17 tests with 0 failures, errors, skips, or not-run cases; SQL fixtures and tests exist and were reviewed statically. | Non-SQL WebApplicationFactory tests were environment-blocked in this review, and the SQL integration suite was Not run; the test phase must execute both on a supported host. |
| S11 | README, setup guide, architecture, database records, documentation index, and deployment notes reflect the implemented foundation and distinguish planned work. | R-01 through R-04 identify remaining wording and evidence issues. |
| AC-1 | Documented restore/build/run/setup commands exist; the Compose stack ran from built images; configuration and startup behavior were exercised. | Clean-checkout restore/build/run was Not run: the host has no .NET 10 SDK and the implementation is uncommitted. The test phase must record T-16. |
| AC-2 | Unit tests passed 17/17 from the built assembly. Both test projects exist and reference the API. | Integration suite execution is Not evidenced; the test phase must run it. |
| AC-3 | Missing and malformed runtime configuration failed startup with a clear key name and no secret value; validator tests passed. | `dotnet format --verify-no-changes` was Not run during review. |
| AC-4 | An empty real SQL Server was initialized twice through the production setup component; migration history, runtime identity, and seeds were present; readiness used the runtime identity. | Formal report pending; no failure observed. |
| AC-5 | Constraints, checks, indexes, and composite FK actions match the schema record; runtime direct writes and DDL were denied; invalid-insert tests exist. | Invalid-insert tests were Not run formally; the test phase must record them. |
| AC-6 | Liveness remained `200` while readiness was `503` with unreachable SQL; readiness was `200` against the initialized database. | Recovery after a real outage was not re-executed; see T-14. |
| AC-7 | OpenAPI and Swagger UI were accessible; readiness and startup smoke checks passed against real SQL in review probes. | The formal xUnit smoke suite is Not run; the test phase must record it. |
| AC-8 | Compose startup, setup gating, named-volume persistence, and destructive reset were exercised. | A clean-checkout Compose run and the formal T-16 record remain pending. |
| AC-9 | Documentation separates implemented and planned foundation work. | R-01 and R-02 identify remaining evidence-labeling and stale-wording issues. |
| T-14 | The outage portion of T-14 was verified: live `200`, ready `503`; initialized ready `200`. | SQL stop/restart recovery was not re-executed during review; the test report must record T-14. |
| T-16 | The documented Compose startup and named-volume behavior were exercised with the built images; `up -d --wait`, `down`, `up`, and `down -v` behaved as documented. | Clean-checkout restore/build/format/test commands were Not run; the implementation is uncommitted and the host lacks the pinned SDK. The test phase must record T-16. |

## Verification

> [!NOTE]
> The rows below record checks performed during this review against the working tree and temporary local dependencies, except the final row, which lists checks left to the test phase. Existing implementation-phase evidence is distinguished from formal test evidence; [[test/1-test|the ticket test report]] remains the owner of the formal matrix and marks its cases Not run.

| Command / check | Evidence source / revision | Observed result | Limitation |
| --- | --- | --- | --- |
| SHA-256 input check | Working tree, HEAD `ba36ac8` | Ticket and research hashes match the verification records; plan/README/setup-guide hashes changed after plan verification as implementation records. | No fresh plan verification is claimed; see Scope and baseline. |
| `dotnet --list-sdks` / `--list-runtimes` | Review host | SDK `8.0.100` and `9.0.315`; runtimes 8/9; no .NET 10 SDK or runtime. | Host restore/build/format/test commands were Not run. |
| Built unit test execution | `tests/ServiceScheduler.UnitTests/bin/Debug/net10.0/ServiceScheduler.UnitTests.dll` (built 2026-10-31 16:49 local) on the .NET 10 runtime | 17 total, 0 errors, 0 failed, 0 skipped, 0 not run. | Uses the existing build, not a fresh restore/build. |
| Non-SQL integration test attempt | Built integration assembly (16:51 local) in the API runtime image | 7 tests failed with `DirectoryNotFoundException: /tests/D:\...` because `WebApplicationFactory` embedded the Windows content-root path. | Review-environment artifact, not a product result; the tests remain Not run officially. |
| API endpoint probes | Built API image (09:40Z) with unreachable SQL | `/health/live` `200`; `/health/ready` `503`; `/openapi/v1.json` `200` with valid JSON; `/swagger/index.html` and `/swagger/index.js` `200` referencing `/openapi/v1.json`; unknown route `404` `application/problem+json` with trace ID. | The image predates the last migration edit; the current assembly was used for the final setup re-check. |
| Startup validation probes | API image with missing/malformed runtime configuration | Missing: `OptionsValidationException: ConnectionStrings:Runtime is required.`; malformed: `... is not a valid SQL Server connection string.`; no credential values. | None observed. |
| `docker compose config` | `compose.yaml` with required variables | Exit `0`; expected services, targets, dependency conditions, pinned SQL image, loopback ports, and named volume present. | No image build. |
| Compose lifecycle | Temporary project using the built API/setup images | `up -d --wait` exit `0`; SQL healthy, setup exit `0`, API healthy; live/ready/OpenAPI `200`; `down` kept the named volume; a second `up -d --wait` exit `0` and ready `200`; `down -v` removed the volume. | Existing images were used; clean-checkout image build not performed. |
| Real SQL setup, twice | Pinned SQL Server 2022 container; built setup image | Both runs exit `0`; 10 tables, 1 migration, 40 shifts, 0 appointments, 10 checks, 14 FKs, 2 runtime permission rows, no user/server roles. | Review-executed, not the formal test report. |
| Runtime permission probes | Runtime `scheduler_app` connection | Read 2 dealerships as `scheduler_app`; no `db_owner`/`db_datawriter`/`db_ddladmin`/`db_datareader`/`sysadmin`; schema `SELECT` allowed; schema ALTER/EXECUTE and appointment INSERT/UPDATE/DELETE denied; CREATE TABLE, ALTER TABLE, and DROP TABLE denied; final state had 0 appointments and no probe objects. | Review-executed, not the formal test report. |
| Current-assembly setup re-check | Current `ServiceScheduler.Api/bin/Debug/net10.0` (16:49 local) mounted in the runtime image against a fresh SQL container | Two setup runs exit `0`; 10 tables, 1 migration, 40 shifts, 10 checks, 14 FKs. | Review-executed; confirms the current compiled migration, not a fresh build. |
| Not run | — | Host `dotnet restore/build/format`, full xUnit integration suite, clean-checkout T-16, SQL stop/restart recovery, and the formal test matrix. | These remain for the test phase; see [[test/1-test|the test report]]. |

## Re-review and resolution

- Re-review revision: working tree based on HEAD `ad774fb` with the review fixes; [[test/1-test|the ticket 1 test report]] remains the formal execution record for the foundation revision.
- Re-review date: 2026-11-02.
- Commands: `dotnet build --no-restore`; `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build`; `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build` against real SQL Server 2022 CU23 and Redis Testcontainers; `dotnet format --verify-no-changes`.
- Observed results: build succeeded with 0 warnings and 0 errors; 135 unit tests and 157 integration tests passed with 0 failed or skipped; formatting required no changes. The integration count includes the ticket 3 suites; the TC-08 assertion in `MigrationAndSeedTests` is the relevant ticket 1 check.

| Finding | Correction | Resolution evidence | Status |
| --- | --- | --- | --- |
| R-01 | The test phase executed the formal matrix and replaced the provisional claims. | `docs/test/1-test.md` is `status: executed` with 21/21 cases Passed; `docs/database/schema.md`, `docs/plans/1/README.md`, and `docs/deploy-notes/1-deploy-notes.md` reference the execution record. | Verified resolved |
| R-02 | `docs/architecture.md` now records completed version pinning and the remaining clean-checkout/image-combination gaps. | Documentation inspection of the version statement and references note. | Verified resolved |
| R-03 | `docs/setup-guide.md` documents `dotnet ef migrations list --no-connect` and the applied-status alternative. | Documentation inspection. | Verified resolved |
| R-04 | `.env.example`, `README.md`, `docs/setup-guide.md`, and the troubleshooting table state that both placeholder values occur in four places. | Documentation inspection. | Verified resolved |
| R-05 | `MigrationAndSeedTests.FutureShiftsCoverTheHorizonAndRerunIsIdempotent` captures login, user, and permission counts before and after the setup rerun and asserts one login, one user, and six permission rows (the current schema includes the ticket 3 procedure/deny grants). | `MigrationAndSeedTests` passed in the 157-test integration run. | Verified resolved |

## Outcome and follow-up

- Review conclusion and remaining risks: The exercised foundation behavior matches ticket 1: startup validation, liveness/readiness, OpenAPI/Swagger, migration and schema constraints, setup idempotency, the restricted runtime identity, seeds, and Compose startup and named-volume behavior. No Critical, High, or Medium findings were identified. As of the 2026-11-02 re-review, all five Low findings are resolved: the ticket 1 test report records the executed 21/21 matrix and clean-copy run, the architecture and setup documentation no longer retains the pre-implementation wording, and TC-08 now asserts runtime principal and permission idempotency. No open review findings remain.
- Required corrections or checks: none outstanding for R-01-R-05. Keep [[test/1-test|the ticket 1 test report]] as the execution record and rerun it if the foundation implementation changes.
- Re-review revision, finding IDs, and resolution evidence: 2026-11-02, working tree based on HEAD `ad774fb`; R-01-R-05 verified resolved in Re-review and resolution.
