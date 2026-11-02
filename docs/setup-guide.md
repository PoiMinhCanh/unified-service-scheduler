---
title: "Unified Service Scheduler - Local Setup Guide"
tags: [setup, development, service-scheduler]
updated: 2026-11-02
---

# Local setup guide

This guide covers prerequisites, configuration, database initialization, running the API and telemetry stack, testing, and local recovery. See [project scope](../README.md) and [[architecture]] for application behavior.

## Prerequisites

| Tool or dependency         | Project requirement                                                | Setup guidance                                                                                                |
| -------------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| Git                        | Source checkout and change review                                  | Use the actual repository URL                                                                                 |
| .NET SDK                   | SDK `10.0.403`, pinned in `global.json` with roll-forward disabled | Install the pinned SDK locally, or run CLI commands through the pinned Docker SDK image                       |
| Editor                     | C# and ASP.NET Core development support                            | Use an editor compatible with the chosen SDK; a particular IDE is not required                                |
| Docker and Compose         | Linux containers and Compose v2                                    | Use an x86-64 Docker host with memory available for SQL Server, Redis, the API, and the telemetry stack                    |
| SQL Server                 | SQL Server 2022 Developer for local development and tests          | Use the pinned Linux image through Compose and Testcontainers; Developer edition is for development/testing   |
| PowerShell | Optional local command examples | PowerShell 5.1+ for the command examples. |
| Redis                      | Optional service-catalog cache              | Use the pinned Linux image through Compose/Testcontainers; Redis is not a booking lock or an API startup gate |
| Telemetry services         | Collector, Prometheus, Tempo, and Grafana stack | Start through Compose; all ports bind to loopback and telemetry is not a booking/readiness dependency. |
| Claude Code CLI and Serena | Optional AI-assisted development workflow                          | Use the developer's configured account and MCP connection; these tools are not runtime API dependencies       |

The initial backend has no separate frontend build.

## Check the local tools

Run these read-only checks on the development machine:

```powershell
git --version
docker version
docker compose version
dotnet --list-sdks
```

Host commands require SDK `10.0.403`, as specified in `global.json`. Docker must use an x86-64 Linux container engine; Compose v2 starts the dependencies.

If the host SDK is not installed, use the pinned SDK image with the checkout mounted. From the repository root:

```powershell
$sdkImage = "mcr.microsoft.com/dotnet/sdk:10.0.403-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b"
docker run --rm --mount "type=bind,source=$($PWD.Path),target=/workspace" --workdir /workspace $sdkImage dotnet --version
```

Replace `dotnet --version` with the relevant restore, build, format, or test command. Containerized tests that use Testcontainers additionally need access to the Docker daemon and reachable dependency ports.

## Repository layout

```text
.
  global.json
  Directory.Build.props
  Directory.Packages.props
  .editorconfig
  .config/dotnet-tools.json
  ServiceScheduler.slnx
  compose.yaml
  .env.example
  src/ServiceScheduler.Api/
    Program.cs
    appsettings.json
    appsettings.Development.json
    Dockerfile
    Features/
      Appointments/
      Availability/
      Dealerships/
      ServiceCatalog/
    Infrastructure/
      Caching/
      Configuration/
      Errors/
      Health/
      OpenApi/
      Persistence/
        Entities/
        Configurations/
        SchedulerDbContext.cs
        SchedulerDbContextFactory.cs
        IAppointmentBookingProcedure.cs
        SqlAppointmentBookingProcedure.cs
      Setup/
      Telemetry/
    Migrations/
  tests/ServiceScheduler.UnitTests/
    Availability/
    Booking/
    Caching/
    Configuration/
    Errors/
    Telemetry/
  tests/ServiceScheduler.IntegrationTests/
    Availability/
    Booking/
    Catalog/
    Fixtures/
    Health/
    OpenApi/
    Persistence/
    Resilience/
    Security/
    Startup/
    Telemetry/
  telemetry/
  docs/
```

Telemetry configuration lives in `telemetry/`. Both test projects include a `Telemetry/` folder, and the API telemetry infrastructure is in `Infrastructure/Telemetry/`.

## Local configuration

The API uses these configuration names:

