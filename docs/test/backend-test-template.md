---
title: "Backend Ticket Test Template"
tags: [test, template, service-scheduler]
---

# Backend ticket test template

Copy the template below into `docs/test/<ticket-number>-test.md`, or `<ticket-number>-manual-test.md` for a separate manual checklist. Replace placeholders with inspected facts and omit irrelevant sections with a reason. This file is a template, not an execution record.

Use automated xUnit tests for core rules and API/persistence behavior. Manual checks can use Swagger UI or executable HTTP requests after those tools exist. No browser automation or authentication workflow is required by the initial release.

```markdown
---
title: "<ticket-number> - Backend Tests"
tags: [test, service-scheduler]
ticket: <ticket-number>
status: not-run
---

# <Ticket number> - Backend tests

## Scope and prerequisites

- Ticket: [[<ticket filename without extension>]]
- Research / plan / reviewed revision:
- Requirements and exclusions:
- Environment, API base URL, dependency versions, and configuration source:
- Migration / seed baseline and synthetic test data:
- Actual test project, command, and working directory (pending until implemented):
- Setup and cleanup / data isolation:

## Requirement coverage

| Requirement ID | Scenario IDs | Level | Automated test file / symbol or manual case | Status |
| --- | --- | --- | --- | --- |

## Test cases

### TC-01: <expected behavior>

> [!NOTE]
> Covers: <requirement IDs>. Level: <unit / API integration / manual / operational>.

- Preconditions and synthetic data:
- [ ] **Step 1:** <action or actual command>
    - **Expected:** <specific response, rule outcome, or persisted state>
- [ ] **Step 2:** <follow-up read or verification>
    - **Expected:** <observable result>
- Cleanup:
- Execution status: Not run
- Observed result / evidence:

Repeat a case section for each scenario. Include applicable invalid input, boundaries, resource conflicts, relationship mismatches, and regression behavior.

## Execution record

| Date / time and zone | Revision | Case / actual command | Observed result | Status | Evidence |
| --- | --- | --- | --- | --- | --- |

## Issues and follow-up

- Failed or blocked cases, reproduction details, and related review findings:
- Unexecuted checks and why:
- Retest revision and results:
```

For a successful booking, verify the `201` response, retrieve the appointment, and assert the correct persisted customer/vehicle/dealership/service/technician/bay associations and duration. For rejected requests, assert the specified API error and absence of an unintended write. Derive exact contracts from the ticket and implementation, not this example alone.

See [[test/integration-test-template|Integration test template]] for dependency-specific setup and concurrency checks. Do not use a manual success-path check as evidence that concurrent booking protection works.
