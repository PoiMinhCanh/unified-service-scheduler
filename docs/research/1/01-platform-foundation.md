---
title: "1 - Init Project: Platform Foundation Research"
tags: [research, service-scheduler, foundation, api, configuration]
ticket: 1
status: verified
updated: 2026-10-31
---

# 1 - Init Project: Platform Foundation Research

- Ticket: [[1 - Init Project]]
- Parent research: [[research/1/00-overview|Foundation research overview]]
- Part: 1 of 3
- Requirement coverage: S1-S4, S8-S9, S11; AC-1, AC-3, AC-6, AC-7, AC-8, AC-9; T-14, T-16; foundation portions of D-01-D-03 and P-04.
- Evidence baseline: research baseline revision `25be1577c895204e58bc74b4f80336486d33bb9c`; rechecked at current revision `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` (2026-10-31); inspected checkout has no application files.

> [!IMPORTANT]
> This part proposes the API/platform scaffold. It is not implementation evidence, and no command below has been executed against an application that does not yet exist.

## Scope of this part

Cover the solution and project layout, version pinning, API host conventions, configuration and startup validation, Problem Details, OpenAPI/Swagger, health endpoints, Docker/Compose packaging, local lint/format commands, and the documentation updates that describe them.

Persistence, migrations, identities, and seeds are in [[research/1/02-persistence-and-identity|Part 2]]. Test projects, fixtures, and executed scenarios are in [[research/1/03-testing-and-verification|Part 3]].

## Current architecture and files

| Component            | Actual inspected evidence                                                                                                                                                                     | Relevant behavior / gap                                                                                       |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Repository root      | Only `.claude/`, `.git/`, `.obsidian/`, `docs/`, `.gitignore`, `.mcp.json`, `CLAUDE.md`, and `README.md` are present; the earlier ignored `.serena/` directory is absent in the 2026-10-31 recheck. | No `src/`, `tests/`, solution, `global.json`, `compose.yaml`, `.env.example`, or Dockerfile.                  |
| Project status       | `README.md:17` says application code, migrations, containers are not scaffolded; `README.md:92` says build/startup/migration/seed/test commands are unavailable.            | Documentation is the only current artifact.                                                                   |
| Planned layout       | `docs/setup-guide.md:61-88` lists a planned `ServiceScheduler/` tree and says only documentation and workflow configuration exist.                                                            | Planned paths must not be treated as existing files.                                                          |
| Planned API behavior | `docs/architecture.md:68-97` defines the eventual API boundary, Problem Details expectations, and health endpoints.                                                                           | Ticket 1 implements only health/OpenAPI and foundation conventions; business endpoints remain planned.        |
| Local toolchain      | 2026-10-31 recheck: `dotnet --list-sdks` reports `8.0.100` and `9.0.315`; `dotnet --version` reports `9.0.315`; Docker server `25.0.3`; Compose `v2.24.6-desktop.1`. The 2026-10-30 baseline reported .NET `3.1.426`/`10.0.401` and Docker `29.7.2`/Compose `v5.3.1`, which the current environment does not reproduce. | The selected .NET 10 SDK is absent on the host; use the pinned Docker SDK image for scaffolding or optionally install it for host execution. Docker tooling is present; SDK image execution remains unverified. This does not verify application startup. |
| Ignore rules         | `.gitignore` ignores `.env`, `.env.*` except examples, `appsettings.Local.json` variants, `bin/`, `obj/`, `TestResults/`, coverage, `.vs/`, `.idea/`, `.obsidian/`, `.serena/`, and `.data/`. | Local secrets and build outputs have an ignore baseline; tracked-file review is still required.               |

## Phase 1: Layer coverage sweep