| Key                                                        | Consumer           | Purpose                                                                                                                        |
| ---------------------------------------------------------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------------ |
| `ConnectionStrings:Runtime` / `ConnectionStrings__Runtime` | API and test host  | Restricted `scheduler_app` connection used for readiness, business reads, and booking procedure execution                      |
| `ConnectionStrings:Setup` / `ConnectionStrings__Setup`     | `--db-setup` only  | Privileged `sa` connection used for migrations, principal provisioning, and shifts                                             |
| `Scheduler:RuntimePassword` / `Scheduler__RuntimePassword` | `--db-setup` only  | Password used when creating the `scheduler_app` login                                                                          |
| `ConnectionStrings:Cache` / `ConnectionStrings__Cache`     | API and test host  | Redis endpoint. Required and parseable, but reachability is not a startup gate.                                                |
| `Scheduler:Cache:ServiceCatalog:AbsoluteTtl`               | API                | Catalog absolute TTL; default `00:10:00`.                                                                                      |
| `Scheduler:Cache:OperationTimeout`                         | API                | Caller budget per cache get/set/remove; default `00:00:00.250`.                                                                |
| `Scheduler:Cache:ConnectTimeout`                           | API                | Redis connection timeout; default `00:00:00.500`.                                                                              |
| `Scheduler:Cache:CommandTimeout`                           | API                | Redis command timeout; default `00:00:00.500`.                                                                                 |
| `Scheduler:Cache:ConnectRetry`                             | API                | Initial connection attempts; default `1`.                                                                                      |
| `Scheduler:Booking:MaxCandidateAttempts`                   | API                | Maximum distinct candidate pairs per booking request; default `5`, valid range 1-20.                                           |
| `Scheduler:Booking:MaxDeadlockRetries`                     | API                | Global whole-attempt retries after a `1205` deadlock victim; default `2`, valid range 0-5.                                     |
| `Scheduler:Booking:RetryDelayMilliseconds`                 | API                | Per-deadlock-retry backoff; default `50`, valid range 0-1000.                                                                  |
| `Scheduler:Booking:RequestBudgetSeconds`                   | API                | Total booking request budget across reads and procedure attempts; default `20`, valid range 1-120.                             |
| `Scheduler:Booking:CommandTimeoutSeconds`                  | API                | `SqlCommand.CommandTimeout` for each procedure call; default `10`, valid range 1-60, and must be less than the request budget. |
| `Scheduler:Telemetry:Enabled` / `Scheduler__Telemetry__Enabled` | API | Creates OTel providers/exporters only when `true`; default `false`; Compose sets `true`. |
| `Scheduler:Telemetry:OtlpEndpoint` / `Scheduler__Telemetry__OtlpEndpoint` | API | OTLP/gRPC endpoint; default `http://127.0.0.1:4317`; Compose `http://collector:4317`. |
| `Scheduler:Telemetry:SamplingRatio` / `Scheduler__Telemetry__SamplingRatio` | API | Parent-based trace ratio `0`-`1`; Compose/`1.0`. |
| `Scheduler:Telemetry:ServiceName` / `ServiceInstanceId`    | API                | Resource identity and Prometheus `job`/`instance`; defaults `unified-service-scheduler-api`/`scheduler-api`. |
| `Scheduler:Telemetry:TraceBatch*`, `OtlpTimeoutMilliseconds`, `MetricExport*` | API | Bounded export values; see `appsettings.json`. |
| `MSSQL_SA_PASSWORD`                                        | Compose SQL Server | Local administrator password for the SQL container                                                                             |
| `MSSQL_PID`                                                | Compose SQL Server | `Developer` for local development/testing                                                                                      |
| `ASPNETCORE_ENVIRONMENT`                                   | API                | Environment name; `Development` enables OpenAPI and Swagger UI                                                                 |

`.env.example` contains placeholders. Copy it to the ignored `.env` file and replace both placeholder passwords in all four places (the two variables and the `Password=` segment of the two connection strings) before starting Compose:

```powershell
Copy-Item .env.example .env
```

The setup and runtime connection strings in `.env.example` use the Compose service name `sqlserver`. The cache value uses the Compose service name `redis`; for commands run from the host, use `ConnectionStrings__Cache=127.0.0.1:6379` instead. On Windows, `localhost` can resolve to IPv6 while the Compose SQL/Redis ports are published on IPv4 loopback.

`.env` is read by Compose interpolation, not by `dotnet run`. Export environment variables for direct host commands. Keep real credentials in ignored local sources and use synthetic data.

