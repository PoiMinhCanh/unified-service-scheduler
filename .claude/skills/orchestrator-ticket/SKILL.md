---
name: orchestrator-ticket
description: Coordinate ticket research, verification, test planning, implementation with executed automated checks, review, and manual test checklist generation.
argument-hint: '<phase> <ticket> [step-number | phase-name] (e.g. research 1, implement 1 phase-2)'
user-invocable: true
metadata:
  version: '1'
  hybrid: true
  multi-phase: true
---

# Orchestrator Ticket

Coordinate ticket phases using [CLAUDE.md](../../../CLAUDE.md) and [shared ticket workflow](../../rules/ticket-workflow.md) for the project baseline, ticket discovery, prerequisites, authorization, documentation format, and evidence. Read [coding conventions](../../rules/conventions.md) and [access rules](../../rules/auth-integrations.md) for affected implementation and verification concerns; domain policy belongs in those sources.

## Commands and execution

```text
/orchestrator-ticket research <ticket>
/orchestrator-ticket verify research <ticket>
/orchestrator-ticket plan <ticket>
/orchestrator-ticket verify plan <ticket>
/orchestrator-ticket implement <ticket> [step-number | phase-name]
/orchestrator-ticket review <ticket>
/orchestrator-ticket test <ticket> [phase-name]
```

The flow is research -> verify research -> plan -> verify plan -> implement -> optional review -> test. Apply the shared authorization rules and report results and unresolved requirements at each meaningful boundary.

Testing ownership: `plan` defines coverage and pass criteria; `implement` writes and runs required automated and operational checks and records evidence; `test` generates the manual checklist for human execution. Generating the checklist does not execute tests or establish manual verification.

### Strict prerequisite gate

Before executing any requested phase, check the complete prerequisite chain for the same ticket and selected scope. Apply the gate even when a downstream output already exists.

| Requested phase | Required completed prerequisites |
| --- | --- |
| research | Resolved ticket |
| verify research | Research |
| plan | Research and passing research verification |
| verify plan | Research, passing research verification, and plan |
| implement | Research, passing research verification, plan, and passing plan verification |
| review / test | All implementation prerequisites and completed implementation with passing required automated/operational evidence for the requested scope; review is optional before test |

If any prerequisite is missing, failed, blocked, stale, or has no recorded evidence, stop before executing or modifying outputs for the requested phase. Report the earliest unmet prerequisite and its exact command. Do not run the missing step automatically. For example, `/orchestrator-ticket plan 1` without passing research verification must reply: `Cannot run plan for ticket 1: research verification is missing. Run /orchestrator-ticket verify research 1 first.` If research itself is missing, point to `/orchestrator-ticket research 1` instead.

For an explicit request covering multiple phases, execute only the requested phases in dependency order, checking each gate before proceeding. Never insert an unrequested prerequisite; a request for the whole workflow includes all phases. Failed or blocked results stop dependent work.

Research or plan file existence does not prove verification. Record each verification outcome in a `Verification` section of the primary research entry point or plan overview: command, Passed/Failed/Blocked status, checked ticket and input revision or content fingerprint (including linked parts), requirement traceability, and unresolved gaps. Only an explicit Passed result with no blocking gaps for the current inputs satisfies a gate. Changed ticket/research inputs invalidate research verification and downstream verification; changed plan inputs invalidate plan verification. If freshness cannot be established, report verification as missing or stale and give the verification command rather than silently re-verifying.

Use the current session and available tools by default. If delegation is authorized and an appropriate Workflow/agent tool is available, pass the resolved repository root, exact ticket path, scope, input/output paths, and verification requirements. If it is unavailable, perform the same phase directly; do not assume an unlisted Workflow interface, launch nested CLI processes, or alter permissions to enable it.

## Resolve inputs and outputs

