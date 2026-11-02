# Template: Scheduler Manual Test File

Adapt this structure using `docs/test/backend-test-template.md` and applicable checks from `docs/test/integration-test-template.md`. Replace placeholders with inspected facts, or mark unavailable prerequisites as pending. Omit irrelevant sections with a reason. The template is not an execution record.

```markdown
---
title: "<ticket-number> - Manual Tests"
tags: [test, manual, service-scheduler]
ticket: <ticket-number>
status: not-run
---

# <Ticket number> - Manual tests

## Scope and prerequisites

- Ticket: [[<ticket filename without extension>]]
- Research / plan / primary test report:
- Reviewed revision and implementation scope:
- Requirement IDs and exclusions:
- Environment and implemented API base URL (pending if unavailable):
- Dependency versions and migration / seed baseline:
- Synthetic seed identifiers and future-time preparation:
- Dealership time zone where relevant:
- Required setup and isolated cleanup:

The initial local demo requires no login. Do not record credentials. Missing prerequisites remain pending or Blocked.

## Requirement coverage

| Requirement ID | Case IDs | Relevant automated evidence / remaining coverage |
| --- | --- | --- |

## Test cases

### TC-01: <observable expected behavior>

> [!NOTE]
> Covers: <actual requirement IDs>. Type: <manual API / operational>.

- Preconditions and synthetic data:
- [ ] **Step 1:** <implemented request or operational action>
    - **Expected:** <specific status, response, or observable outcome>
- [ ] **Step 2:** <retrieval or follow-up verification>
    - **Expected:** <persisted state, absence of unintended writes, or operational evidence>
- Cleanup:
- Execution status: Not run
- Observed result / evidence:

Repeat per applicable case. Use actual routes, identifiers, commands, and response contracts once available. Label unavailable details as pending.

## Execution record

| Date / time and zone | Revision | Case / actual action | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |

Use Passed, Failed, Not run, or Blocked with a reason. Checklist generation alone leaves cases Not run.

## Issues and follow-up

- Failed or blocked cases and reproduction details:
- Unexecuted checks and missing prerequisites:
- Related automated concurrency / permission evidence:
- Retest revision and observed results:
```

Choose checks according to the ticket:

- **Booking success:** Verify `201`, Location, retrieval, full associations, UTC interval, and duration snapshot.
- **Validation or capacity conflict:** Verify the specified error and no unintended confirmation, including applicable ownership, dealership, time, and resource boundaries.
- **Catalog/cache:** Verify correct values and applicable freshness or outage behavior using implemented signals.
- **Operations:** Verify applicable restart persistence, SQL readiness, or correlated logs/metrics/traces using isolated dependencies.

Manual checks supplement automated business, real SQL Server concurrency/permission, and Redis integration tests. Record links to their actual evidence; do not claim those guarantees from Swagger success alone.
