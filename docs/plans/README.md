---
title: "Implementation Plans Guide"
tags: [plans, workflow, tickets]
---

# Implementation plans

Use `docs/plans/` for ticket implementation plans: the changes to make, their order and dependencies, and how to verify each step. Plans translate ticket requirements and research into actionable work; they do not prove that implementation or testing is complete.

## Folder and file naming

Keep each ticket's plan in `docs/plans/<ticket-number>/`. For example, ticket 1 uses `docs/plans/1/`.

| Plan size | Files | Example |
| --- | --- | --- |
| Simple: at most 5 requirements and at most 3 affected layers | One `plan.md` | `docs/plans/1/plan.md` |
| Complex: more than 5 requirements or more than 3 affected layers | A `README.md` overview and `phase-N-name.md` files | `docs/plans/1/README.md`, `docs/plans/1/phase-1-foundation.md` |

Choose one structure per ticket. In a multi-phase plan, the ticket's README lists the phase files, dependency order, and overall progress; each phase file contains its own implementation steps and verification criteria.

Tickets live directly in `docs/tickets/`. Store each ticket's plan files under `docs/plans/<ticket-number>/`.

These paths are naming examples, not claims that ticket 1 already has a plan.

## What a plan includes

- Links to the ticket and research, requirement IDs, scope, exclusions, assumptions, and unresolved decisions.
- Ordered steps or phases with dependencies, affected files or components, intended behavior, and completion criteria.
- Database, migration, cache, configuration, API, and deployment impacts where relevant.
- Verification scenarios, expected outcomes, and the requirements each check covers. Include actual commands when known; label commands and paths that depend on future scaffolding as planned.
- Status and evidence for completed work, failed checks, and remaining tasks.

## Testing ownership

Planning defines applicable unit, API, real-dependency integration, concurrency, and operational checks, their implementation steps, test files/projects, commands, prerequisites, expected outcomes, and pass criteria. Existing tests may be run to establish a useful baseline; new behavior's tests are written and executed during implementation. Keep proposed commands explicitly planned until available.

Implementation owns executing required automated/operational checks and recording their results in `docs/test/<ticket>-test.md` before its scope is complete. Each phase's exit criteria must include that evidence; required failed or blocked checks prevent completion. The orchestrator `test` phase generates `docs/test/<ticket>-manual-test.md` for human execution and cannot supply deferred automated verification.

Use Obsidian-compatible Markdown with `title` and `tags` frontmatter and internal wiki links. Keep each paragraph and list item on one line. Preserve code blocks, diagrams, and tables as appropriate.

## Keeping plans current

Read the ticket and available research before writing a plan. Update the plan when an approved requirement or implementation decision changes its scope, dependencies, or verification. Keep phase work tied to ticket requirements and explain deviations.

Mark implementation and verification separately. A completed step without executed checks remains unverified; record actual results and evidence rather than checking off planned outcomes. Do not invent existing code paths or line numbers for the new project.

For data changes, follow [[database/maintenance|Database maintenance guideline]]. If a ticket needs deployment-specific actions, create its notes using [[deploy-notes/README|Deployment notes guide]] and link them from the plan.

## Single-file or phase template

Use this structure in `plan.md` or adapt it for an individual phase. For multi-phase plans, add the phase index and dependency overview to the ticket's README.

```markdown
---
title: "<ticket-number> - <plan or phase title>"
tags: [plans, service-scheduler]
ticket: <ticket-number>
status: planned
---

# <Plan or phase title>

## Requirements and scope

- Ticket: [[<ticket filename without extension>]]
- Research: <link if available>
- Requirement IDs, intended outcome, exclusions, and assumptions:

## Dependencies

- Prerequisite steps, phases, or decisions:

## Implementation steps

| Step | Requirement IDs | Files / components | Change | Completion criteria | Status |
| --- | --- | --- | --- | --- | --- |

## Verification

| Requirement IDs | Scenario / command | Expected outcome | Observed result / evidence |
| --- | --- | --- | --- |

## Impacts and remaining decisions

- Database / migrations / cache:
- API / configuration / deployment notes:
- Open questions, deviations, and follow-up:
```