| Component                            | What to inspect                                                                             | Status / evidence                                                                                          |
| ------------------------------------ | ------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| API contract and validation          | Controller registration, DTO boundary, explicit timestamp offsets, Problem Details, OpenAPI | Missing — proposed addition. Ticket 1 has no business endpoint; conventions and middleware are scaffolded. |
| Scheduling rules                     | Duration, eligibility, ownership, opening/shift coverage, TimeProvider                      | N/A — ticket 2/3 own scheduling behavior; no rule functions in ticket 1.                                   |
| Resource selection and availability  | Bounded queries, stable order, advisory availability                                        | N/A — ticket 2 owns it.                                                                                    |
| Domain model and EF mappings         | Entities, SQL types, relationships, snapshots                                               | Missing — Part 2.                                                                                          |
| EF Core migrations and seeds         | Migration SQL, model snapshot, seed lifecycle                                               | Missing — Part 2.                                                                                          |
| Booking procedure and transactions   | Locking, revalidation, conflict checks, commit/rollback                                     | N/A — explicitly excluded; ticket 3 owns it.                                                               |
| Runtime permissions                  | Restricted principal, procedure execution, denied direct writes                             | Missing — Part 2.                                                                                          |
| Failure and retry handling           | Startup validation, Problem Details, readiness; booking retries                             | Foundation-only proposed changes; booking retries excluded.                                                |
| Redis catalog cache                  | Key, payload, TTL, fallback                                                                 | N/A — ticket 2 owns Redis; ticket 1 has no cache impact.                                                   |
| Configuration and packaging          | SDK/package/image pins, config contract, Compose, volume, init, health                      | Missing — proposed addition in this part.                                                                  |
| Telemetry                            | Serilog, traces, metrics, exporters                                                         | N/A — ticket 4; ticket 1 uses framework logging and remains runnable without telemetry.                    |
| Unit tests                           | Pure rules, validation, time boundaries                                                     | Missing — Part 3.                                                                                          |
| API and dependency integration tests | WebApplicationFactory, Testcontainers, permissions, health                                  | Missing — Part 3.                                                                                          |
| Operational verification      | Restart persistence, outages, clean checkout                                            | Local operational checks proposed.                                                |
| Documentation and delivery           | Architecture, README, setup, database docs, test report                                     | Existing planned documents must change after implementation.                                               |
| UI or new integrations               | Custom UI or external integration                                                           | N/A — backend-only ticket; Swagger UI is generated.                                                        |

## Phase 2: Data-flow trace for the foundation paths

### 2.1 Startup and configuration

Proposed flow:

```text
process environment + appsettings
  -> configuration provider
  -> DatabaseOptions binding
  -> validator: required key present, SQL connection string parses, database name present
  -> ValidateOnStart
  -> host starts (no SQL connection attempt)
```

Important boundary: startup validation must not open a SQL connection. If it did, an unavailable SQL Server would prevent process startup and make `/health/live` unreachable, contradicting AC-6 and T-14. Database reachability belongs to the readiness check.

| Path                  | Flow to inspect                                                                   | Important boundary                                                               | Evidence / gap      |
| --------------------- | --------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- | ------------------- |
| Host startup          | `Program.cs` -> configuration -> options validation -> Kestrel                    | Fail fast on missing/invalid required configuration, not on dependency downtime. | Missing — proposed. |
| Runtime configuration | Environment variables / `appsettings` -> `ConnectionStrings:Runtime` -> DbContext | Runtime identity only; setup connection is not loaded by the API.                | Missing — proposed. |
| Setup configuration   | `.env` / Compose environment -> `ConnectionStrings:Setup` -> setup step           | Privileged identity; never exposed by the API.                                   | Missing — Part 2.   |

### 2.2 Health paths

| Path             | Flow to inspect                                                    | Important boundary                                                                      | Evidence / gap                                  |
| ---------------- | ------------------------------------------------------------------ | --------------------------------------------------------------------------------------- | ----------------------------------------------- |
| Liveness         | `GET /health/live` -> no dependency checks                         | Must stay `200` when SQL Server is down.                                                | Missing — proposed.                             |
| Readiness        | `GET /health/ready` -> database health check -> runtime connection | Returns `503` when SQL Server is unavailable; `200` after a ready initialized database. | Missing — proposed; T-14.                       |
| OpenAPI document | `GET /openapi/v1.json` -> OpenAPI metadata                         | Must be generated without a database connection.                                        | Missing — proposed; no business endpoints yet.  |
| Swagger UI       | `GET /swagger/index.html` -> generated UI                          | Must load against the OpenAPI document.                                                 | Missing — proposed.                             |
| Problem Details  | Unknown route / unhandled exception -> Problem Details middleware  | Consistent response shape with trace ID; no stack traces or secrets.                    | Missing — proposed; limited ticket-1 endpoints. |

### 2.3 Runtime request path

Ticket 1 has no business endpoint. The proposed request pipeline is:

```text
HTTP request -> routing -> controller (future ticket 2/3) -> application/scheduling use case
  -> EF Core read/procedure (future) -> response DTO
```

The only ticket-1 HTTP endpoints are health and documentation. This is a deliberate scope limitation, not a claim that controller/DTO/asynchronous/cancellation behavior has been exercised.

## Phase 3: UX state matrix

N/A — ticket 1 introduces no custom UI. Swagger UI is a generated API client and is checked only for accessibility (AC-7); no user-visible state matrix is required. Request/response validation for future endpoints belongs to tickets 2 and 3.

