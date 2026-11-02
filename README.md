# Unified Service Scheduler

## Project Overview

Unified Service Scheduler is an automotive service-booking backend for dealership service advisors. It confirms an appointment only when a qualified technician and a service bay are available for the full service duration, with SQL Server protecting both reservations under concurrent requests.

The initial release covers:

- Dealership and service discovery.
- Availability checks based on service duration, opening hours, technician qualifications, shifts, and existing reservations.
- Appointment creation and retrieval with persistent storage.
- Protection against overlapping technician or bay reservations under concurrent requests.
- Service-catalog caching, structured logs, metrics, and tracing.

OpenAPI and Swagger UI provide the API client interface.

See [architecture](docs/architecture.md) for the domain model, business rules, and API contract, and [setup](docs/setup-guide.md) for the development environment. The [documentation index](docs/README.md) provides the full document list.

## Explore the application

1. [Start the Compose stack](#run-the-local-compose-stack) with synthetic seeds and separate setup/runtime database identities.
2. Open [Swagger UI](http://127.0.0.1:8080/swagger/index.html), discover services, check advisory availability, create a booking, and follow its `Location` to retrieve it.
3. Open the [Grafana dashboard](http://127.0.0.1:3000/d/scheduler-overview), generate API traffic, and correlate a JSON log or Problem Details `traceId` with Tempo.
4. Read the [booking transaction and concurrency design](docs/architecture.md#appointment-confirmation) to understand resource allocation and conflict handling.

| Repository path | Purpose |
| --- | --- |
| `src/ServiceScheduler.Api/Features/` | Dealership, catalog, availability, and appointment controllers, contracts, and use cases. |
| `src/ServiceScheduler.Api/Infrastructure/` | SQL persistence, setup, bounded cache, error handling, health, and telemetry. |
| `src/ServiceScheduler.Api/Migrations/` | Schema, seeds, time-zone mappings, and the transactional booking procedure. |
| `tests/` | Unit tests and integration tests against real containerized dependencies. |
| `telemetry/` | Collector, Prometheus, Tempo, and provisioned Grafana configuration. |
| `docs/` | Architecture, setup, database references, tickets, reviews, and verification evidence. |

## Scope and constraints

A service advisor books on behalf of a customer. The API chooses one qualified technician and one active service bay at the requested dealership for the complete service interval. Catalogs, customer/vehicle data, resource qualifications, and shifts are seeded configuration in the initial release.

The API uses synthetic data without authentication. Dealership scope and vehicle ownership checks enforce data integrity. A separate frontend, payments/invoicing, notifications, external dealership integrations, administrative CRUD, multi-region hosting, Kubernetes, microservices, event sourcing, and a message broker are outside the initial release. Production hosting and operations require separate design.

Cancellation, rescheduling, authentication, idempotent POSTs, vehicle overlap protection, centralized log search, and cache stampede protection require separately scoped contracts and tests. A repeated POST is a new booking attempt.

[Architecture](docs/architecture.md) describes the scheduling assumptions, business rules, and API semantics.

## Technology

The project uses the following technology stack.

| Area              | Technology                                                                  |
| ----------------- | --------------------------------------------------------------------------- |
| Backend           | C#, .NET 10, ASP.NET Core Web API                                           |
| Persistence       | Entity Framework Core 10, SQL Server 2022 Developer                         |
| Caching           | Redis                                                                       |
| API documentation | OpenAPI, Swagger UI                                                         |
| Logging           | Serilog                                                                     |
| Instrumentation   | OpenTelemetry SDK and Collector                                             |
| Metrics           | Prometheus                                                                  |
| Tracing           | Grafana Tempo                                                               |
| Dashboards        | Grafana                                                                     |
| Testing           | xUnit, WebApplicationFactory, Testcontainers with real SQL Server and Redis |
| Local execution   | Docker, Docker Compose                                                      |

## API and design

| Operation | Endpoint | Purpose |
| --- | --- | --- |
| Discover dealerships | `GET /api/dealerships` | List dealerships and their time zones. |
| Browse services | `GET /api/dealerships/{dealershipId}/service-types` | Read the dealership catalog through Redis cache-aside with SQL fallback. |
| Check availability | `GET /api/dealerships/{dealershipId}/availability?serviceTypeId=...&startsAt=...` | Check a future service interval without reserving resources. |
| Create an appointment | `POST /api/dealerships/{dealershipId}/appointments` | Reserve a qualified technician and active bay atomically. |
| Retrieve an appointment | `GET /api/dealerships/{dealershipId}/appointments/{appointmentId}` | Read the persisted booking within its dealership. |

SQL Server is authoritative for availability and booking. The `dbo.CreateAppointment` procedure locks the technician and then the bay, rechecks eligibility and overlaps, and commits both reservations in one transaction. The runtime SQL identity can read data and execute the procedure but cannot directly insert, update, or delete appointments.

Redis caches service catalogs with bounded timeouts and expiry. Cache failures fall back to SQL; booking and availability do not depend on cached data. Serilog writes structured JSON logs, while OpenTelemetry exports metrics and traces through the Collector to Prometheus and Tempo. Grafana provides a provisioned dashboard. Telemetry is enabled by Compose and disabled by default for host execution.

`/health/live` reports process liveness. `/health/ready` checks SQL Server and Redis: SQL failure returns `503`, while Redis-only degradation returns `200`. Redis and telemetry health do not gate API startup.

## How to set up

### Prerequisites

- Git.
- .NET SDK `10.0.403` for host build/test commands; Compose builds through the pinned Docker SDK image.
- Docker with Linux container support and Docker Compose v2 on an x86-64 host, with memory available for SQL Server and the full telemetry stack.
- PowerShell for the optional local command examples.

Claude Code CLI and Serena are optional development tools. See [CLAUDE.md](CLAUDE.md) for repository instructions.

### Prepare the repository

1. Clone this repository using its actual Git URL and open the repository root.
2. Follow the [local setup guide](docs/setup-guide.md).
3. Check the installed tools:

   ```powershell
   git --version
   docker version
   docker compose version
   dotnet --list-sdks
   ```

`dotnet --list-sdks` must show `10.0.403` for host execution.

### Restore, build, format, and test

Run these commands from the repository root:

```powershell
dotnet restore
dotnet build --no-restore
dotnet format --verify-no-changes
dotnet test tests/ServiceScheduler.UnitTests/ServiceScheduler.UnitTests.csproj --no-build
dotnet test tests/ServiceScheduler.IntegrationTests/ServiceScheduler.IntegrationTests.csproj --no-build
```

The integration suite starts the pinned SQL Server 2022, Redis, Collector, Prometheus, Tempo, and Grafana containers through Testcontainers, so Docker must be running and the images must be pullable. It uses one container per collection, one uniquely named database per test class, and a distinct Redis database index per cache test class. Coverage includes scheduling rules, API contracts, persistence, concurrent booking, runtime permissions, cache fallback, telemetry delivery, and dashboard provisioning.

C# imports must be used and sorted alphabetically, with `System` namespaces first and no blank groups. Use the editor's Organize Usings action to remove unused imports and sort them. Unused imports (`IDE0005`) fail the build; `.editorconfig` defines the sorting preferences.

### Run the local Compose stack

1. Copy the environment example and replace both placeholder passwords in all four places (the two variables and the `Password=` segment of the two connection strings):

   ```powershell
   Copy-Item .env.example .env
   ```

2. Validate and start the stack:

   ```powershell
   docker compose config
   docker compose up -d --build --wait
   ```

   Compose waits for SQL Server health, runs the one-shot `db-setup` service (migrations, time-zone/procedure validation, runtime identity/grants, future shifts), and starts the API only after setup exits successfully. Redis starts alongside SQL Server but is not an API startup gate; Redis-only failure keeps readiness at `200` with a `Degraded` cache check.

3. Check the API:

   ```powershell
   Invoke-WebRequest http://127.0.0.1:8080/health/live
   Invoke-WebRequest http://127.0.0.1:8080/health/ready
   Invoke-RestMethod http://127.0.0.1:8080/health/ready | ConvertTo-Json -Depth 5
   Invoke-WebRequest http://127.0.0.1:8080/openapi/v1.json
   Start-Process http://127.0.0.1:8080/swagger/index.html
   ```

   Readiness returns JSON with `database` and `redis` check statuses. A first readiness call immediately after an API restart can be `Unhealthy` while the SQL check warms, then returns `Healthy`.

4. Inspect telemetry. Compose starts the pinned Collector, Prometheus, Tempo, and Grafana alongside the API; the API does not wait for them:

   ```powershell
   Invoke-WebRequest http://127.0.0.1:13133/                 # Collector health
   Invoke-WebRequest http://127.0.0.1:9090/-/ready            # Prometheus
   Invoke-WebRequest http://127.0.0.1:3200/ready              # Tempo
   Invoke-WebRequest http://127.0.0.1:3000/api/health         # Grafana
   Start-Process http://127.0.0.1:3000/d/scheduler-overview   # provisioned dashboard
   ```

   Copy the `TraceId` from the API JSON logs or Problem Details `traceId` and query it in Grafana Explore -> Tempo. Provisioned data sources use UIDs `prometheus` and `tempo`; the dashboard UID is `scheduler-overview`. All telemetry ports bind to loopback only. `docker compose stop collector prometheus tempo grafana` leaves the API usable, and restarting them resumes new exports.

5. Stop the stack. `docker compose down` keeps the named SQL volume; `docker compose down -v` is destructive and removes all local database data:

   ```powershell
   docker compose down
   ```

### Run setup or the API directly on the host

`dotnet run` does not load `.env`. Export the required values in the shell. Use `127.0.0.1` for host-to-container SQL connections; `localhost` may resolve to IPv6 while the Compose port is published on IPv4 loopback.

```powershell
$env:ConnectionStrings__Setup = "Server=127.0.0.1,1433;Database=ServiceScheduler;User Id=sa;Password=<local-sa-password>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
$env:Scheduler__RuntimePassword = "<local-runtime-password>"
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -- --db-setup

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__Runtime = "Server=127.0.0.1,1433;Database=ServiceScheduler;User Id=scheduler_app;Password=<local-runtime-password>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=3"
$env:ConnectionStrings__Cache = "127.0.0.1:6379"
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj
```

The API mode never binds the setup connection. Keep real credentials in the ignored `.env` file or local shell environment and use synthetic data for development and tests. For host commands, use `127.0.0.1` for Redis because Compose publishes it on IPv4 loopback; the Compose API uses `redis:6379`.

## AI development workflow

See the [AI development setup and usage guide](docs/ai-development-guide.md) for SQL Server MCP Toolbox v1.8.0 and Serena MCP setup, creating tickets in `docs/tickets/<id> - <title>.md`, and running research, research verification, planning, plan verification, implementation, review, and manual checklist generation. The guide links the existing workflow rules, templates, and ticket evidence.