Local SQL connections use `Encrypt=True;TrustServerCertificate=True`. That trust setting is scoped to this local demo; do not propagate it to another environment. The `Scheduler:Booking:*` defaults live in `appsettings.json`; invalid or out-of-range values fail startup. The procedure's SQL-side `LOCK_TIMEOUT` is 5 seconds, below the default 10-second command timeout.

## Initialization and startup sequence

### Compose stack

1. Validate the Compose file and image references:

    ```powershell
    docker compose config
    ```

2. Start the stack and wait for health:

    ```powershell
    docker compose up -d --build --wait
    ```

    Compose starts SQL Server and Redis, waits for SQL Server health, runs the one-shot `db-setup` service, and starts the API only after setup exits successfully. Setup applies EF migrations, validates the IANA-to-Windows mapping and booking procedure, provisions `scheduler_app` with `CONNECT`, schema `SELECT`, procedure `EXECUTE`, and appointment DML denies, and seeds future weekday shifts. The API does not depend on Redis health; Redis starts whether or not the API can reach it.

3. Check the API and documentation endpoints:

    ```powershell
    Invoke-WebRequest http://127.0.0.1:8080/health/live
    Invoke-WebRequest http://127.0.0.1:8080/health/ready
    Invoke-WebRequest http://127.0.0.1:8080/openapi/v1.json
    Start-Process http://127.0.0.1:8080/swagger/index.html
    ```

    The API listens on container port `8080`, published to `127.0.0.1:8080`. There is no HTTPS endpoint. Readiness returns JSON with `database` and `redis` statuses; Redis-only failure is `Degraded` with HTTP `200`, and SQL failure is `Unhealthy` with `503`.

4. Check telemetry access and provisioning:

    ```powershell
    Invoke-WebRequest http://127.0.0.1:13133/                 # Collector
    Invoke-WebRequest http://127.0.0.1:9090/-/ready            # Prometheus
    Invoke-WebRequest http://127.0.0.1:3200/ready              # Tempo
    Invoke-WebRequest http://127.0.0.1:3000/api/health         # Grafana
    Invoke-RestMethod http://127.0.0.1:3000/api/datasources/uid/prometheus
    Invoke-RestMethod http://127.0.0.1:3000/api/datasources/uid/tempo
    Invoke-RestMethod http://127.0.0.1:3000/api/dashboards/uid/scheduler-overview
    Start-Process http://127.0.0.1:3000/d/scheduler-overview
    ```

    Telemetry services start independently of the API. The provisioned dashboard is `scheduler-overview`; data-source UIDs are `prometheus` and `tempo`. Copy `TraceId` from JSON logs or Problem Details `traceId` into Grafana Explore -> Tempo. Stopping telemetry services leaves the API usable and restarting them resumes new exports.

5. Verify the cache and API reads:

    ```powershell
    docker compose exec redis redis-cli ping
    Invoke-RestMethod http://127.0.0.1:8080/api/dealerships
    Invoke-RestMethod "http://127.0.0.1:8080/api/dealerships/<dealershipId>/service-types"
    ```

    Use Swagger UI to manually check availability with a future weekday start within the 28-day shift horizon, equivalent Z and explicit-offset requests, booking/retrieval, and `400`/`404`/`409` Problem Details responses.

6. Inspect the setup service and API logs:

    ```powershell
    docker compose logs db-setup
    docker compose logs api
    ```

    The setup command has a 120-second deadline and exits nonzero on failure without an unbounded retry loop.

### Direct host commands

Use these when the SQL container is already running and you want to run the API or setup mode outside Compose:

```powershell
$env:ConnectionStrings__Setup = "Server=127.0.0.1,1433;Database=ServiceScheduler;User Id=sa;Password=<local-sa-password>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
$env:Scheduler__RuntimePassword = "<local-runtime-password>"
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -- --db-setup

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__Runtime = "Server=127.0.0.1,1433;Database=ServiceScheduler;User Id=scheduler_app;Password=<local-runtime-password>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=3"
$env:ConnectionStrings__Cache = "127.0.0.1:6379"
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
```

The API mode never binds `ConnectionStrings:Setup` or the runtime password. A syntactically valid but unreachable runtime SQL server does not prevent startup; it makes `/health/ready` return `503` while `/health/live` remains `200`. A valid but unreachable Redis endpoint also does not prevent startup; it makes the `redis` readiness check `Degraded` while the endpoint remains `200`.

## Build, format, and test commands

Run from the repository root with the pinned SDK:

```powershell
dotnet restore
dotnet build --no-restore
dotnet format --verify-no-changes
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
```