## Phase 3b: Pre-audit verification gates

### Gate 1: Candidate internal structure verification

| Candidate file / symbol | Why it matched                                              | Actual relevant contents                                                           | Requirement link | Verdict                          |
| ----------------------- | ----------------------------------------------------------- | ---------------------------------------------------------------------------------- | ---------------- | -------------------------------- |
| `README.md`             | Project scope and setup entry point                         | States implementation is pending and commands are unavailable (`README.md:17,92`). | S11, AC-9        | Must Change after implementation |
| `docs/architecture.md`  | Planned API boundary/health/version decisions               | Planned design and proposed components only (`docs/architecture.md:12-13,68-97`).  | S1, S9, S11      | Must Change after implementation |
| `docs/setup-guide.md`   | Planned layout and startup sequence                         | Planned tree and pending commands (`docs/setup-guide.md:11-12,61-139`).            | S3, S4, S8       | Must Change after implementation |
| `.gitignore`            | Secret/build ignore baseline                                | Covers `.env*` examples, build/test outputs, local tool data.                      | S4, S8           | No Change                        |
| `docs/database/*`       | Persistence guidance                                        | Planned model; no mappings or migrations.                                          | S5-S7            | Part 2                           |
| Application candidates  | No `src/`, `tests/`, `global.json`, or Compose file exists. | Nothing to inspect.                                                                | S1-S10           | Missing — proposed addition      |

### Gate 2: Call path and boundary verification

1. No application entry point exists, so no DI registration, middleware order, health-check registration, or OpenAPI route can be traced today.
2. The proposed `Program.cs` flow is a design proposal, not an inspected call chain.
3. No cache-hit, miss, transaction, retry, or error path exists in ticket 1 beyond the proposed startup/health paths.
4. The target code does not execute on any current path because the code does not exist; all classifications are proposed additions.

| Entry point               | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence                      |
| ------------------------- | ------------------------------ | ------------------------------- | --------------- | --------------------------------------------- |
| `GET /health/live`        | None                           | No dependencies                 | No              | Proposed endpoint; create and test.           |
| `GET /health/ready`       | None                           | Runtime SQL connection          | No              | Proposed endpoint; create and test.           |
| `GET /swagger/index.html` | None                           | None                            | No              | Proposed endpoint; create and test.           |
| `GET /openapi/v1.json`    | None                           | None                            | No              | Proposed endpoint; create and test.           |
| API startup               | None                           | Configuration only              | No              | Proposed options validation; create and test. |

## Phase 4: Code audit

| Classification    | File / symbol / component                                            | Current behavior / explicit gap                      | Proposed change or reason for no change                                                               | Requirement IDs              | Gate evidence                      |
| ----------------- | -------------------------------------------------------------------- | ---------------------------------------------------- | ----------------------------------------------------------------------------------------------------- | ---------------------------- | ---------------------------------- |
| Must Change       | `README.md`                                                          | Pending implementation status.                       | Record verified restore/build/run/test/format/migration/seed/Compose commands and implemented status. | S11, AC-1, AC-9, D-03        | `README.md:17,92`                  |
| Must Change       | `docs/architecture.md`                                               | Planned design; no actual paths/status.              | Record actual layout, config, health, OpenAPI, identity, and implemented-vs-planned status.           | S1, S9, S11, D-01            | `docs/architecture.md:12-13`       |
| Must Change       | `docs/setup-guide.md`                                                | Planned layout and pending commands.                 | Replace with actual prerequisites, config names, commands, and troubleshooting evidence.              | S3, S4, S8, AC-1, AC-3, AC-8 | `docs/setup-guide.md:11-12,61-139` |
| Must Change       | `docs/README.md`                                                     | Indexes ticket 1 as pending.                         | Update status and links after implementation.                                                         | S11, AC-9                    | Document index                     |
| Proposed Addition | `global.json`                                                        | Missing SDK pin.                                     | Pin `10.0.401` with stable roll-forward policy.                                                       | S3, AC-1                     | SDK check in chosen execution environment                |
| Proposed Addition | `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` | Missing central build, package, and format settings. | Add nullable/implicit usings/analyzers, central versions, and style rules.                            | S2-S4, AC-3                  | Missing                            |
| Proposed Addition | Solution file (`.slnx` or `.sln`)                                    | Missing.                                             | Add the API, unit-test, and integration-test projects.                                                | S2, AC-2                     | Missing                            |
| Proposed Addition | `src/ServiceScheduler.Api/Program.cs`                                | Missing host.                                        | Add controller/DI/options/Problem Details/OpenAPI/health composition.                                 | S1, S4, S9                   | Missing                            |
| Proposed Addition | API feature/infrastructure folders                                   | Missing.                                             | Add the single API project with feature folders and a small persistence/infrastructure boundary.      | S1, S2, S5                   | Missing                            |
| Proposed Addition | `appsettings.json`, `appsettings.Development.json`, `.env.example`   | Missing configuration contract.                      | Add non-secret defaults/placeholders and document environment-variable names.                         | S4, S8                       | Missing                            |
| Proposed Addition | API Dockerfile                                                       | Missing.                                             | Add a pinned multi-stage build/runtime image.                                                         | S8, AC-8                     | Missing                            |
| Proposed Addition | `compose.yaml`                                                       | Missing.                                             | Add SQL Server Developer, named volume, healthcheck, setup one-shot, and API service.                 | S8, AC-8                     | Missing                            |
| Proposed Addition | Local verification command documentation/script                      | Missing.                                             | Document `dotnet format --verify-no-changes`, build, test, Compose, migration, and seed commands.     | S4, AC-1, AC-3               | Missing                            |
| No Change         | `.gitignore`                                                         | Already covers local secrets and build outputs.      | No change unless a new generated artifact appears.                                                    | S4, S8                       | `.gitignore`                       |
| No Change         | `.claude/`, `CLAUDE.md`, `.mcp.json`                                 | Workflow configuration, not application behavior.    | No change for ticket 1.                                                                               | Context                      | Inspected root/config              |

