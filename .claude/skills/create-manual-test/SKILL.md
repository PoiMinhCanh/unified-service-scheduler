---
name: create-manual-test
description: Generate an Obsidian-compatible manual API or operational test checklist from a scheduler ticket, its research, and the actual implementation. Supplements automated tests.
argument-hint: '[ticket number] (optional — inferred from conversation if omitted)'
user-invocable: true
metadata:
  version: '1'
---

# Create Manual Test

Create `docs/test/<ticket>-manual-test.md` for supplementary manual API and operational checks. Read [CLAUDE.md](../../../CLAUDE.md) and follow [shared ticket workflow](../../rules/ticket-workflow.md) for discovery, prerequisites, documentation format, and evidence.

The orchestrator `test` phase uses this skill to generate instructions for human execution. Automated/operational execution and the primary `<ticket>-test.md` evidence belong to implementation. Checklist generation does not run tests, execute manual cases, or modify application/test code. When invoked for an orchestrator phase selector, limit new/updated cases to that phase's requirements and preserve other cases and prior execution records.

## Inputs and implementation scope

- Resolve the ticket from `$ARGUMENTS` or unambiguous conversation context using the shared discovery rules.
- Read the primary research and relevant linked parts, the ticket's plan, and existing test report when present. Preserve actual requirement IDs.
- Identify the requested implementation revision or diff. Inspect unstaged, staged, or committed changes as appropriate; plain `git diff` alone does not cover staged or committed work. Inspect relevant code and existing tests to confirm behavior.
- Read [the testing guide](../../../docs/test/README.md), [backend template](../../../docs/test/backend-test-template.md), and applicable scenarios in [the integration template](../../../docs/test/integration-test-template.md). Use [the manual checklist template](references/template.md) for output format.

## Checklist content

Map applicable requirements and changed behavior to concrete cases. Use Swagger UI for API checks and implemented operational tools for dependency and telemetry checks. Include success, validation, resource conflicts, relationship scope, relevant boundaries, and regression checks according to the actual change. Add UI cases only when a ticket changes a UI.

Use the implemented API base address and routes discovered from configuration and setup documentation. Use synthetic seeded UUIDs and future timestamps within seeded opening hours and shifts. Record the dealership time zone where relevant. If scaffolding, commands, configuration, or seed identifiers are unavailable, mark the affected prerequisites and cases as pending or Blocked; do not invent executable values.

Apply [access rules](../../rules/auth-integrations.md) to environment, authentication, synthetic data, and credentials; derive steps from the actual ticket and implemented configuration.

For booking success, check the response and retrieve the persisted associations and interval. For rejected requests, check the ticket-defined error and absence of an unintended write. Select operational scenarios from the integration template and link automated evidence required by [verification conventions](../../rules/conventions.md#code-clarity-and-verification).

## Output and execution status

- Create or update `docs/test/<ticket>-manual-test.md`; preserve existing execution records and distinguish new planned cases from prior observed results.
- Use shared documentation formatting and the manual template's per-case requirement coverage and `- [ ]` checkbox steps with four-space-indented expected results. Do not put interactive checkboxes in table cells.
- Write English steps with explicit setup and scoped cleanup. Keep expected outcomes separate from observations.
- New cases start as Not run or Blocked with a reason. Record Passed or Failed only after actual execution, with revision, environment, command or action, and observed evidence.
- Link the checklist from the ticket's primary `docs/test/<ticket>-test.md` report when it exists. Report the output path, coverage, and remaining prerequisites without claiming execution from checklist generation.
