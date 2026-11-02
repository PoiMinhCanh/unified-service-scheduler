---
title: "2 - Phase 5: OpenAPI, Swagger Documentation, and Full Regression"
tags: [plans, service-scheduler, openapi, verification]
ticket: 2
phase: 5
status: planned
---

# Phase 5: OpenAPI, Swagger documentation, and full regression

## Requirements and scope

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/01-api-contracts-and-validation|API contracts and validation]], [[research/2/04-verification-and-documentation|verification and documentation]].
- Requirement IDs: S2-06; AC2-01; project D-02 and P-04; test IDs TC2-33 and TC2-35, plus the complete regression matrix.
- Intended outcome: publish the three operations and their success/error contracts in OpenAPI, provide interactive Swagger request documentation, and run the full unit/integration/format suite against the implemented behavior.
- Exclusions: new endpoints, cache behavior, availability rules, or schema changes; code style changes outside the ticket; manual checklist generation (orchestrator `test` phase); final documentation consolidation (Phase 6).

## Dependencies

- Phases 1-4: complete platform, endpoints, availability rules, resilience behavior, Compose Redis, and recorded phase evidence.
- Seeded Compose stack is needed for supplementary human Swagger checks, not for a retired script prerequisite.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S2-06; AC2-01; D-02 | `src/ServiceScheduler.Api/Program.cs`, feature OpenAPI transformer files | Register operation/schema transformers through `AddOpenApi`; document operation summaries, ordering (name then ID), parameter descriptions and explicit-offset/`%2B` requirement, both availability outcomes, and `ProblemDetails` `code`/`traceId` extensions and error examples. | OpenAPI document describes all three operations, DTO schemas, status codes, ordering, explicit-offset input, and error extensions without expiring dates as the only example. | Planned |
| 2 | S2-06; AC2-01; TC2-33 | `tests/ServiceScheduler.IntegrationTests/OpenApi/OpenApiTests.cs` | Extend assertions for the three paths, response schemas/statuses, ordering text, availability outcome descriptions, and Problem Details extension documentation while preserving the existing document/Swagger checks. | TC2-33 passes; Swagger UI remains at `/swagger/index.html` and reads `/openapi/v1.json`. | Planned |
| 3 | S2-06; AC2-01 | README and setup guide | Use Swagger UI and existing automated API tests instead of a standalone script. | Current instructions contain no deleted script command. | Completed by scope amendment 2026-11-01 |
| 4 | S2-06; AC2-01; TC2-35 | Scope record | Retire the script-only case; preserve previous runs as historical evidence. | TC2-35 is Retired, not Passed; supplementary manual cases are generated in the test phase and start Not run. | Completed by scope amendment 2026-11-01 |
| 5 | S2-06; P-04 | Full regression commands | Run `dotnet restore`, `dotnet build --no-restore`, `dotnet format --verify-no-changes`, the full unit suite, and the full integration suite (real SQL Server + Redis). | All suites pass; any skipped/blocked checks are recorded with reasons; ticket 1 foundation tests still pass. | Planned |
| 6 | S2-06; P-04 | `docs/test/2-test.md` | Append Phase 5 commands, counts, image/package versions, OpenAPI output, and remaining gaps. | Report contains the complete executed matrix and preserves prior phase evidence. | Planned |

## Verification

| Test ID / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build/format | `dotnet restore`; `dotnet build --no-restore`; `dotnet format --verify-no-changes`. | Restore/build/format succeed with no new warnings or formatting changes. | Not run |
| TC2-33 (S2-04, S2-06, AC2-01) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~OpenApi"` | Three operations, DTOs, status codes, ordering, and Problem Details extensions are documented; existing Swagger check passes. | Not run |
| TC2-35 (S2-06, AC2-01) | Standalone demo-script execution removed by user-approved scope amendment. | No script deliverable or execution prerequisite. | Retired 2026-11-01; prior runs historical only |
| Full unit suite | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build`. | All unit tests pass, including ticket 1 configuration tests. | Not run |
| Full integration suite | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build`. | All integration tests pass against real SQL Server and Redis; foundation regression preserved. | Not run |

## Exit criteria

- OpenAPI documents the complete ticket 2 surface and TC2-33 passes.
- Swagger/manual instructions match the current API; TC2-35 is explicitly retired. Human Swagger execution is not a prerequisite to generating its checklist.
- Full unit/integration suites and format verification pass; any unexecuted check is explicitly Blocked/Not run with a reason.
- No acceptance criterion is marked Passed from documentation alone; the test report ties each result to executed evidence.
- `docs/test/2-test.md` contains the complete Phase 5 evidence.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: no behavior change; full regression includes cache lifecycle and outage tests.
- Configuration: uses implemented names and Compose settings; no new names.
- Deployment: no standalone demo script or CI workflow is required.
- Remaining decisions: none for this documentation-only scope amendment.
