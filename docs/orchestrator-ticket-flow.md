---
title: "Scheduler Ticket Workflow Guide"
tags: [orchestrator, workflow, service-scheduler]
status: specified
---

# Scheduler ticket workflow

Use the `orchestrator-ticket` skill to coordinate research, verification, test planning, implementation with executed checks, review, and manual checklist generation for the Unified Service Scheduler. The active definition is `.claude/skills/orchestrator-ticket/SKILL.md`; research uses `.claude/skills/research-ticket/SKILL.md`. Read CLAUDE.md, [project scope](../README.md), [[architecture]], and the actual ticket as the baseline.

> [!IMPORTANT]
> Inspect the current checkout and the relevant development records before claiming implementation or verification.

## Commands and authorization

```text
research -> verify research -> plan -> verify plan -> implement -> optional review -> test
```

| Command                                    | Outcome                                                        |
| ------------------------------------------ | -------------------------------------------------------------- |
| `/orchestrator-ticket research <id>`          | Research the selected work item against the current checkout                 |
| `/orchestrator-ticket verify research <id>`   | Verify requirement coverage, scope, and inspected evidence     |
| `/orchestrator-ticket plan <id>`              | Plan implementation, test coverage, commands, and pass criteria |
| `/orchestrator-ticket verify plan <id>`       | Check traceability, prerequisites, and verification criteria   |
| `/orchestrator-ticket implement <id>`         | Implement all authorized steps, run required checks, and record evidence |
| `/orchestrator-ticket implement <id> 5`       | Implement step 5 of a single-file plan                         |
| `/orchestrator-ticket implement <id> phase-2` | Implement the unique indexed phase matching phase-2            |
| `/orchestrator-ticket review <id>`            | Review implementation against requirements and record findings |
| `/orchestrator-ticket test <id>`              | Generate the ticket's manual test checklist for human execution |
| `/orchestrator-ticket test <id> phase-2`      | Generate manual cases for the selected phase, preserving other cases |

A phase command authorizes only that phase. Before executing it, enforce the complete prerequisite chain: planning requires passing research verification; implementation requires passing plan verification; review and checklist generation require completed implementation with passing required automated/operational evidence for their scope. Missing, failed, blocked, stale, or unrecorded prerequisites stop execution. Report the earliest missing step and its exact command instead of running it automatically. For example, `/orchestrator-ticket plan <id>` before research verification must remind the user to run `/orchestrator-ticket verify research <id>` first, without creating or updating the plan.

Record verification status, checked ticket/input revision or content fingerprint (including linked parts), traceability, and gaps in the primary research entry point or plan overview. Existing documents alone do not establish passing verification. Input changes invalidate affected verification and downstream verification. When the user explicitly requests multiple phases or the whole workflow, continue through those phases in order while their gates pass; do not insert unrequested prerequisites. Optional review does not make automated tests optional.

Use the current session and available tools. Workflow/agent delegation is optional when authorized and supported; ordinary phase work can run directly when that tool is unavailable. Do not assume a particular Workflow interface, nested CLI launcher, or machine-specific checkout exists.

## Ticket discovery and record paths

Search directly inside this checkout's docs/tickets/. Match the exact leading ticket number followed by a separator or end of filename stem: `7` matches `7 - Example.md`, but not `70 - Other.md`. Resolve missing or ambiguous tickets before requirement-dependent work.

Tickets live directly in docs/tickets/. Organize supporting records by ticket number using the paths below. These paths are conventions, not existing implementation evidence.

| Phase / record | Output |
| --- | --- |
| Research, single document | `docs/research/<ticket>-research.md` |
| Research, multipart | `docs/research/<ticket>/00-overview.md` plus numbered topic files |
| Verify research | Requirement traceability matrix and evidence gaps |
| Plan, simple | `docs/plans/<ticket>/plan.md` |
| Plan, multi-phase | `docs/plans/<ticket>/README.md` plus `phase-N-name.md` files |
| Verify plan | Step-to-requirement mapping, dependency and verification checks |
| Implementation | Code, tests, affected documentation, and executed check results |
| Review | `docs/review/<ticket>-review.md` |
| Implementation verification evidence | `docs/test/<ticket>-test.md` |
| Test phase: manual checklist | `docs/test/<ticket>-manual-test.md` |

Keep one primary research entry point. Multipart research uses its overview and linked parts throughout later phases; a duplicate synthesized file is unnecessary. Split files by concern when useful; this does not require multiple agents. See [[research/README|Research guide]].

Use a single plan for at most five requirements and three affected layers; otherwise use an overview and phase files. Resolve selectors such as `phase-2` through the overview to the actual filename, rather than assuming `phase-2.md` exists. See [[plans/README|Plans guide]].

## Research and verification