Resolve the ticket and primary research under [shared ticket workflow](../../rules/ticket-workflow.md#baseline-and-ticket-discovery). Use the owning guide for each record's paths and content:

| Record | Guide |
| --- | --- |
| Research, including multipart records | [Research guide](../../../docs/research/README.md) |
| Simple or multi-phase plans | [Plans guide](../../../docs/plans/README.md) |
| Review | [Review guide](../../../docs/review/README.md) |
| Implementation test results and generated manual checklist | [Testing guide](../../../docs/test/README.md) |

Select plan structure using the plans guide. Resolve a supplied `phase-2` to the unique indexed filename, such as `phase-2-booking.md`; report missing or ambiguous matches instead of constructing a nonexistent filename.

## Research

Read and follow .claude/skills/research-ticket/SKILL.md. Inspect actual implementation where present; mark missing code and proposed additions explicitly. Apply the template's phases to scheduler components, with UI analysis only when relevant. Follow docs/research/README.md for naming and multipart organization.

Return the primary output path, requirement coverage, proposed changes, assumptions/open gaps, and database/cache impact. A research document is not implementation evidence.

## Verify research

Read the ticket, primary research entry point, and relevant parts. Check:

- Forward trace: every ticket requirement and acceptance criterion has research coverage.
- Reverse trace: each research requirement ID exists in the ticket, and each proposed change supports it or a necessary dependency. Check data sources, gap analysis, conditional UI analysis, and the change proposal as well as the audit.
- Exclusions and assumptions are preserved; research does not silently weaken or expand the ticket.
- Existing-file and symbol claims match inspected contents. Check candidate structure and exact call signatures/branches when relevant; proposed new files are not stale existing-file references.
- Booking, schema, cache, and verification impacts follow the current architecture and domain guides.

Report a requirement traceability matrix and concrete gaps. Correct evidence-backed documentation errors within scope, inspect the diff, and recheck affected traces. Never treat a template marker or a populated evidence column alone as proof of validation.

## Plan and verify plan

Read verified research and follow docs/plans/README.md. Include requirement IDs, scope, dependency order, planned/existing files, intended behavior, and verification for each step. Define required unit, API, integration, concurrency, and operational scenarios as applicable, with test-project/file targets, commands, dependency/identity setup, expected results, and pass criteria. Plan supplementary human checks separately. Run existing tests only when useful to establish a baseline and record those observations; new behavior's tests are written and executed during implementation.

For multi-phase plans, the overview owns the dependency graph and phase index. Each phase has its own exit evidence. A working booking slice must include its SQL procedure and restricted write boundary; do not plan a successful runtime POST before its required write path exists.

To verify a plan, check forward and reverse traceability against the ticket and research, phase/file existence, prerequisite order, absence of cycles, actual call paths when the approach changes, and relevant database/cache/deployment impacts. Confirm every required automated/operational check has an implementation step and achievable exit evidence; do not defer execution to `test`. Planned paths and commands remain labelled as planned until scaffolded and verified. Report unresolved dependencies before implementation.

## Implement

Read the ticket, research, selected plan scope, and actual repository state. With no selector, implement all authorized plan steps in dependency order; with a step or phase selector, restrict changes to that scope and its established prerequisites. Report missing prerequisites instead of silently implementing unrelated phases.

Apply [persistence](../../rules/conventions.md#persistence-and-migrations), [booking integrity](../../rules/conventions.md#booking-integrity-and-time-handling), and [cache/observability conventions](../../rules/conventions.md#cache-and-observability) to affected changes. Complete documentation and operational follow-up through [shared workflow](../../rules/ticket-workflow.md#documentation-ownership-and-formatting).

Write meaningful automated tests alongside implementation and add missing in-scope coverage in the actual test projects. Discover and run actual restore/build/format/test commands using [verification conventions](../../rules/conventions.md#code-clarity-and-verification) and the shared evidence rules. Run affected checks as steps finish, then complete required verification for the selected scope, including real-dependency and operational scenarios required by the ticket. Record missing scaffolding or dependencies instead of claiming execution.

Create or extend `docs/test/<ticket>-test.md` during implementation using the [testing guide](../../../docs/test/README.md), [backend template](../../../docs/test/backend-test-template.md), and [integration template](../../../docs/test/integration-test-template.md). Map requirements to executable scenarios and record revision, environment, dependencies, actual commands, outcomes, and evidence; preserve prior execution records. Planned manual checks remain Not run until a human executes them. Required automated/operational failures or blockers prevent implementation completion for that scope; do not defer them to the checklist phase. After review fixes, rerun affected checks and update the evidence before handoff.

Resolve in-scope build/test failures from their evidence, rerun affected checks, and report unresolved failures. Do not continue dependent phases as verified while required checks fail or remain blocked. Report changed files, requirement coverage, executed commands/results, and remaining checks.

## Review

Use [reviewer guidance](../../agents/clean-code-reviewer.md) and [the review guide](../../../docs/review/README.md) to review the requested revision/diff against requirements and record findings. Optional review does not waive required implementation tests.

## Test (manual checklist generation)

This phase produces manual instructions for the user; it does not run automated tests, execute the manual cases, or change application/test code. Follow [create-manual-test](../create-manual-test/SKILL.md) and its checklist template.

1. Read the ticket, research, plan, implementation, and implementation-phase test report. For a phase selection, resolve its exact indexed file and requirement IDs; a working-tree diff alone does not identify all phase changes or committed work.
2. Check required implementation evidence for the selected scope. If required automated/operational evidence is missing, failed, blocked, or stale, report the gap and the appropriate `/orchestrator-ticket implement <ticket> [selector]` command; do not run tests or repair implementation in this phase.
3. Create or update `docs/test/<ticket>-manual-test.md` with requirement coverage, prerequisites, actual routes/commands, synthetic data, numbered actions or checkbox steps, expected outcomes, and scoped cleanup. Mark unavailable prerequisites explicitly instead of inventing executable details.
4. New manual cases start Not run, or Blocked with a reason. Preserve prior human execution records and identify their revision; do not relabel them as evidence for changed behavior. For a selected phase, retain other phases' cases and records.
5. Link the checklist from `docs/test/<ticket>-test.md` and link relevant automated evidence from the checklist. Preserve implementation results; checklist generation must not mark manual cases or ticket acceptance criteria Passed.
6. Report the checklist path, coverage, prerequisites, and remaining human verification. Checklist generation completes this phase; ticket verification is complete only when all required automated and human checks have passing evidence.

## Evidence and handoff

Follow [shared evidence and handoff rules](../../rules/ticket-workflow.md#evidence-and-handoff). Report phase output paths and the next applicable phase; keep failed or blocked checks visible before dependent work.
