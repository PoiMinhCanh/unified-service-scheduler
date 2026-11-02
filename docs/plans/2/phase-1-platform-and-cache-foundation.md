---
title: "2 - Phase 1: Platform, Errors, and Cache Foundation"
tags: [plans, service-scheduler, api, caching, errors]
ticket: 2
phase: 1
status: planned
---

# Phase 1: Platform, errors, and cache foundation

## Requirements and scope

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Research: [[research/2/01-api-contracts-and-validation|API contracts and validation]], [[research/2/03-catalog-cache-and-resilience|catalog cache and resilience]], [[research/2/04-verification-and-documentation|verification and documentation]].
- Requirement IDs: S2-04, S2-05, and the platform portions of AC2-05 and AC2-06; project P-02; test IDs TC2-11, TC2-32, and the startup subset of TC2-31.
- Intended outcome: register the application clock, Redis-backed `IDistributedCache`, bounded cache operations, cache options validation, cache health check, shared Problem Details code/trace-ID contract, narrow SQL failure classification, JSON health output, and extended test host.
- Exclusions: no business controller or DTO (Phase 2/3); no compose Redis service (Phase 4); no OpenAPI transformers or document assertions (Phase 5); no schema/seed/runtime-grant change.

## Dependencies

- Research verification passed 2026-11-01 (see [[research/2/00-overview#Verification|the research Verification record]]).
- Existing foundation from [[plans/1/README|ticket 1]] is implemented: `Program.cs` composition root, `SchedulerDbContext`, health checks, `SchedulerApiFactory`, SQL fixture, and Problem Details middleware.
- No previous ticket 2 phase.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | S2-05; P-02 | `Directory.Packages.props`, `src/ServiceScheduler.Api/ServiceScheduler.Api.csproj`, `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj` | Pin `Microsoft.Extensions.Caching.StackExchangeRedis` `10.0.13` and `Testcontainers.Redis` `4.14.0`; reference them from the API and integration-test projects. | `dotnet restore` resolves the packages and `dotnet build --no-restore` succeeds with the existing warning policy. | Planned |
| 2 | S2-05, S2-04; AC2-05 | `src/ServiceScheduler.Api/Infrastructure/Caching/CacheOptions.cs`, `CacheOptionsValidator.cs` | Bind `Scheduler:Cache` (`ServiceCatalog:AbsoluteTtl`, `OperationTimeout`, `ConnectTimeout`, `CommandTimeout`, `ConnectRetry`); require a present, parseable `ConnectionStrings:Cache` in API mode; validate positive/bounded values without opening a connection; error messages name the key, never the value. | Unit tests cover valid defaults, missing/malformed connection string, non-positive TTL/timeouts, and excessive operation timeout; startup validation fails clearly for invalid cache configuration. | Planned |
| 3 | S2-05; AC2-05 | `src/ServiceScheduler.Api/Infrastructure/Caching/RedisCacheConfiguration.cs`, `BoundedCache.cs` | Build `ConfigurationOptions` with `AbortOnConnectFail=false`, 500 ms connect/sync/async timeouts, `ConnectRetry=1`, and `BacklogPolicy.FailFast`; register `AddStackExchangeRedisCache`; wrap get/set/remove with a linked 250 ms budget, `WaitAsync`, request-cancellation propagation, abandoned-task observation, recoverable Redis/timeout classification, and explicit `ILogger` warning events for degradation (no connection strings or credentials). | Unit tests with a fake `IDistributedCache` prove: budget expiry is recoverable and logged; request cancellation propagates without a degradation log; `RedisException` is classified as degradation; an unexpected exception is not swallowed; the configured per-operation budget is applied. | Planned |
| 4 | S2-04; AC2-06 | `src/ServiceScheduler.Api/Infrastructure/Errors/ProblemCodes.cs`, `SqlFailureClassifier.cs`, `SqlDependencyExceptionHandler.cs`, `ProblemDetailsConfiguration.cs` | Define the four stable codes; classify only the research SQL error-number list through inner exceptions; register `IExceptionHandler` that writes `503 dependency_unavailable` Problem Details with `traceId`; configure `ProblemDetailsOptions.CustomizeProblemDetails` to add `code`/`traceId` consistently; leave cancellation and unlisted exceptions unclassified. | Unit tests assert every listed code is classified, unlisted/auth/permission codes are not, inner-exception traversal works, and cancellation is not classified; an integration check asserts unknown-route `404` still contains `code`/`traceId` with no stack/password. | Planned |
| 5 | S2-04, S2-05; AC2-05 | `src/ServiceScheduler.Api/Program.cs`, `src/ServiceScheduler.Api/Infrastructure/Caching/CacheHealthCheck.cs`, `src/ServiceScheduler.Api/Infrastructure/Health/HealthResponseWriter.cs` | Register `TimeProvider.System`; options validation; exception handler; Problem Details customization; bounded Redis health check tagged `ready` with a 2-second registration timeout; JSON response writer that emits only check names/statuses/durations; keep liveness dependency-free. | Host builds; `/health/live` returns `200` with no dependencies; `/health/ready` remains SQL-dependent; a `Degraded` cache check renders `200` with cache state in the body and no secret/connection text. | Planned |
| 6 | S2-05; AC2-05, AC2-06; TC2-31 partial, TC2-32 | `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs`, `tests/ServiceScheduler.IntegrationTests/Startup/StartupValidationTests.cs`, `Startup/CacheConfigurationTests.cs` | Default the test host to a valid but unreachable cache endpoint; add overridable settings, a `TimeProvider` replacement, and an optional test-service hook; add startup tests for missing/malformed cache configuration and an unreachable endpoint. | Factory default hosts still run health/OpenAPI/404 tests without Redis; missing/malformed cache configuration fails startup with a clear key-naming error; an unreachable endpoint does not block host start or liveness. | Planned |
| 7 | S2-06; P-04 | `docs/test/2-test.md` | Create the ticket 2 test report using the backend and integration templates; record the phase-1 revision, environment, commands, results, and unexecuted checks. | Report exists with actual command/output evidence and explicit Not run/Blocked rows for later phases. | Planned |

## Verification

| Test ID / requirement | Scenario and command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| Build baseline | `dotnet restore` then `dotnet build --no-restore` from the repository root. | Restore and build succeed with no new warnings. | Not run |
| TC2-11 (S2-05) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Caching"` | Bounded cache, cancellation, corrupt payload, and unexpected-exception behavior pass. | Not run |
| SQL classifier (S2-04) | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build --filter "FullyQualifiedName~Errors"` | Listed SQL numbers classify; unlisted/auth/permission codes remain unclassified. | Not run |
| TC2-32 (S2-05) | `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Startup"` | Missing/malformed `ConnectionStrings:Cache` fails startup with a clear, non-secret error; the existing startup tests still pass. | Not run |
| TC2-31 partial (S2-05) | Start the test host with the default unreachable cache endpoint; GET `/health/live` and `/health/ready`. | Host starts; liveness `200`; readiness remains SQL-dependent; cache state is visible without secrets. | Not run |
| Problem Details baseline (S2-04) | Extended unknown-route check in `Startup/ProblemDetailsTests.cs`. | `404` Problem Details contain `code=not_found`, non-empty `traceId`, and no stack/password. | Not run |
| Format | `dotnet format --verify-no-changes`. | No formatting changes are required. | Not run |

## Exit criteria

- Package pins restore and the solution builds.
- Cache options validation, bounded cache wrapper, and SQL classifier unit tests pass.
- The host starts with an unreachable Redis and preserves liveness/readiness semantics.
- `docs/test/2-test.md` records Phase 1 commands, results, environment, and remaining checks.
- No business endpoint, cache-aside behavior, compose service, or migration is claimed as complete in this phase.

## Impacts and remaining decisions

- Database/migrations: none; no schema object is added.
- Cache: package registration, options, budgets, health check, and degradation classification only; key/payload behavior belongs to Phase 2.
- Configuration: new `Scheduler:Cache:*` names and `ConnectionStrings:Cache`; setup mode remains cache-free.
- Deployment: no compose change yet; Redis service behavior is deferred to Phase 4.
- Remaining decisions: none blocking. Exact wrapper type names and health-writer shape may adjust during implementation while preserving the fixed configuration names, budgets, codes, and observable behavior.
