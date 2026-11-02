---
title: "2 - Service Discovery and Availability: Deployment Notes"
tags: [deploy-notes, service-scheduler, redis]
ticket: 2
status: executed-local
---

# Ticket 2 - Deployment notes

## Scope and references

- Ticket: [[2 - Service Discovery and Availability|Ticket 2]]
- Change summary: add dealership discovery, dealership-scoped service catalogs with bounded Redis cache-aside, advisory availability evaluation, shared Problem Details error codes/trace IDs, Redis health degradation, Redis Compose service, and executable examples.
- Implementation revision / artifact: checkout HEAD `3f8ab1e` plus the ticket 2 working-tree changes; local Compose API image built from `src/ServiceScheduler.Api/Dockerfile`.
- Related records: [[plans/2/README|Implementation plan]], [[test/2-test|Test report]], [[database/cache|Cache]], [[database/schema|Schema]], [[database/migration|Migrations]], [[architecture]], [[setup-guide]].

## Prerequisites and deployment order

- Target environment: isolated local demo only; synthetic data; no authentication.
- Required services: SQL Server 2022 Developer, Redis `7.4.12-alpine` (pinned digest), the API, and the one-shot `db-setup`.
- Required configuration: `ConnectionStrings:Runtime`, `ConnectionStrings:Setup`, `Scheduler:RuntimePassword`, `ConnectionStrings:Cache`, and the non-secret `Scheduler:Cache:*` defaults. Secrets stay in the ignored local `.env` or shell environment.
- Deployment order: SQL Server healthy -> `db-setup` completes -> API starts. The API does **not** depend on Redis health and must start with Redis unreachable.
- Database compatibility, downtime, backup: no schema migration, no backfill, and no downtime; the ticket is read-only against the existing schema.

## Deployment steps

1. **Database migrations/backfills:** not applicable. Ticket 2 adds no migration. Existing opening-hours, technician, qualification, shift, bay, and appointment indexes support the availability queries; generated SQL was reviewed during implementation and the index mapping is recorded in [[database/schema|Schema]]. See [[database/migration|Migrations]] for the no-migration record.
2. **Application/configuration:**
   - Deploy the API artifact with `ConnectionStrings:Runtime` and `ConnectionStrings:Cache` configured. Missing or malformed cache configuration is a startup validation failure; a valid but unreachable endpoint is not.
   - The Docker build stage pins SDK `10.0.403` (aligned with `global.json`).
   - Start the local stack with `docker compose up -d --wait`; Compose starts Redis alongside SQL Server and does not gate the API on Redis health.
3. **Cache:**
   - Key: `scheduler:v1:dealership:{dealershipId:D}:service-types`; payload version `v1`; absolute TTL 10 minutes.
   - After a catalog/seed change, delete the affected keys or bump the key version to `v2`.
   - Invalidation command for a local key: `docker compose exec redis redis-cli DEL "scheduler:v1:dealership:<lowercase-guid>:service-types"`.
   - If invalidation is missed, the TTL bounds staleness to the configured 10 minutes.

## Post-deployment verification

- [x] Confirm the API starts with Redis unreachable and with SQL healthy.
- [x] Confirm `/health/live` returns `200` and `/health/ready` returns `200` Degraded for SQL healthy/Redis down.
- [x] Confirm `/health/ready` returns `503` when SQL Server is down, regardless of Redis.
- [x] Confirm discovery and availability return `503 dependency_unavailable` during a SQL outage, while a valid catalog cache hit returns `200`.
- [x] Confirm the catalog miss populates the exact key, a hit serves the cached payload, and `DEL` forces a fresh SQL read.
- [x] Confirm Redis recovery restores `Healthy` readiness and cache population.
- [x] Historical: the former executable demo script (removed with the ticket 3 harness cleanup) exited zero against the Compose stack; current manual checks use Swagger UI at `/swagger/index.html`.
- [ ] Confirm ticket 2 acceptance only after the generated manual checklist is executed by a human (not yet generated).

Observed results and commands are recorded in [[test/2-test|the ticket 2 test report]].

## Rollback or forward recovery

- Failure triggers: startup configuration failure, unexpected `500` behavior, or a demonstrated cache inconsistency.
- Application/configuration recovery: redeploy the previous API version and remove `ConnectionStrings:Cache` from a version that did not require it. SQL data is unchanged.
- Database compatibility and data loss: no ticket 2 schema change; no rollback or data-loss action applies.
- Cache recovery and verification: stop/remove the Redis service; keys are ephemeral and contain no customer/vehicle data. Deleting `scheduler:v1:dealership:*` keys or bumping the key version forces fresh SQL reads. Verify by requesting the catalog and checking that the key is repopulated.
- Combined failure: liveness remains `200`; readiness is `503` while SQL is down; Redis-only failure remains `200` Degraded.

## Execution record

| Environment | Date / time and zone | Revision | Actions and observed results | Evidence / follow-up |
| --- | --- | --- | --- | --- |
| Local Windows 11; Docker `25.0.3`; Compose `v2.24.6-desktop.1`; SDK `10.0.403`; Redis `7.4.12-alpine`; SQL Server 2022 CU23 | 2026-11-01 (local) | HEAD `3f8ab1e` + ticket 2 working tree | `docker compose --env-file .env.example config` valid; images built after aligning the Docker SDK pin; `up -d --wait` started SQL/Redis/setup/API; `redis-cli ping` returned `PONG`; readiness JSON reported `Healthy` with database and redis checks; Redis stop produced readiness `200` Degraded and SQL-backed catalog/availability `200`; Redis start restored `Healthy`; `DEL` removed the key and the next catalog request repopulated it; SQL stop produced discovery/availability `503 dependency_unavailable`, catalog hit `200`, readiness `503`, liveness `200`; SQL restart restored readiness/`200`; the former demo script (since removed) exited `0`; `docker compose down` removed containers/network and retained the named SQL volume. | [[test/2-test|Test report]] execution record. First readiness immediately after an API restart can be `Unhealthy` while the SQL check warms, then `Healthy`. |
