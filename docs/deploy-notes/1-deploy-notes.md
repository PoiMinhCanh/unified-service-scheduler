---
title: "1 - Deployment Notes"
tags: [deploy-notes, service-scheduler]
ticket: 1
status: locally-executed
updated: 2026-10-31
---

# Ticket 1 - Deployment notes

## Scope and references

- Ticket: [[1 - Init Project]]
- Change summary: Initial local foundation — API host, shared-domain schema and migration, setup/runtime SQL identities, synthetic seeds, future shifts, OpenAPI/Swagger, health checks, Dockerfile, and Compose stack.
- Implementation revision: working tree based on HEAD `ba36ac889e069b915e87a2b8f6ffbc59feeef1b1` with ticket 1 implementation changes; the implementation was not committed when the local run was recorded.
- Related records: [[database/schema|Schema]], [[database/migration|Migrations]], [[database/cache|Cache]], [[setup-guide|Local setup]], [[test/1-test|Ticket 1 test report]].

> [!NOTE]
> These notes cover the isolated local demo only. There is no production deployment, backup/HA design, Redis, telemetry stack workflow in ticket 1.

## Prerequisites and deployment order

- Target environment: local Docker host with Linux container support, x86-64, at least 4 GiB available for the demo stack. The recorded run used Docker server `25.0.3` and Compose `v2.24.6-desktop.1` on Windows 11.
- Required configuration: an ignored `.env` copied from `.env.example` with replaced passwords for `MSSQL_SA_PASSWORD`, `Scheduler__RuntimePassword`, `ConnectionStrings__Setup`, and `ConnectionStrings__Runtime`.
- Required services: SQL Server 2022 Developer on `linux/amd64`, API runtime/setup image targets, Docker Compose v2.17 or later for `service_completed_successfully`.
- Dependency order: SQL Server healthy -> `db-setup` succeeds -> API starts. The API never runs migrations or seeds.
- Compatibility/downtime: empty local database; no existing data, backfill, or production compatibility. The named volume survives ordinary restarts and `docker compose down`; `docker compose down -v` is destructive.

## Deployment steps

1. Database migrations and seeds: build the stack and run `docker compose up -d --wait`. The one-shot `db-setup` service applies `20261031092016_InitialCreate`, provisions `scheduler_app` with `CONNECT` and `SELECT ON SCHEMA::dbo` only, and adds missing future weekday shifts. Setup has a 120-second deadline and exits nonzero on failure.
2. Application/configuration: the API service binds only `ConnectionStrings__Runtime`; `db-setup` receives only `ConnectionStrings__Setup` and `Scheduler__RuntimePassword`. Images use the pinned SDK/ASP.NET/SQL Server references in `src/ServiceScheduler.Api/Dockerfile` and `compose.yaml`.
3. Cache: Not applicable. Ticket 1 adds no Redis service or cache key; the seeded service-type shape is future ticket 2 input.

### Local commands

```powershell
Copy-Item .env.example .env
# Replace the placeholder passwords in .env.
docker compose config
docker compose up -d --wait
docker compose logs db-setup
docker compose logs api
```

Host-only alternatives when SQL is already running:

```powershell
$env:ConnectionStrings__Setup = "Server=127.0.0.1,1433;Database=ServiceScheduler;User Id=sa;Password=<local-sa-password>;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10"
$env:Scheduler__RuntimePassword = "<local-runtime-password>"
dotnet run --project src/ServiceScheduler.Api/ServiceScheduler.Api.csproj -- --db-setup
```

Use `127.0.0.1`, not `localhost`, for host-to-container SQL connections when `localhost` resolves to IPv6.

## Post-deployment verification

- [x] Confirm setup exit code is `0` and the API is healthy.
- [x] Confirm `/health/live`, `/health/ready`, `/openapi/v1.json`, and `/swagger/index.html` respond.
- [x] Confirm runtime reads use `scheduler_app` and direct appointment writes/DDL are denied.
- [x] Confirm migration history contains `20261031092016_InitialCreate` and expected synthetic rows exist.
- [x] Confirm an ordinary stop/restart preserves the named volume and setup is idempotent.
- [x] Confirm `docker compose down -v` removes the volume and a fresh setup recreates the database.

Expected and observed local results are recorded below. Formal ticket verification results belong in [[test/1-test|the ticket 1 test report]].

## Rollback or forward recovery

- Failure conditions: SQL does not become healthy, setup exits nonzero, or readiness stays `503`.
- Application/configuration recovery: inspect `docker compose logs sqlserver`, `docker compose logs db-setup`, and `docker compose logs api` without exposing passwords; correct configuration and rerun `docker compose up -d --wait`.
- Database recovery: a corrected setup rerun is idempotent. To reset the local instance, drop/recreate the database or use `docker compose down -v`; both remove local synthetic data. A code revert or `dotnet ef database update 0` does not restore deleted data.
- Password recovery: setup does not rotate an existing runtime login password. Reset the login/database explicitly and document the data loss; do not print passwords.
- Cache recovery: Not applicable; no cache exists in ticket 1.

## Execution record

| Environment | Date / time and zone | Revision | Actions and observed results | Evidence / follow-up |
| --- | --- | --- | --- | --- |
| Local Windows 11 host; Docker `25.0.3`; Compose `v2.24.6-desktop.1`; SQL image `2022-CU23-ubuntu-22.04@sha256:0ec7739e...`; SDK `10.0.401` | 2026-10-31 (local session) | Working tree based on `ba36ac8`, implementation uncommitted | `docker compose config` valid; API/setup image targets built; `db-setup` exited `0`; live/ready/OpenAPI/Swagger returned `200`; runtime read `CURRENT_USER=scheduler_app` and 2 dealerships; appointment INSERT/UPDATE/DELETE and CREATE/ALTER/DROP denied; migration history `20261031092016_InitialCreate`; 2 dealerships, 2 customers, 2 vehicles, 3 service types, 4 technicians, 5 qualifications, 4 bays, 40 shifts, 0 appointments; SQL stop produced live `200`/ready `503`; SQL restart produced ready `200`; `down`/`up` preserved the same shift ID and 40 shifts; `down -v` followed by `up` produced a fresh setup exit `0`, 40 shifts, and ready `200`; host `--db-setup` exited `0`; host API live/ready/OpenAPI returned `200` | Formal scenario matrix and execution record executed in [[test/1-test|the test report]] (21/21 cases Passed) |