The integration project uses real SQL Server, Redis, Collector, Prometheus, Tempo, and Grafana through Testcontainers with the pinned images. It starts one SQL container per SQL collection, one Redis container per combined collection, one pinned telemetry stack per telemetry test class (serialized by a stack gate), initializes one uniquely named database per test class through the same setup component, uses a distinct Redis `defaultDatabase` index per cache test class, and disables parallel execution for the SQL/Redis collection. The unit suite disables parallelization because `ActivityListener`/`MeterListener` observe process-wide sources. Docker must be running and the pinned images must be available.

Schema work uses the privileged setup identity. Runtime reads and denial checks use independent `scheduler_app` connections. The test fixtures generate synthetic passwords in memory and do not write them to reports.

## Database migration and seed commands

The initialization path is `--db-setup`, which calls EF Core `MigrateAsync`:

```powershell
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -- --db-setup
```

To generate and inspect a migration during development:

```powershell
dotnet tool restore
dotnet ef migrations list --no-connect --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
dotnet ef migrations script --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj --startup-project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -o initial.sql
```

`--no-connect` lists the migrations defined in the project without connecting, so the command works without exporting a setup connection. Omit the flag and export `ConnectionStrings__Setup` when you need applied-migration status from the local database; `dotnet ef` does not load `.env`.

The base migration is `20261031092016_InitialCreate`; the second migration is `20261101161530_AddBookingProcedureAndTimeZoneMappings` with `dbo.TimeZoneMappings`, the dealership mapping FK, and `dbo.CreateAppointment`. See [[database/migration|Migrations]] for the records and recovery limits.

## Stop, restart, and recovery

| Action                                             | Command                                                                                                                              | Data effect                                                                                                                                             |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Stop services                                      | `docker compose stop`                                                                                                                | Keeps containers and the named volume                                                                                                                   |
| Restart services                                   | `docker compose start`                                                                                                               | Reuses the named `scheduler-sql-data` volume                                                                                                            |
| Remove containers, keep data                       | `docker compose down`                                                                                                                | Keeps the named volume; a later `up` reuses the database                                                                                                |
| Destructive reset                                  | `docker compose down -v`                                                                                                             | Removes the named volume and all local synthetic data                                                                                                   |
| Database-only reset                                | Drop/recreate the local database, then rerun `--db-setup`                                                                            | Removes only that database's data                                                                                                                       |
| Redis-only outage/recovery                         | `docker compose stop redis` / `docker compose start redis`                                                                           | Keeps SQL data; catalog falls back to SQL and readiness becomes `Degraded`/`200`                                                                        |
| Catalog invalidation                               | `docker compose exec redis redis-cli DEL "scheduler:v1:dealership:<lowercase-guid>:service-types"`                                   | Removes one cached catalog; the next request reads SQL and repopulates                                                                                  |
| Runtime permission recovery after a down migration | `REVOKE INSERT, UPDATE, DELETE ON OBJECT::dbo.Appointments FROM [scheduler_app];`; rerun `--db-setup` or recreate the local database | Removes surviving appointment DML denies; the procedure `EXECUTE` grant disappears with the dropped procedure. Do not add direct write grants. |

A normal setup rerun is idempotent: EF migration history prevents schema reapplication, time-zone/procedure validation reads the current state, principal creation and the exact grants/denies tolerate existing state, and the shift seeder adds only missing intervals without deleting historical shifts. Setup does not rotate an existing runtime login password; if the password changes, reset the login/database explicitly and document the data loss.

A code revert or down migration does not restore deleted data. This setup targets an isolated local demo with synthetic data only.

## API and verification workflow

The API exposes `GET /api/dealerships`, `GET /api/dealerships/{dealershipId}/service-types`, `GET /api/dealerships/{dealershipId}/availability?serviceTypeId=...&startsAt=...`, `POST /api/dealerships/{dealershipId}/appointments`, `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}`, `/health/live`, `/health/ready`, `/openapi/v1.json`, and `/swagger/index.html`.

Catalog reads use the bounded Redis cache-aside path and fall back to SQL Server; availability and booking validation use authoritative SQL Server data and never read Redis. Availability is advisory and creates no reservation. Booking calls `dbo.CreateAppointment` through the restricted runtime identity and uses one short `SERIALIZABLE` transaction with technician-then-bay locks. Use the runtime identity for reads and procedure execution, and the setup identity only for schema/fixture work. Direct appointment writes and DDL must remain denied after application changes. Swagger success alone does not prove booking or concurrency correctness; use the real SQL Server tests.