## Phase 5: Gap analysis for this part

| ID   | Type     | Question / risk                                                                            | Proposed answer / mitigation                                                                                                                                                  | Requirement impact       | Status                                       |
| ---- | -------- | ------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------ | -------------------------------------------- |
| G-01 | Decision | Exact package versions.                                                                    | Use the SDK and package baseline in section 6.1 below; transfer exact versions to central package management and the local tool manifest during scaffolding.                  | S3, AC-1                 | Resolved for planning; SDK container execution and restore/build pending |
| G-09 | Decision | Health-check implementation.                                                               | Adopt accepted resolution G-09 in the overview's accepted resolutions table; earlier alternatives are superseded. | S9, AC-6, T-14           | Resolved for planning; execution pending |
| G-10 | Decision | How to evidence controllers/DTOs/async/cancellation without a business endpoint.           | Adopt accepted resolution G-10 in the overview's accepted resolutions table; earlier alternatives are superseded. | S1, AC-7                 | Resolved for planning; execution pending |
| G-11 | Decision | OpenAPI/Swagger package and route.                                                         | Adopt accepted resolution G-11 in the overview's accepted resolutions table; earlier alternatives are superseded. | S9, AC-7                 | Resolved for planning; execution pending |
| G-14 | Decision | SQL Server and .NET base-image tags/digests.                                               | Adopt accepted resolution G-14 in the overview's accepted resolutions table; earlier alternatives are superseded. | S3, S8, S10              | Resolved for planning; execution pending |
| G-15 | Decision | Configuration names and secret loading.                                                    | Adopt accepted resolution G-15 in the overview's accepted resolutions table; earlier alternatives are superseded. | S4, S6, S8               | Resolved for planning; execution pending |
| G-21 | Risk     | Startup validation opening a SQL connection would break liveness during database downtime. | Adopt accepted resolution G-21 in the overview's accepted resolutions table; earlier alternatives are superseded. | S4, S9, AC-3, AC-6, T-14 | Mitigation selected; verification pending |
| G-22 | Risk     | Compose initialization may block API startup indefinitely or run with runtime credentials. | Adopt accepted resolution G-22 in the overview's accepted resolutions table; earlier alternatives are superseded. | S6-S8, AC-4, AC-8        | Mitigation selected; verification pending |
| G-23 | Decision | Dockerfile location and build context.                                                     | Adopt accepted resolution G-23 in the overview's accepted resolutions table; earlier alternatives are superseded. | S8, AC-8                 | Resolved for planning; execution pending |
| G-24 | Risk     | Local SQL certificate trust setting leaking into non-local configuration.                  | Adopt accepted resolution G-24 in the overview's accepted resolutions table; earlier alternatives are superseded. | S8                       | Mitigation selected; verification pending |
| G-25 | Decision | `dotnet format` scope and whether warnings are errors.                                     | Adopt accepted resolution G-25 in the overview's accepted resolutions table; earlier alternatives are superseded. | S4, AC-3                 | Resolved for planning; execution pending |
| G-26 | Decision | OpenAPI document completeness in ticket 1.                                                 | Adopt accepted resolution G-26 in the overview's accepted resolutions table; earlier alternatives are superseded. | S9, AC-7                 | Resolved for planning; execution pending |
| G-27 | Decision | Local HTTP/HTTPS behavior.                                                                 | Adopt accepted resolution G-27 in the overview's accepted resolutions table; earlier alternatives are superseded. | S8, AC-8                 | Resolved for planning; execution pending |

