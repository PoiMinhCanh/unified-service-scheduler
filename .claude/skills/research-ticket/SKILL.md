---
name: research-ticket
description: Research a Unified Service Scheduler ticket before planning or implementation, recording requirements, inspected evidence, proposed changes, and verification scenarios.
argument-hint: '<number> (e.g. 1 or #1)'
user-invocable: true
metadata:
  version: '1'
---

# Research Ticket

Research the ticket against the current checkout using [CLAUDE.md](../../../CLAUDE.md) and [shared ticket workflow](../../rules/ticket-workflow.md) for discovery, prerequisites, documentation format, and evidence.

## Inputs

1. Resolve and read the actual ticket under the shared discovery rules. Preserve its requirement IDs, acceptance criteria, exclusions, and assumptions.
2. Read the project baseline and applicable domain guides identified by [research workflow rules](../../rules/research-workflows.md). These own data-flow tracing, database/booking analysis, cache impact, and verification requirements.
3. Inspect referenced images and relevant PDF pages using [the assets guide](../../../docs/assets/README.md). Record missing or unreadable attachments without guessing.
4. Read [the research guide](../../../docs/research/README.md) for naming and multipart organization and [the research template](../../../docs/templates/research-template.md) for phase structure.

## Research execution

Execute the template's phases using the applicability gates in [research workflow rules](../../rules/research-workflows.md#research-phases-and-applicability). Inspect actual candidate contents and reachable call paths before classifying changes; identify absent implementation and proposed additions explicitly. Use [code-tool rules](../../rules/code-tools.md) for inspection.

For affected behavior, read [coding conventions](../../rules/conventions.md) and [access rules](../../rules/auth-integrations.md); apply their constraints to the findings and dependency-ordered proposal. Keep requirements, inspected facts, assumptions, proposed decisions, and unresolved questions distinguishable.

Maintain one primary research entry point under the shared workflow. Split complex research by concern when helpful; the overview owns the part index, dependencies, and consolidated decisions. File decomposition does not require delegation.

## Output and handoff

Write the primary research document and any linked parts using the research template and shared documentation format. Include requirement-to-verification mapping under [the testing guide](../../../docs/test/README.md); proposed checks remain Not run until executed.

Report the entry point, requirements covered, proposed changes, database/cache impact or reason for none, unresolved decisions, and verification needs. The next phase is research verification, followed by planning under [the plans guide](../../../docs/plans/README.md).