Use Swagger UI at `/swagger/index.html` for manual API checks, including availability, equivalent time-zone offsets, booking/retrieval, and `400`/`404`/`409` Problem Details responses.

Run the integration suite for SQL locking, permissions, and concurrency coverage.

## Troubleshooting

| Symptom                                                                | What to inspect                                                                                                                                                                                                                                                                |
| ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Startup fails before serving                                           | The error names the missing or malformed `ConnectionStrings:Runtime` or `ConnectionStrings:Cache` key; fix configuration rather than weakening validation                                                                                                                      |
| `docker compose config` fails on a variable                            | Ensure `.env` exists and sets `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, `ConnectionStrings__Setup`, `ConnectionStrings__Runtime`, and `ConnectionStrings__Cache`                                                                                                      |
| SQL container does not become healthy                                  | Check Docker memory, the image pin, EULA/Developer settings, and `docker compose logs sqlserver` without exposing passwords                                                                                                                                                    |
| SQL log shows `Login failed for user 'sa'` after reusing an old volume | The named volume keeps the original SA password; use the original local password, reset the isolated volume with `docker compose down -v` after confirmation, or start an isolated Compose project/volume                                                                      |
| Login fails after editing `.env`                                      | A placeholder can remain duplicated in a connection string. Replace both placeholder values in all four places: the two variables and the `Password=` segment of each matching connection string                                                                                |
| Setup exits nonzero                                                    | Inspect `docker compose logs db-setup`; common causes are password policy failures, an unreachable SQL server, or an unexpected privileged state on `scheduler_app`                                                                                                            |
| Readiness returns `503`                                                | Check the JSON body: `database: Unhealthy` means SQL Server is unavailable or uninitialized; liveness should stay `200` because startup does not connect to SQL                                                                                                                |
| Readiness shows `redis: Degraded` but returns `200`                    | Redis is unreachable or exceeds its budget. SQL-backed requests still work; start Redis and verify recovery with `docker compose start redis`                                                                                                                                  |
| Catalog is slow or stale during a Redis outage                         | Recoverable Redis failures fall back to SQL within the bounded budget; a stale hit expires after the 10-minute TTL or can be invalidated with `redis-cli DEL`                                                                                                                  |
| Catalog returns a cached value after a seed change                     | Delete the affected `scheduler:v1:dealership:<guid>:service-types` key or bump the key version; the TTL bounds staleness if invalidation is missed                                                                                                                             |
| Booking returns `409 capacity_unavailable`                             | This is a valid request with no eligible pair under the bounded policy. Check availability, opening/shift coverage, and existing confirmed appointments; the limit deliberately does not guarantee exhaustive capacity discovery. Do not convert it into a dependency failure. |
| Booking returns `503 dependency_unavailable`                           | Inspect `docker compose logs api` for lock/command timeouts, deadlocks, or classified SQL failures. The API does not blindly replay a write with an unknown commit outcome; recover the dependency and retry as a new request.                                                 |
| Setup fails mapping/procedure validation                               | Inspect `docker compose logs db-setup`; apply the booking migration and verify `dbo.CreateAppointment` and the `TimeZoneMappings` rows. Do not bypass the validator or grant schema `EXECUTE`.                                                                                |
| Runtime cannot execute the booking procedure                           | Rerun `--db-setup` to apply the exact object `EXECUTE` grant. Do not grant direct appointment writes or schema `EXECUTE` to bypass the issue.                                                                                                                                  |
| Runtime query denied                                                   | Use the setup identity for migrations and permission analysis; do not grant direct appointment writes to bypass a failure                                                                                                                                                      |
| Host connection times out while Compose is healthy                     | Use `127.0.0.1` rather than `localhost` for host-to-container SQL and Redis connections                                                                                                                                                                                        |
| No metrics or traces                                                   | Confirm `Scheduler__Telemetry__Enabled=true`, the OTLP endpoint, Collector logs/queues, Prometheus `--web.enable-otlp-receiver`, Tempo readiness, sampling ratio, and the `traceparent` sampled flag. |

## Optional AI development workflow

See [[ai-development-guide|AI development setup and usage]] for Claude Code, SQL Server MCP Toolbox, Serena, and repository workflow configuration. These tools are optional and are not required to run the application.
