---
title: "4 - Observability: Phase 5 - Operational Verification"
tags: [plans, service-scheduler, observability, operations, verification]
ticket: 4
status: implemented
updated: 2026-11-02
---

# Phase 5 - Operational verification

- Ticket: [[4 - Observability|Ticket 4]]
- Research: [[research/4/00-overview|Ticket 4 research overview]]; [[research/4/03-verification-workload-and-operations|Verification and operations]].
- Plan overview: [[plans/4/README|Ticket 4 implementation plan]].
- Requirements: S4-05, S4-06; AC4-06, AC4-08; TC4-11 through TC4-14, TC4-18; T-13; S4-V01; project P-01/P-02/P-04.
- Dependencies: Phase 3 Compose stack and Phase 4 automated telemetry checks.

## Scope and intended behavior

This phase documents and executes Compose-level operational checks against the real local stack. The operational checks verify startup, access, Grafana provisioning, JSON log capture, Redis degradation, telemetry outage/recovery, and backend delivery.

Exclusions: no production load model, SLO, capacity guarantee, centralized logs, alerting, or application UI. Human Grafana/log inspection remains manual; direct Compose checks provide operational evidence.

## Operational verification

Run the following Compose-level checks directly and record commands, results, and evidence in `docs/test/4-test.md`:

1. `docker compose -p <project> config` and `up -d --build --wait`; verify API/Collector/Prometheus/Tempo/Grafana readiness with bounded polling.
2. Generate synthetic catalog/availability/booking traffic through direct requests with a known `traceparent`; query Prometheus for the expected series/outcomes and Tempo for the trace, including through the Grafana Tempo data-source proxy; assert bounded label sets and absence of synthetic sensitive values.
3. Capture `docker compose -p <project> logs --no-color --no-log-prefix api`, parse JSON lines, and assert the request event contains the same trace ID, route template, status class, and outcome.
4. Query Grafana `/api/health`, data-source UIDs, dashboard UID, and the panel PromQL; assert provisioning and zero-traffic/expected-workload displays as documented.
5. Stop Redis, request catalog/booking, assert SQL fallback and booking/retrieval correctness, cache-failure metric/log/Tempo signal, readiness `Degraded`/`200`; restart Redis and verify recovery.
6. Stop the Collector, assert API start/booking/readiness remain correct; restart the Collector, generate new traffic, and poll Prometheus/Tempo within the documented observation window; no backfill claim.

Use synthetic seeded IDs and keep secrets, connection strings, request bodies, VINs, and customer contact data out of recorded evidence. Human Grafana/log inspection remains a separate check. No standalone verification script is required.

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |
| 5.3 | S4-05, S4-06 | Documented Compose operational checks | Document the Compose startup/access/backend/log/Redis/Collector outage/recovery checks and result output. | The documented procedure covers the documented checks, fails with useful diagnostics, and records results in the test report without sensitive data. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 5.5 | S4-05, S4-06 | Direct Compose operational checks; `docs/test/4-test.md` | Execute Compose startup, provisioned access, JSON log capture, Grafana/Prometheus/Tempo checks, Redis outage, telemetry outage/recovery, and booking/readiness assertions. | Operational checks pass or are Blocked with a reason; observed results and evidence paths are recorded; manual Grafana inspection remains explicitly pending. | Implemented 2026-11-02 (see [[test/4-test]]) |
| 5.6 | S4-04, S4-06 | `docs/deploy-notes/4-deploy-notes.md`; `docs/test/4-test.md` | Record startup/access/retention/recovery/troubleshooting; preserve prior ticket records. | Deploy notes and test report reflect actual observed commands/results; no planned result is presented as executed. | Implemented 2026-11-02 (see [[test/4-test]]) |

## Verification

| Case | Command / scenario | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |
| TC4-11, TC4-12 Telemetry outage/recovery | Direct Compose verification stops/starts the Collector around booking/readiness checks | API/booking/readiness remain correct with telemetry unavailable; new exports appear within the documented window after recovery; no backfill claim. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-13, T-13 Redis degradation | `docker compose stop redis`; catalog/booking/readiness checks; restart Redis | Cache-failure signals, SQL fallback, booking/retrieval correctness, persisted row, readiness `Degraded`/`200`, and recovery observed. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-14, T-15 Correlation/access | Script generates a known `traceparent`, captures JSON API logs, queries Tempo/Prometheus/Grafana | Same trace ID in log/Tempo; route template/status/outcome match; provisioned dashboard/data sources accessible. | Not run (plan-time record; see [[test/4-test]]) |
| TC4-18 Operational instructions | Follow documented startup/access/retention/recovery/troubleshooting steps | Instructions match observed behavior; human-only inspections remain Not run until executed. | Not run (plan-time record; see [[test/4-test]]) |

## Exit criteria

- Direct Compose verification covers Compose startup, access, backend delivery, dashboard provisioning, JSON log correlation, Redis outage/fallback, and telemetry outage/recovery checks, or records Blocked checks with reasons.
- `docs/test/4-test.md` contains actual commands, observed results, evidence paths, and explicit unexecuted/manual checks; no performance target or ticket acceptance is claimed.
- `docs/deploy-notes/4-deploy-notes.md` reflects actual startup/access/retention/recovery/troubleshooting behavior.
- Required automated/operational failures or blockers prevent Phase 6 completion.

## Impacts and remaining decisions

- Database/migrations: none.
- Cache: no key/payload/TTL/invalidation change.
- Booking: no behavior change.
- Configuration/deployment: Compose telemetry environment, services, volumes, ports, retention, and access instructions.
- Remaining decision: a separate Compose project/volume is recommended to avoid polluting base demo data; if the base stack is used, cleanup must still remove only run-created appointments and keys.
- Record actual Docker/API query results in the test report; do not mark checks Passed from a generated command list.
