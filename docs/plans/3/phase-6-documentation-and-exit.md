---
title: "3 - Phase 6: Documentation, Clean Checkout, and Exit Criteria"
tags: [plans, service-scheduler, booking, documentation, operations]
ticket: 3
phase: 6
status: planned
---

# Phase 6: Documentation, clean checkout, and exit criteria

## Requirements and scope

- Ticket: [[3 - Appointment Booking and Retrieval|Ticket 3]]
- Research: [[research/3/03-permissions-migrations-and-operations|Permissions, migrations, and operations]], [[research/3/04-verification-and-documentation|Verification and documentation]].
- Requirement IDs: S3-06; AC3-01 through AC3-13 evidence consolidation; the clean-checkout portion of T-16; project D-01, D-02, D-03, D-04, P-04.
- Intended outcome: make the delivered records match the implemented booking behavior and actual evidence, complete the clean-checkout verification, and close ticket 3 only when every required automated/operational check has passing evidence or an explicit documented blocker.
- Exclusions: no new production behavior, test weakening, schema/permission change, telemetry stack, authentication, cancellation/rescheduling, idempotency, vehicle-overlap protection, or manual checklist execution. The orchestrator `test` phase later generates the manual checklist; this phase does not execute it.

## Dependencies

- Phases 1-5 complete with required automated/operational evidence recorded in `docs/test/3-test.md`.
- The actual migration ID, procedure name/errors, option defaults, permission rows are known from implementation.
- [[database/maintenance|Database maintenance guidance]], [[test/README|Testing guidance]], [[deploy-notes/README|Deployment notes guidance]], and the documentation index are available.
- No required check is failing or blocked; a failure stops exit and returns to the owning phase.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S3-06; D-01, D-02, P-04 | `README.md` | Record booking status, POST/GET endpoints with the actual DTO/status contract, `Scheduler:Booking` options, migration/setup changes, runtime identity behavior, and links to the ticket 3 test report and deploy notes. Do not claim unexecuted behavior. | README matches implemented routes/options/commands and links actual evidence; planned behavior is not presented as executed. | Planned |
| 2 | S3-06; D-01, D-02 | `docs/architecture.md` | Record the procedure contract and error numbers, IANA-to-Windows mapping, technician-then-bay locking, SERIALIZABLE/XACT_ABORT/lock/command/budget limits, candidate order and non-exhaustive `409` bound, API/DTO/Problem Details contract, runtime permissions, failure classification, unknown-commit limitation, and cache no-impact. | Architecture matches implementation and research decisions; no contradictory lock, retry, or reservation wording remains. | Planned |
| 3 | S3-06; D-03 | `docs/setup-guide.md` | Record booking configuration keys/defaults, migration and grant/deny order, fresh/repeated setup, ticket 2 upgrade, down-migration/permission recovery, and Compose/health commands. | Setup instructions are executable from the repository; configuration names/defaults and recovery steps match the code and deploy notes. | Planned |
| 4 | S3-06; D-03, D-04 | `docs/database/schema.md`, `overview.md`, `migration.md`, `cache.md` | Record the `TimeZoneMappings` table/PK/unique index, dealership FK, `dbo.CreateAppointment` signature/errors, existing appointment indexes and no new index, actual migration ID, upgrade/backfill (none)/down/recovery behavior, exact runtime grants/denies, and the cache no-change reason. | Database documents match the implemented schema/procedure/permissions and actual migration evidence; cache documentation states no key/payload/TTL/invalidation change and why. | Planned |
| 5 | S3-06; D-01, D-02 | `docs/README.md` | Update ticket 3, plan, research, test, deploy-notes, architecture, setup, and database statuses/links to reflect implemented behavior and actual evidence; keep planned/manual items explicit. | The index navigates all ticket 3 records and does not claim manual or acceptance completion prematurely. | Planned |
| 6 | S3-06; P-04 | `docs/test/3-test.md`, `docs/deploy-notes/3-deploy-notes.md` | Finalize requirement coverage, executed scenarios, commands, environment/dependency versions, observed vs expected results, Passed/Failed/Blocked/Not run status, and links to evidence. Deploy notes record migration order, grants/denies, repeated setup, upgrade, rollback/revoke, recovery, and actual local execution. | Every S3/T/S3-V requirement maps to executed evidence or an explicit Not run/Blocked reason; manual-only checks remain Not run; deploy notes are operational and do not duplicate ticket requirements. | Planned |
| 7 | S3-06; T-16, AC3-09 | Clean checkout in an isolated local environment | Follow the documented restore, build, initialization, migration, seed, startup, health, booking/retrieval commands from a clean checkout. Use synthetic data and generated future times. If a destructive `docker compose down -v` reset is required, confirm it for the isolated local stack before running. | Documented commands work end-to-end from a clean checkout; actual commands/results are recorded; any missing prerequisite or failed step is reported rather than hidden. | Planned |
| 8 | S3-06; all acceptance criteria evidence | Full regression and final exit review | Run `dotnet restore`, `dotnet build --no-restore`, `dotnet format --verify-no-changes`, the full unit and integration suites, Compose validation/startup/health. Review requirement traceability, acceptance criteria, evidence links, and unexecuted manual checks. Optionally inspect Swagger UI as a supplement, not as automated evidence. | All required automated/operational checks pass; failed or blocked checks are visible and prevent completion; every acceptance criterion has evidence or an explicit remaining manual/blocked status; no criterion is marked Passed from a checklist or build alone. | Planned |