## Phase 6: Proposed changes and verification

The accepted resolutions in [[research/1/00-overview#Accepted resolutions for G-03 through G-43 (2026-10-31)]] govern this part. They select setup command mode, exact image pins, timeouts, configuration loading, Dockerfile location, warnings-as-errors, and loopback HTTP binding. Examples below remain unbuilt.

### 6.1 Toolchain and solution

| Step | Requirement IDs | Proposed files                                              | Change                                                                   | Verification                         |
| ---- | --------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------ | ------------------------------------ |
| 1.1  | S2, S3, AC-1    | `global.json`                                               | Pin `10.0.401`; set a stable roll-forward policy and disable prerelease. | `dotnet --version`; restore/build.   |
| 1.2  | S2, S3, AC-1    | `Directory.Packages.props`, `Directory.Build.props`         | Central package versions, nullable/implicit usings/analyzer settings.    | Restore/build; no floating versions. |
| 1.3  | S2, AC-2        | `ServiceScheduler.slnx` (or `.sln`) and three project files | Add API, unit-test, and integration-test projects with references.       | `dotnet sln list`/build.             |
| 1.4  | S4, AC-3        | `.editorconfig`                                             | Define formatting/style rules used by `dotnet format`.                   | Format check.                        |

Proposed repository layout (paths are planned, not existing):

```text
src/ServiceScheduler.Api/
  Program.cs
  Features/                      # future ticket 2/3 feature folders
  Infrastructure/Persistence/    # DbContext, entity configurations, seeds
  Migrations/                    # EF Core migrations
tests/ServiceScheduler.UnitTests/
tests/ServiceScheduler.IntegrationTests/
  Fixtures/
  Startup/
  Health/
  OpenApi/
  Persistence/
  Security/
```

#### G-01 resolution: SDK and package baseline (2026-10-31)

Keep SDK `10.0.401` as the planning pin, with `rollForward: disable` and `allowPrerelease: false` in the future `global.json`; target `net10.0` in all projects. The original 2026-10-30 environment reported this SDK, but this session's `dotnet --list-sdks` reports only `8.0.100` and `9.0.315`. Use the pinned Docker SDK image for scaffolding and CLI verification by default; installing the selected SDK on the host is optional for host CLI/IDE tooling. Verify the SDK version inside the chosen execution environment before scaffolding; container execution is not yet verified. See [[setup-guide]] for the repository-mounted SDK command.

Use these exact stable package versions as the initial scaffold baseline. The linked publisher package records establish published versions and target-framework support; compatibility of the complete graph still requires restore/build and smoke tests. This is a reproducible baseline, not a claim that each version is the newest release.

| Package / tool                                                      | Selected version | Purpose / evidence                                                                                                                                  |
| ------------------------------------------------------------------- | ---------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Microsoft.EntityFrameworkCore.SqlServer`                           | `10.0.13`        | SQL Server provider; [NuGet record](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer/10.0.13).                                |
| `Microsoft.EntityFrameworkCore.Design`                              | `10.0.13`        | Migration design services, with `PrivateAssets=all`; [NuGet record](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.13).   |
| `dotnet-ef`                                                         | `10.0.13`        | Local tool manifest, matching EF runtime/design versions; [NuGet record](https://www.nuget.org/packages/dotnet-ef/10.0.13).                         |
| `Microsoft.AspNetCore.OpenApi`                                      | `10.0.13`        | Built-in document generation; [NuGet record](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi/10.0.13).                                  |
| `Swashbuckle.AspNetCore.SwaggerUI`                                  | `10.0.1`         | UI only, pointed at `/openapi/v1.json`; [NuGet record](https://www.nuget.org/packages/Swashbuckle.AspNetCore.SwaggerUI/10.0.1).                     |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | `10.0.13`        | Database readiness check; [NuGet record](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore/10.0.13). |
| `Microsoft.AspNetCore.Mvc.Testing`                                  | `10.0.13`        | Integration-test `WebApplicationFactory`; [NuGet record](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.13).                  |
| `xunit.v3`                                                          | `3.2.2`          | Both test projects; [NuGet record](https://www.nuget.org/packages/xunit.v3/3.2.2).                                                                  |
| `xunit.runner.visualstudio`                                         | `3.1.5`          | VSTest adapter, with `PrivateAssets=all`; [NuGet record](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5).                           |
| `Microsoft.NET.Test.Sdk`                                            | `18.0.1`         | `dotnet test` via VSTest; [NuGet record](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.0.1).                                             |
| `Testcontainers.MsSql`                                              | `4.14.0`         | Real SQL Server fixture; [NuGet record](https://www.nuget.org/packages/Testcontainers.MsSql/4.14.0).                                                |
| `Microsoft.Extensions.TimeProvider.Testing`                         | `10.0.0`         | Test-only `FakeTimeProvider`; [NuGet record](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.0.0).                      |

Pin package references in `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; pin `dotnet-ef` separately in `.config/dotnet-tools.json`. Use xUnit v3 executable test projects (`OutputType=Exe`) with the VSTest adapter and test SDK above; keep the default VSTest runner rather than enabling Microsoft.Testing.Platform during this scaffold. Do not add the deprecated `xunit` v2 package or the full Swashbuckle generator bundle alongside built-in OpenAPI generation.

At scaffolding, restore tools/packages, inspect dependency conflicts and vulnerability audit results, build, verify test discovery/execution, and smoke-test both OpenAPI routes and SQL readiness. Record actual results in the ticket test report; revise the baseline explicitly if a check exposes incompatibility. None of those checks has run. Container tags/digests are selected in the overview's G-14 resolution; container execution verification remains pending.

### 6.2 API host and configuration

Proposed `Program.cs` shape (illustrative, not compiled):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    // SchedulerDbContext is registered in Part 2 and uses ConnectionStrings:Runtime.
    .AddDbContextCheck<SchedulerDbContext>("database", tags: ["ready"]);

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("ConnectionStrings"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Runtime),
        "ConnectionStrings:Runtime is required.")
    .Validate(o => DatabaseOptions.IsParseable(o.Runtime),
        "ConnectionStrings:Runtime is invalid.")
    .ValidateOnStart();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "ServiceScheduler v1"));