Apply the research template's scope, coverage, data-flow, pre-audit, audit, gap, change-proposal, and scope-verification phases to actual scheduler components. UI analysis applies only when the ticket changes a UI. Existing files need inspected evidence; missing implementation and proposed additions must be labelled explicitly.

Verify both directions: every ticket requirement has research coverage, and each proposed change traces to a requirement or necessary dependency. Preserve exclusions and assumptions. Inspect candidate contents and actual signatures/call branches where code exists; do not infer applicability from a pattern match or template marker alone.

## Planning and implementation

Plans include ordered dependencies, requirement IDs, proposed/existing files, intended behavior, and verification for each step. Define applicable unit/API/integration/concurrency/operational coverage, test targets, commands, dependencies, expected outcomes, and pass criteria. Existing tests may be run for a useful baseline; new tests are written and executed during implementation. Each phase must have achievable exit evidence. The first working booking slice includes its booking procedure and restricted runtime write boundary; it cannot depend on a later phase to supply the only permitted write path.

Use EF Core migrations and coordinated model/procedure/grant updates, following [[database/maintenance|Database maintenance]] and [[database/migration|Migration guidance]]. Run migrations/seeds with a privileged setup identity and the API with its restricted runtime identity. Preserve atomic confirmation, technician-then-bay locking, full-duration eligibility, and both resource conflict checks.

Assess service-catalog cache impact using [[database/cache|Cache guidance]], recording affected keys/freshness actions or a reason for no impact. Create [[deploy-notes/README|deployment notes]] when operational actions are needed.

Discover actual solution/project paths, SDK pins, and commands from the implemented checkout and [[setup-guide]]. During implementation, write tests alongside code, run affected checks as steps finish, and complete required automated/operational verification for the selected scope. Record results in `docs/test/<ticket>-test.md` using the testing templates. Document exact working directories and prerequisites in setup and the root README. Resolve in-scope failures and rerun affected checks, including after review fixes; required failed or blocked checks prevent implementation completion and cannot be deferred to `test`.

## Review and manual checklist generation

Review against requirement behavior and impact using [[review/README|Review guidance]]. Prioritize transactional correctness, permissions, time handling, relationship scope, failure behavior, and missing tests.

Implementation owns the execution report using [[test/README|Testing guidance]], [[test/backend-test-template|Backend test template]], and [[test/integration-test-template|Integration test template]]. It includes:

- Requirement-linked automated unit/API tests and real SQL Server/Redis dependency tests as applicable.
- Restricted runtime identity, independent connections, controlled time, coordinated concurrency, and final stored-state assertions for booking guarantees.
- Applicable operational checks for cache failure, restart persistence, database readiness, and correlated logs/metrics/traces.
- Exact commands, working directories, revision, environment/dependency versions, expected versus observed results, and evidence.
- Passed, Failed, Not run, or Blocked status with reasons. Missing scaffolding or dependencies must be reported; generated checklists and test files are not execution evidence.

Use the current ticket's requirements, acceptance criteria, and verification cases as the test baseline. Architecture supplies design context. Intermediate phase reports identify their narrower coverage and remaining checks. Keep a ticket's primary test report and update it with implementation-phase execution records.

The `test` command generates `docs/test/<ticket>-manual-test.md` by following `.claude/skills/create-manual-test/SKILL.md`. Inspect the implementation and passing evidence; if required implementation checks are missing or stale, report the gap and the corresponding implement command. Do not run automated tests, execute manual cases, or change application/test code in this phase.

Generate requirement-linked manual cases with prerequisites, synthetic data, actual routes/commands, actions, expected outcomes, and scoped cleanup. New cases are Not run or Blocked with a reason. Preserve prior execution records and other phases' cases. Link the checklist from the primary report and link automated evidence from the checklist. Human testers execute and record manual results; generating instructions does not mark acceptance criteria Passed.

## Troubleshooting and completion

| Condition | Response |
| --- | --- |
| Ticket missing or ambiguous | Request the exact ticket before requirement-dependent work |
| Prerequisite missing, failed, blocked, stale, or unrecorded | Stop the requested phase, report the earliest unmet prerequisite and exact command; do not run it automatically |
| Existing and multipart research compete | Inspect status/references and resolve the primary baseline before proceeding |
| Application scaffolding or dependencies missing | Record blocked/not-run checks with the missing prerequisite |
| Build or test fails | Inspect the failure, fix within scope, rerun affected checks, and report unresolved issues |
| Phase selector missing or ambiguous | List the actual indexed phase names |
| Optional Workflow tool unavailable | Execute the same phase directly with available tools |

A phase report identifies its output files, requirement coverage, executed results, unresolved decisions, and next work. The workflow is complete only when required behavior and checks have passing evidence and remaining scope is resolved. Update the root README and setup guide with verified commands, [[architecture]] with design decisions, and the ticket's test report with observed checks when relevant; do not invent approvals or passing results.