## Documentation ownership

| Record | Owns |
| --- | --- |
| Ticket 3 | Requirements, acceptance criteria, verification cases, and scope/exclusions. |
| Research 3 | Decisions, inspected evidence, procedure/API/permission design, and planned verification. |
| This plan | Ordered implementation steps, dependencies, files, commands, and verification ownership. |
| `docs/test/3-test.md` | Executed ticket 3 evidence and unexecuted/blocked checks. |
| `docs/deploy-notes/3-deploy-notes.md` | Per-ticket operational actions and local execution record. |
| `README.md`, `docs/architecture.md`, `docs/setup-guide.md`, `docs/database/*.md` | Implemented behavior, contracts, configuration, schema/procedure, and recovery. |
| `docs/test/3-manual-test.md` | Later human-executed manual checklist; not created by this phase. |

## Verification

| Check / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet restore`, `dotnet build --no-restore`, `dotnet format --verify-no-changes`. | Restore/build/format succeed with no new warnings. | Not run |
| Unit regression | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build`. | All unit tests pass. | Not run |
| Integration regression | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build`. | All integration tests pass against real SQL Server/Redis; no required failure remains. | Not run |
| Clean checkout (T-16, AC3-09) | Follow the documented clean-checkout setup/startup/demo sequence in an isolated local environment; record actual commands and outputs. | Initialization, migration, seed, startup, health, booking, retrieval work; generated future times are within seeded shifts. | Not run |
| Compose/health | `docker compose config`, `docker compose up -d --wait`, `docker compose logs db-setup`, `docker compose logs api`, liveness/readiness requests. | Stack validates/starts; setup applies; health bodies are correct and contain no secrets. | Not run |
| Documentation review (S3-06, D-01-D-04) | Inspect README, architecture, setup, database schema/overview/migration/cache, docs index, test report, and deploy notes against implemented code/evidence. | No planned behavior presented as executed; actual migration ID/options/procedure/errors/permissions/links are correct. | Not run |
| Acceptance/evidence review | Walk AC3-01 through AC3-13, T-01-T-14/T-16, and S3-V01-S3-V06 against the test report and deploy notes. | Each has passing evidence or an explicit Not run/Blocked reason; required failures block exit; manual checks remain Not run. | Not run |
| Manual supplement (optional) | Swagger UI or Grafana/browser inspection if available. | Supplementary only; does not substitute for automated evidence and is recorded as manual. | Not run |

## Exit criteria

- README, architecture, setup guide, database schema/overview/migration/cache, documentation index, test report, and deploy notes reflect the implemented booking behavior and actual evidence.
- The clean-checkout initialization, migration, seed, startup, booking/retrieval, Compose commands work from a fresh local checkout using synthetic data and generated future times.
- All required automated and operational checks have passing evidence; any failure or blocker is explicitly recorded and prevents completion.
- Every acceptance criterion and verification case has evidence or a clearly identified manual/unexecuted status; no acceptance criterion is marked Passed from a generated checklist, a build, or review alone.
- The manual checklist remains for the later `/orchestrator-ticket test 3` phase and starts Not run/Blocked; this phase does not claim human verification.
- No unresolved scope expansion, cache/telemetry change, authentication, cancellation/rescheduling, idempotency, or vehicle-overlap work is bundled into ticket 3.

## Impacts and remaining decisions

- Documentation: final README/architecture/setup/database/index/test/deploy records; no new requirements.
- Database/migrations: records the actual ticket 3 migration ID, objects, compatibility, and recovery; no further schema change.
- Cache/telemetry: no cache change and no telemetry infrastructure; the reasons are recorded.
- Deployment: operational notes and clean-checkout evidence are complete; a destructive local reset is run only with explicit confirmation.
- Remaining decisions: none blocking. The manual checklist generation and human execution remain a separate later phase; any required automated failure returns the ticket to the owning phase rather than being marked Passed here.
