---
title: "1 - Phase 4: Containers and Local Initialization"
tags: [plans, service-scheduler, foundation, docker, compose]
ticket: 1
status: implemented
---

# 1 - Phase 4: Containers and local initialization

## Requirements and scope

- Ticket: [[1 - Init Project]]
- Research: [[research/1/00-overview|Foundation research overview]] and [[research/1/01-platform-foundation|Platform foundation research]].
- Requirement IDs: S8; AC-8; T-16 Compose portion; S3 container pins.
- Intended outcome: a reproducible API image and local Compose stack with SQL Server 2022 Developer, a named data volume, an explicit one-shot initialization service, and an API that starts only after successful setup.
- Exclusions: no production hosting, HTTPS termination, backup/HA design, Redis, telemetry, or external integrations; no floating `latest` image tags.
- Assumptions: the host has an x86-64 Linux Docker engine with at least 4 GiB available for the demo stack; the G-14 image digests remain published; the `.env` file is local and ignored; setup exits nonzero on failure.

## Dependencies

- Phase 3 `--db-setup` mode, runtime principal provisioning, and shift seeder.
- Phase 2 migration and static seeds.
- Accepted decisions G-14, G-22, G-23, G-24, G-27, G-42, and G-15 configuration names.
- Documentation guide [[deploy-notes/README|Deployment notes guide]] for the per-ticket operational record.

## Implementation steps

| Step | Requirement IDs | Files / components (planned) | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 4.1 | S3, S8, AC-8 | `src/ServiceScheduler.Api/Dockerfile` | Add a multi-stage Dockerfile using the pinned SDK `10.0.401-noble` and ASP.NET `10.0.13-noble` `tag@sha256:digest` references; use repository-root build context; copy central toolchain files and the API source; publish in Release; create `runtime` and `setup` targets from the same published assembly; run as `$APP_UID`; expose container port 8080; no HTTPS setup. | Both targets build from the root context; the runtime target starts the API and the setup target runs `--db-setup` without starting HTTP. | Implemented |
| 4.2 | S8, AC-8 | `.dockerignore` | Exclude `.git`, `.env*` secrets, `bin/`, `obj/`, `TestResults/`, coverage output, local IDE data, and documentation not required by the build; keep `global.json`, build props, package props, and `src/` available. | The build context excludes ignored secrets and build outputs; the image still restores and publishes successfully. | Implemented |
| 4.3 | S3, S8, AC-8, T-16 | `compose.yaml` | Add the pinned SQL Server 2022 Developer service with `platform: linux/amd64`, EULA acceptance, `MSSQL_PID=Developer`, named volume at `/var/opt/mssql`, and a healthcheck; add a one-shot `db-setup` service with `restart: no`, setup-only environment, and `depends_on: service_healthy`; add the API service with runtime-only environment, `depends_on: service_completed_successfully`, and loopback binding `127.0.0.1:8080:8080`. | `docker compose config` validates; API cannot start before successful setup; SQL and API images use the pinned references; no secret is committed. | Implemented |
| 4.4 | S4, S8, AC-8 | `.env.example`, local `.env` guidance | Finalize placeholders for `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, `MSSQL_PID`, `ConnectionStrings__Setup`, `ConnectionStrings__Runtime`, and `ASPNETCORE_ENVIRONMENT`; document host-to-container (`localhost`) and container-to-container (`sqlserver`) connection forms; keep local `TrustServerCertificate=True` scoped to this demo. | A developer can copy the example to an ignored `.env`, replace placeholders, and start the stack; no real credential is tracked. | Implemented |
| 4.5 | S8, AC-8, T-16; TC-18 Compose portion, TC-19 | Compose operational runbook in `docs/setup-guide.md` | Verify the SQL image tool path used by the healthcheck rather than assuming `/opt/mssql-tools18/bin/sqlcmd`; document `docker compose config`, SQL startup wait, one-shot setup, API startup, health/OpenAPI checks, stop/restart without `-v`, and destructive `down -v` recovery. | The documented sequence succeeds from a clean local environment; restart preserves data while the volume remains; destructive reset is clearly separated. | Implemented |
| 4.6 | S8, S11, AC-8, AC-9 | `docs/deploy-notes/1-deploy-notes.md`, `docs/setup-guide.md` | Create the per-ticket deployment notes with prerequisites, dependency order, migration/identity/seed steps, no-cache-impact statement, smoke checks, recovery/data-loss limits, and an empty execution record; update the setup guide with actual Compose commands and troubleshooting. | Notes use actual paths and commands once verified, keep secrets out, and distinguish planned from executed results. | Implemented |

## Verification

> [!NOTE]
> Implementation was completed on 2026-10-31 and the commands were exercised locally. Formal Passed/Failed/Blocked results are recorded by the test phase in [[test/1-test|the ticket 1 test report]]; the rows below remain the planning baseline.

| Requirement IDs | Scenario / command (planned) | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| S8, AC-8 | `docker compose config` | Configuration is valid; image references include the selected tags/digests; no unresolved required variable. | Not run |
| S3, S8, AC-8 | Build the `runtime` and `setup` targets from the root context | Both targets build successfully; the images use the pinned .NET base images and run as the non-root application user. | Not run |
| S8, AC-8, T-16, TC-18 | Start SQL Server and wait for health; run the one-shot setup; start the API | SQL becomes healthy; setup exits `0`; the API starts only after setup completes. | Not run |
| S9, S8, AC-7, AC-8 | Request `/health/live`, `/health/ready`, `/openapi/v1.json`, and `/swagger/index.html` through the published API port | Live and ready return `200`; OpenAPI and Swagger UI are accessible; no HTTPS endpoint is required. | Not run |
| S8, AC-8, T-16, TC-19 | Stop and restart the stack without removing the named volume | Database, migration history, runtime identity, and seeds persist while the volume remains. | Not run |
| S8, AC-8, T-16 | Remove the named volume with `docker compose down -v` and reinitialize | The reset is destructive as documented; a fresh setup succeeds. | Not run |
| S10, AC-1, T-16 | Run the documented clean-checkout restore/build/format/test/Compose sequence | Commands execute in the documented order; results are recorded with revision and image pins. | Not run |
| S11, AC-9 | Review setup guide and deployment notes against Compose | Commands, variable names, ports, volume behavior, and recovery limits match the implementation. | Not run |

## Impacts and remaining decisions

- Database / migrations: Compose applies the Phase 2 migration through the Phase 3 setup mode; no additional schema change.
- Cache: none; no Redis service is added to Compose.
- API / configuration / deployment: introduces containerized API and setup modes, loopback HTTP, named-volume persistence, and local certificate trust scoped to the demo.
- Operational risk: SQL Server memory requirements and image startup are environment-dependent; if Docker or image access is unavailable, record the affected checks as Blocked per G-42 rather than substituting simulated evidence.
- Open decisions for implementation: verified SQL healthcheck tool path, final `.dockerignore` contents, and whether the Compose setup service needs an explicit `command` override in addition to the image `CMD`.
- Handoff: Phase 5 records the documentation status and the test phase executes the full operational matrix, including the named-volume restart and clean-checkout run.