app.MapOpenApi();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
```

The proposed `DatabaseOptions` exposes a `Runtime` property bound from the `ConnectionStrings` section; `IsParseable` constructs a `SqlConnectionStringBuilder` and returns false on `ArgumentException`. Per accepted G-21, validation also requires server, database, SQL user, and password values, rejects the privileged `sa` identity for runtime use, and requires a finite connect timeout; the illustrative validators above are expanded accordingly during implementation. The API does not bind or use `ConnectionStrings:Setup`. The validator does not open a connection: a parse or component failure produces a clear configuration error, while a syntactically valid but unreachable server remains a readiness failure.

Proposed configuration keys:

| Key                                                        | Consumer           | Purpose                                                                  |
| ---------------------------------------------------------- | ------------------ | ------------------------------------------------------------------------ |
| `ConnectionStrings:Runtime` / `ConnectionStrings__Runtime` | API                | Restricted runtime login used for readiness and future reads.            |
| `ConnectionStrings:Setup` / `ConnectionStrings__Setup`     | Setup step only    | Privileged migration/seed/principal connection; never loaded by the API. |
| `MSSQL_SA_PASSWORD`                                        | Compose SQL Server | Local administrator password; placeholders only in `.env.example`.       |
| `MSSQL_PID`                                                | Compose SQL Server | `Developer` for local development/testing.                               |
| `ASPNETCORE_ENVIRONMENT`                                   | API                | Environment name; no secrets.                                            |

Configuration names above are selected; record their implemented wiring in [[setup-guide]]. Setup additionally requires `Scheduler__RuntimePassword` to provision the runtime login without supplying the runtime connection string to setup. Compose interpolates ignored `.env` values explicitly per service; local `dotnet` runs require exported environment variables and do not load `.env` automatically.

Proposed `.env.example` placeholders (never real credentials):

```dotenv
MSSQL_SA_PASSWORD=Replace_with_local_strong_password
Scheduler__RuntimePassword=Replace_with_runtime_password
MSSQL_PID=Developer
ConnectionStrings__Setup=Server=<sql-host>,1433;Database=ServiceScheduler;User Id=sa;Password=Replace_with_local_strong_password;Encrypt=True;TrustServerCertificate=True
ConnectionStrings__Runtime=Server=<sql-host>,1433;Database=ServiceScheduler;User Id=scheduler_app;Password=Replace_with_runtime_password;Encrypt=True;TrustServerCertificate=True
ASPNETCORE_ENVIRONMENT=Development
```

Use `localhost` for host-to-container commands and the Compose service name (for example, `sqlserver`) for container-to-container connections; document both forms. The local `TrustServerCertificate=True` setting is scoped to this demo connection.

### 6.3 Problem Details, controllers, and DTO conventions

- Register `AddControllers()` and `MapControllers()`; do not add generic repositories, mediator, or runtime AI dependencies.
- Register `AddProblemDetails()` and use the exception/status-code middleware so future controllers return consistent Problem Details with a trace ID.
- Keep controllers focused on HTTP request/response handling; place DTOs in feature-local contract folders when ticket 2/3 add endpoints.
- Propagate cancellation tokens through future asynchronous boundaries; no blocking `Result`, `Wait`, or `Thread.Sleep`.
- Ticket 1 cannot prove controller/DTO/async/cancellation behavior without a business endpoint. Record this as a gap rather than adding an unrelated business route.

### 6.4 OpenAPI and Swagger UI

- Expose the OpenAPI document at the selected `/openapi/v1.json` route.
- Expose Swagger UI at `/swagger` using a pinned compatible package.
- Verify both endpoints return success without a database connection.
- Document that the ticket-1 document is minimal until tickets 2 and 3 add endpoints.

### 6.5 Health checks

- `/health/live`: no dependency checks; returns `200` while the process is running.
- `/health/ready`: database check tagged `ready`; returns `503` when the runtime connection cannot reach SQL Server.
- Use the runtime identity, not the setup identity.
- Do not run migrations or seed at API startup; initialization is a separate privileged step.
- Use a 5-second check timeout, 3-second SQL connect timeout, and no EF readiness retry policy, as selected in G-09.

### 6.6 Docker and Compose

Selected API Dockerfile shape (illustrative, unbuilt; pins verified in overview G-14):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ServiceScheduler.Api/ src/ServiceScheduler.Api/
RUN dotnet restore src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
RUN dotnet publish src/ServiceScheduler.Api/ServiceScheduler.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ServiceScheduler.Api.dll"]

FROM runtime AS setup
CMD ["--db-setup"]
```

