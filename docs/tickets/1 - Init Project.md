---
title: "1 - Init Project: Project Foundation and Testing"
tags: [ticket, service-scheduler, backend, dotnet, testing]
ticket: 1
status: specified
created: 2026-10-28
---

# 1 - Init Project: Project Foundation and Testing

## Objective and baseline

Create an executable, testable foundation for the Unified Service Scheduler.

Read [README](../../README.md) and [[architecture]]. README defines project requirements. Architecture defines AS-01 through AS-11 assumptions, BR-01 through BR-09 business rules, and API contracts. This ticket owns foundation work only; its completion does not mean the booking workflow or full release is complete.

> [!IMPORTANT]
> All work is specified, not implemented or verified. Establish automated tests alongside the foundation implementation.

## Scope

- Scaffold a standalone .NET 10 / ASP.NET Core API with controllers, DTO boundaries, built-in DI, asynchronous I/O, cancellation support, and consistent Problem Details responses.
- Use one API project with feature folders and separate xUnit unit/integration projects. Avoid adding generic repositories, mediator libraries, or runtime AI dependencies.
- Pin the installed stable SDK in `global.json`, compatible EF Core 10/NuGet packages, and a SQL Server 2022 Developer container CU tag/digest. Do not use floating `latest` tags.
- Configure environment-based settings and startup validation; supply `.env.example` with placeholders/synthetic defaults. Add local linting/format checks and build/test commands.
- Establish EF Core mappings, initial migrations, keys, checks, relationships, and indexes for the shared domain: dealerships/opening intervals, customers, vehicles, service types, technicians, qualifications, shifts, bays, and appointments. Use UUIDs and UTC `datetimeoffset(7)` values.
- Create separate privileged migration/seed and restricted runtime identities. The runtime identity reads application data and cannot directly insert, update, or delete appointments or alter schema.
- Add reproducible synthetic seeds, including qualified technicians, active bays, and future shifts usable by tests and demo scripts. Catalogs and resource administration remain read-only seeded configuration.
- Provide an API Dockerfile and SQL Server Compose service with a named data volume and explicit initialization step. Document x86-64 Linux containers, adequate memory, EULA acceptance, Developer edition, local secrets, and scoped certificate settings.
- Expose OpenAPI and Swagger UI for interactive API requests. Add `GET /health/live` for process liveness and `GET /health/ready` for database readiness.
- Configure xUnit, WebApplicationFactory, real SQL Server Testcontainers, controllable time, and reusable isolated fixtures. Establish migrations/seed, configuration, startup, health, and restricted-identity smoke checks.
- Write the initial system design and README build/run/test/setup instructions. Record design decisions, review, and verification evidence.

## Scope boundaries

This scope covers API/test scaffolding, schema, seeds, configuration, health checks, and local tooling. Dealership/catalog/availability endpoints, Redis, booking APIs, the transactional booking procedure and its EXECUTE grant, booking concurrency verification, Serilog, the telemetry stack are outside this ticket's scope. Use framework logging; keep the application runnable without telemetry.

## Acceptance criteria

- [ ] A clean checkout can restore, build, and run the scaffold using documented commands.
- [ ] Unit and integration test projects execute successfully from the beginning.
- [ ] Missing/invalid required configuration fails startup with a clear error; local lint/format checks run.
- [ ] An empty SQL Server instance can be initialized with migrations, runtime identity, and synthetic seeds; the API uses the restricted identity.
- [ ] Database constraints and relationships match the shared model; tests prove runtime reads work and direct appointment writes/schema changes are denied.
- [ ] Liveness stays independent of database availability; readiness fails when SQL Server is unavailable.
- [ ] OpenAPI/Swagger UI is accessible, and health/startup smoke tests pass against real SQL Server.
- [ ] Local Docker startup, named-volume behavior, and initialization are documented and checked for this scope.
- [ ] Initial architecture, README, and setup guide distinguish implemented behavior from planned design.

## Verification and documentation

| Check | Level | Expected result | Requirements |
| --- | --- | --- | --- |
| T-14 Database readiness | Integration/smoke | With SQL Server unavailable, liveness remains `200` and readiness returns `503`; a ready initialized database restores readiness. | P-04 |
| T-16 Foundation clean checkout | Local smoke | Documented restore, build, format, test, migration, seed, and Compose commands work from a clean checkout. Verify runtime reads, denied appointment writes/schema changes, OpenAPI, and named-volume persistence. | D-02, D-03, D-04, P-04 |


Owns the foundation portions of D-01 through D-04, P-04, T-14 readiness, and T-16 local reproducibility. Do not claim booking persistence, caching, telemetry have passed.

Record scenarios, exact commands, and observed results in `docs/test/1-test.md` using [[test/README|Testing guidance]]. Follow [[database/maintenance|Database maintenance]] for schema changes and update [[setup-guide]], [[architecture]], and database documentation with actual implementation paths.
