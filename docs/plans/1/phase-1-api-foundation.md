---
title: "1 - Phase 1: Solution Scaffold and API Foundation"
tags: [plans, service-scheduler, foundation, api]
ticket: 1
status: implemented
---

# 1 - Phase 1: Solution scaffold and API foundation

## Requirements and scope

- Ticket: [[1 - Init Project]]
- Research: [[research/1/00-overview|Foundation research overview]] and [[research/1/01-platform-foundation|Platform foundation research]].
- Requirement IDs: S1, S2, S3, S4, S9, S10 test-foundation portion; AC-1 scaffold portion, AC-2, AC-3, AC-6, AC-7; T-14 outage portion; foundation portions of D-01-D-03.
- Intended outcome: a pinned, buildable solution with one API host and two executable xUnit projects, configuration validation that fails startup without opening SQL, consistent Problem Details, documented DTO/async/cancellation conventions, OpenAPI/Swagger, and liveness/readiness endpoints.
- Exclusions: no business controller or DTO endpoint (S1 behavior is structural until tickets 2 and 3 add endpoints); no EF entities, migration, runtime identity, seeds, Compose, or business data; no Serilog, OpenTelemetry, Redis.
- Assumptions: the G-01 package baseline restores together; SDK `10.0.401` is available through the pinned Docker SDK image or an optional host installation; the empty `SchedulerDbContext` registered here is completed in Phase 2 and exists only so the selected `AddDbContextCheck<SchedulerDbContext>` readiness design can be wired once.

## Dependencies

- Accepted research decisions G-01, G-09, G-10, G-11, G-15, G-21, G-25, G-26, G-27, G-37, G-40, and G-43.
- .NET SDK `10.0.401` through the pinned Docker SDK image (default) or an optional host installation, plus access to NuGet restore. No SQL container is required for this phase because the outage tests use a non-listening SQL endpoint. See the setup guide for the SDK container command.
- Phase 2 depends on the host, configuration, and DbContext registration produced here.

## Implementation steps

| Step | Requirement IDs | Files / components (planned) | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 1.1 | S2, S3, AC-1 | `global.json`, `.config/dotnet-tools.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` | Pin SDK `10.0.401` with `rollForward: disable` and `allowPrerelease: false`; pin `dotnet-ef` `10.0.13`; centralize the G-01 package versions; enable `net10.0`, nullable, implicit usings, built-in analyzers, and `TreatWarningsAsErrors`; define formatting rules. | `dotnet --version` in the chosen SDK environment matches the pin; restore/build/format use no floating package or image versions. | Implemented |
| 1.2 | S2, S10, AC-1, AC-2 | `ServiceScheduler.slnx`, `src/ServiceScheduler.Api/ServiceScheduler.Api.csproj`, `tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj`, `tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj` | Create the three projects and solution; reference the API project from both test projects; make the xUnit v3 test projects executable with the VSTest adapter and test SDK; add the test-only `FakeTimeProvider` package. | `dotnet sln list` shows the API and both test projects; both test projects start and execute. | Implemented |
| 1.3 | S1, S4, AC-3 | `src/ServiceScheduler.Api/Program.cs`, `Infrastructure/Configuration/DatabaseOptions.cs`, `appsettings.json`, `appsettings.Development.json`, `.env.example` | Bind and validate `ConnectionStrings:Runtime`; require presence, parseable SQL syntax, server/database/user/password, a non-`sa` runtime identity, and a finite connect timeout; use `ValidateOnStart`; document environment-variable names and placeholder values; never bind `ConnectionStrings:Setup` in API mode. | Missing or malformed runtime configuration fails startup with a clear key-level message; a syntactically valid but unreachable server still starts the host; no SQL connection is opened by validation. | Implemented |
| 1.4 | S1, S9, AC-6, AC-7 | `src/ServiceScheduler.Api/Program.cs`, `Infrastructure/Persistence/SchedulerDbContext.cs` | Register controllers, `AddProblemDetails()`, exception/status-code handling, built-in OpenAPI at `/openapi/v1.json`, Swagger UI at `/swagger/index.html`, and mapped controllers; reserve feature-local contract folders for future DTOs and document asynchronous/cancellation conventions; register an empty `SchedulerDbContext` with `ValueGeneratedNever` conventions ready for Phase 2; add `public partial class Program` for `WebApplicationFactory`. | The host serves OpenAPI/Swagger without SQL; unknown routes return a Problem Details body with a trace ID; the DTO/async/cancellation conventions are documented; no business endpoint is added. | Implemented |
| 1.5 | S9, AC-6, T-14 outage portion | `src/ServiceScheduler.Api/Program.cs` | Map `/health/live` with no checks and `/health/ready` with the `ready`-tagged `AddDbContextCheck<SchedulerDbContext>`; configure the database check timeout to 5 seconds and the runtime connection `Connect Timeout` to 3 seconds; disable EF readiness retry. | Live returns `200` when SQL is unavailable; ready returns `503` on connection failure; readiness uses only the runtime identity. | Implemented |
| 1.6 | S4, S10, AC-2, AC-3 | `tests/ServiceScheduler.UnitTests/Configuration/DatabaseOptionsValidatorTests.cs` | Add unit tests for missing, malformed, missing-component, `sa`, infinite-timeout, and valid connection strings without opening SQL. | Tests cover each validation rule and assert messages name the configuration key without exposing values. | Implemented |
| 1.7 | S1, S4, S9, S10, AC-3, AC-6, AC-7; TC-01, TC-02, TC-03, TC-04, TC-14, TC-15, TC-20 | `tests/ServiceScheduler.IntegrationTests/Fixtures/SchedulerApiFactory.cs`, `Startup/StartupValidationTests.cs`, `Health/HealthEndpointTests.cs`, `OpenApi/OpenApiTests.cs`, `Startup/ProblemDetailsTests.cs` | Add a `WebApplicationFactory<Program>` that supplies runtime configuration and a controllable clock; test missing/malformed startup failure, liveness `200` and readiness `503` against a syntactically valid non-listening SQL endpoint using a `scheduler_app` runtime connection, OpenAPI JSON, Swagger UI, and unknown-route Problem Details. | The integration project executes these scenarios without a SQL container; failures identify the missing key; responses contain no stack trace or secret. | Implemented |
| 1.8 | S4, AC-1, AC-3 | `docs/setup-guide.md`, `README.md` | Record the actual toolchain pins, project paths, configuration names, and verified restore/build/format/test commands once executed; keep unexecuted Compose and database steps marked pending. | Documentation matches the implemented scaffold and distinguishes planned from verified commands. | Implemented |