Selected Compose shape (illustrative, not executed; build both targets from `src/ServiceScheduler.Api/Dockerfile` with repository-root context):

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-CU23-ubuntu-22.04@sha256:0ec7739e1c5ec2f57861facbe1f2b74f1d3e147c7c97edf91eeea920c5944d9c
    platform: linux/amd64
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_PID: "Developer"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD}"
    volumes:
      - scheduler-sql-data:/var/opt/mssql
    healthcheck:
      test:
        [
          "CMD-SHELL",
          '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -C -Q ''SELECT 1'' || exit 1',
        ]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s
  db-setup:
    build:
      context: .
      target: setup
      dockerfile: src/ServiceScheduler.Api/Dockerfile
    depends_on:
      sqlserver:
        condition: service_healthy
    environment:
      ConnectionStrings__Setup: "${ConnectionStrings__Setup}"
      Scheduler__RuntimePassword: "${Scheduler__RuntimePassword}"
    restart: "no"
  api:
    build:
      context: .
      target: runtime
      dockerfile: src/ServiceScheduler.Api/Dockerfile
    depends_on:
      db-setup:
        condition: service_completed_successfully
    environment:
      ConnectionStrings__Runtime: "${ConnectionStrings__Runtime}"
      ASPNETCORE_ENVIRONMENT: "Development"
    ports:
      - "127.0.0.1:8080:8080"
volumes:
  scheduler-sql-data:
