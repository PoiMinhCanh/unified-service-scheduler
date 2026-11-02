# Shared ticket and documentation workflow

These rules apply to ticket work in every skill and review. Keep shared policy here; skills own phase-specific procedures, and domain rules own implementation constraints.

## Baseline and ticket discovery

- Use the current checkout, [project scope](../../README.md), and [architecture](../../docs/architecture.md). Historical assessments and inherited examples do not override this baseline.
- When a ticket number is supplied, locate and read the actual ticket directly under `docs/tickets/` before requirement-dependent research, planning, coding, or review.
- Remove an optional `#` and match the exact leading number followed by a separator or end of the filename stem. `1` matches `1.md` or `1 - Init Project.md`, but not `10 - Other.md` or `21.md`.
- Read the unique match. If none match, request the ticket path; if multiple match, ask which ticket is intended before requirement-dependent work. Preserve its requirement IDs, acceptance criteria, exclusions, and assumptions.

## Research and implementation prerequisites

- Before implementing or fixing a ticket, check for research at `docs/research/<number>-research.md` or `docs/research/<number>/00-overview.md`. Read the primary entry point and relevant linked parts.
- Keep one primary research entry point. If both forms exist, inspect their status and references and resolve the baseline before using competing records.
- Research is required before ticket implementation. For direct implementation, if missing, perform it when already authorized; otherwise ask whether to run `/research-ticket <number>` first. Orchestrator invocations use the strict prerequisite gate in [orchestrator-ticket](../skills/orchestrator-ticket/SKILL.md#strict-prerequisite-gate): report the earliest missing step and its command instead of automatically running it.
- Follow the dependency-ordered Change Plan in research for direct implementation. When using the orchestrator, follow its research verification and separate plan/verification phases before implementing the selected plan scope.
- A phase invocation authorizes only that phase, not missing prerequisite phases. Continue through explicitly authorized phases in dependency order only when their prerequisite evidence passes; never skip verification or infer it from existing output files. Do not infer deployment or unrelated system access. Delegate only when authorized and supported; otherwise work in the current session.

## Documentation ownership and formatting

Keep project scope, requirement IDs, and completion criteria in [README](../../README.md); domain rules, API contracts, and design in [architecture](../../docs/architecture.md); and environment instructions in [setup](../../docs/setup-guide.md). Each ticket owns its acceptance criteria and verification cases; its test report records execution evidence. Each ticket and its records describe their current scope and exclusions directly, without assigning work to future tickets.

Use [the documentation index](../../docs/README.md) for navigation and the applicable folder guide for naming and content:

| Record | Guide |
| --- | --- |
| Research and multipart organization | [Research guide](../../docs/research/README.md) |
| Implementation plans and phase structure | [Plans guide](../../docs/plans/README.md) |
| Findings, severity, and resolution status | [Review guide](../../docs/review/README.md) |
| Executable coverage, scenarios, and observed results | [Testing guide](../../docs/test/README.md) |
| Supporting images and PDFs | [Assets guide](../../docs/assets/README.md) |
| Per-ticket operational actions, when needed | [Deployment guide](../../docs/deploy-notes/README.md) |

When creating or updating `docs/` files:

- Include YAML frontmatter with `title` and `tags`.
- Use internal wiki links such as `[[filename]]` or `[[filename|display text]]`.
- Treat `docs/` as the Obsidian vault root. Folder-qualified wiki links omit the repository's `docs/` prefix, for example `[[research/1/00-overview|Foundation research overview]]`; escape the alias separator as `\|` inside Markdown tables. Repository file paths in prose and commands retain `docs/`.
- Use Obsidian callouts such as `> [!NOTE]` or `> [!WARNING]` instead of plain blockquotes.
- Keep each paragraph and list item on one line; preserve tables, diagrams, and code blocks. Use standard Markdown otherwise.

Update affected database documentation in the same change under [database maintenance](../../docs/database/maintenance.md), including a reason when a ticket has no database impact. Assess cache impact under [research workflow rules](research-workflows.md#cache-impact-check).

## Evidence and handoff

- Inspect the checkout before asserting files, symbols, configuration, routes, or commands exist. Use [the setup guide](../../docs/setup-guide.md) and root README for verified setup/build/run/test commands; update both when implementation establishes new commands.
- Keep planned design, implementation status, and verification status distinct. Record missing prerequisites and unexecuted checks explicitly; never invent passing tests, database results, or human approval.
- Test selection and execution records follow [the testing guide](../../docs/test/README.md) and [verification conventions](conventions.md#code-clarity-and-verification). Generated checklists, a successful build, and absence of review findings do not establish passing behavior.
- Record design decisions and their rationale in architecture, setup changes in the root README and setup guide, and observed checks in the ticket's test report.
- Planning defines automated/operational coverage and pass criteria. Implementation writes and executes required checks and records evidence in `docs/test/<ticket>-test.md`; required failed or blocked checks prevent completion. The orchestrator `test` phase only generates `docs/test/<ticket>-manual-test.md` for human execution, preserving prior records and leaving new cases Not run or Blocked.
- Report changed/output paths, requirement coverage, executed results, unresolved gaps, and next work. Be direct and concise, cite inspected evidence, distinguish uncertainty, and challenge unsupported assumptions respectfully.