## Verification

> [!NOTE]
> Implementation was completed on 2026-10-31 and the commands were exercised locally. Formal Passed/Failed/Blocked results are recorded by the test phase in [[test/1-test|the ticket 1 test report]]; the rows below remain the planning baseline.

| Requirement IDs | Scenario / command (planned) | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| S2, S3, AC-1 | `dotnet restore`; `dotnet build --no-restore` | Restore and build succeed with the pinned SDK and package graph; no floating `latest` references. | Not run |
| S2, S10, AC-2, TC-16 | `dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj`; `dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj` | Both projects execute; unit and Phase 1 integration tests pass; counts and revision are recorded. | Not run |
| S4, AC-3, TC-01 | Start with `ConnectionStrings:Runtime` missing | Startup fails before serving; the error names the missing key and contains no secret value. | Not run |
| S4, AC-3, TC-02 | Start with a malformed runtime connection string | Startup fails with a clear validation message; no SQL connection attempt is needed to detect it. | Not run |
| S9, AC-6, T-14, TC-03 | Runtime connection to a syntactically valid non-listening SQL endpoint; request `/health/live` | `200` while the process runs. | Not run |
| S9, AC-6, T-14, TC-04 | Same non-listening endpoint; request `/health/ready` | `503` within the configured check timeout. | Not run |
| S9, AC-7, TC-14 | Request `/openapi/v1.json` | `200` with valid JSON and document metadata; an empty `paths` object is acceptable for ticket 1. | Not run |
| S9, AC-7, TC-15 | Request `/swagger/index.html` | Swagger UI loads and references `/openapi/v1.json`. | Not run |
| S1, AC-7, TC-20 | Request an unknown route | `404` with a consistent Problem Details body and trace ID; no stack trace or configuration value. | Not run |
| S4, AC-3, TC-17 | `dotnet format --verify-no-changes` | Exit code 0. | Not run |
| S11, AC-9 | Review README and setup-guide changes against the scaffold | Commands and paths match the implementation; unexecuted checks remain labelled. | Not run |

## Impacts and remaining decisions

- Database / migrations / cache: registers an empty `SchedulerDbContext` for readiness only; no entity model, migration, seed, runtime login, or cache key is added in this phase.
- API / configuration / deployment: adds `ConnectionStrings:Runtime` and the host conventions; no `ConnectionStrings:Setup`, Compose, or container packaging yet.
- Documentation: Phase 1 updates toolchain and command sections; Phase 5 consolidates cross-document status.
- Restore exposed NuGet audit warnings NU1902/NU1903 for the transitive `System.Security.Cryptography.Pkcs` `9.0.11` dependency of `Microsoft.Data.SqlClient` `6.1.7`. The implementation pins `System.Security.Cryptography.Pkcs` `10.0.13` in central package management as a security override; all other G-01 package selections are unchanged. Remove the override only when the SqlClient dependency is repaired.
- Open decisions for implementation: final solution format (`.slnx` selected; fall back to `.sln` only if tooling rejects it, with the deviation recorded), exact Problem Details test shape, and whether `UseStatusCodePages()` alone or an explicit exception handler is required to produce the required bodies.
- Handoff: Phase 2 replaces the empty model with the full shared-domain model and migration; Phase 3 adds initialized readiness and the restricted runtime identity.