```

Documentation must cover: x86-64 Linux containers, adequate memory for SQL Server, EULA acceptance, Developer edition, local secret handling, named volume mount at `/var/opt/mssql`, the explicit initialization step, and scoped local certificate trust. The `sqlcmd` path must be verified against the selected image; do not assume it exists at that path.

### 6.7 Local lint/format/build/test commands

Proposed documented commands (working directory: repository root; final commands pending scaffolding):

```powershell
dotnet restore
dotnet build --no-restore
dotnet format --verify-no-changes
dotnet test --no-build
docker compose config
docker compose up -d sqlserver
# run the documented privileged initialization step
docker compose up -d api
```

The exact solution/project paths and command working directories must be recorded in [project README](../../../README.md) and [[setup-guide]] after scaffolding and successful execution.

### 6.8 Documentation changes

- Update [project README](../../../README.md) with current status, prerequisites, setup, and verified commands.
- Update [[architecture]] with actual component paths, configuration, health/OpenAPI behavior, and implemented-vs-planned status.
- Update [[setup-guide]] with exact configuration names, initialization order, Compose commands, and troubleshooting.
- Update [[README]] status and links.
- Add `docs/deploy-notes/1-deploy-notes.md` for initialization/recovery steps.
- Record executed results in `docs/test/1-test.md` during the test phase.

## Rollback or forward recovery

The platform changes are code, configuration, and container changes: revert the commit and recreate local containers. The named SQL volume is persistent and its recovery path is covered in the overview and Part 2. No production deployment is in scope. Status: planned; Not run.

## Verification scenarios

| Requirement IDs | Scenario / command                                             | Expected outcome                                                                      | Status  |
| --------------- | -------------------------------------------------------------- | ------------------------------------------------------------------------------------- | ------- |
| S2, S3, AC-1    | Restore/build from a clean checkout                            | Restore and build succeed with pinned SDK/packages.                                   | Not run |
| S4, AC-3        | Start with missing runtime connection string                   | Startup fails with a clear message naming the missing key.                            | Not run |
| S4, AC-3        | Start with a malformed connection string                       | Startup fails with a clear validation message.                                        | Not run |
| S9, AC-6, T-14  | `GET /health/live` with SQL Server stopped                     | `200` remains available.                                                              | Not run |
| S9, AC-6, T-14  | `GET /health/ready` with SQL Server stopped                    | `503`.                                                                                | Not run |
| S9, AC-6, T-14  | `GET /health/ready` after initialization                       | `200`.                                                                                | Not run |
| S9, AC-7        | `GET /openapi/v1.json`, `GET /swagger/index.html`              | Both accessible.                                                                      | Not run |
| S8, AC-8        | `docker compose config`, Compose startup, named-volume restart | Configuration valid; initialization succeeds; data persists while the volume remains. | Not run |
| S4, AC-3        | `dotnet format --verify-no-changes`                            | Exit code 0.                                                                          | Not run |
| S11, AC-9       | Documentation review                                           | README/architecture/setup distinguish implemented from planned.                       | Not run |

## Phase 7: Part scope verification

| Change                           | Requirement IDs      | Direct requirement or necessary dependency  | Verification                          |
| -------------------------------- | -------------------- | ------------------------------------------- | ------------------------------------- |
| Toolchain/solution/projects      | S2-S4, AC-1, AC-2    | Required scaffold and reproducible build    | Restore/build/format.                 |
| API host and DI                  | S1, AC-1, AC-7       | Required API foundation                     | Startup/OpenAPI smoke.                |
| Configuration/startup validation | S4, AC-3             | Required fail-fast configuration behavior   | Missing/invalid config tests.         |
| Problem Details conventions      | S1, AC-7             | Required consistent API errors              | Future endpoint tests; 404 smoke now. |
| OpenAPI/Swagger                  | S9, AC-7             | Required API client interface               | HTTP smoke.                           |
| Health endpoints                 | S9, AC-6, T-14       | Required liveness/readiness behavior        | Outage/recovery checks.               |
| Docker/Compose/volume/init       | S8, AC-8, T-16       | Required local reproducibility              | Compose startup/restart checks.       |
| Lint/format commands             | S4, AC-3             | Required local checks                       | Format check.                         |
| Documentation updates            | S11, AC-9, D-01-D-03 | Required implemented-vs-planned distinction | Document review.                      |
| Booking/Redis/telemetry       | Excluded             | Not ticket 1                                | No change.                            |

## Handoffs

| Finding / decision                                                        | Target     | Required action                                                               |
| ------------------------------------------------------------------------- | ---------- | ----------------------------------------------------------------------------- |
| Proposed `ConnectionStrings:Runtime` and `ConnectionStrings:Setup` names. | Part 2     | Use the same names in the setup step, principal SQL, and tests.               |
| Proposed health check uses the runtime identity.                          | Part 2/3   | Create the runtime login before readiness tests.                              |
| Proposed Compose `db-setup` one-shot blocks API startup.                  | Part 2/3   | Define the explicit init command, idempotency, and failure behavior.          |
| No business controller in ticket 1.                                       | Ticket 2/3 | Exercise DTO/controller/async/cancellation behavior when endpoints are added. |
| OpenAPI document is minimal in ticket 1.                                  | Ticket 2/3 | Add endpoint coverage and re-verify the contract.                             |
| Local `TrustServerCertificate=True` is scoped to Compose.                 | Ticket 3/4 | Do not propagate the local trust setting to other environments.               |
