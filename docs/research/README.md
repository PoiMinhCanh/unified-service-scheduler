---
title: "Ticket Research Guide"
tags: [research, workflow, tickets]
---

# Ticket research

Use `docs/research/` to record what a ticket requires, what already exists, which parts of the application are affected, and what must change before writing an implementation plan. Research captures evidence, assumptions, risks, and unresolved decisions; it does not prove that implementation or testing is complete.

## File naming

| Structure       | Path                                                                                      | Purpose                                                                  |
| --------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| Single document | `docs/research/<ticket-number>-research.md`                                               | Ticket requirements, findings, impact analysis, and proposed changes     |
| Multiple parts  | `docs/research/<ticket-number>/00-overview.md` and `01-<topic>.md`, `02-<topic>.md`, etc. | Overview plus focused research for separate concerns in a complex ticket |

For example, ticket 1 can use `docs/research/1-research.md`. If research needs multiple parts, use `docs/research/1/00-overview.md` and numbered topic files instead. These are naming examples, not existing research or completed findings.

Tickets live directly in `docs/tickets/`. Keep one primary research entry point per ticket using the paths above; when splitting an existing document, update links to the overview.

## Research contents

Follow [[templates/research-template|Research Protocol]] for the detailed phase structure. Assess component applicability for each ticket; mark missing implementation and proposed additions explicitly, and mark irrelevant components as not applicable with a reason. Template examples are not evidence of existing functionality.

- **Scope lock:** link the ticket, extract requirement IDs and acceptance criteria, and state exclusions and assumptions.
- **Architecture and layer coverage:** identify affected API, scheduling logic, persistence, migration, cache, configuration, observability, and test components. Mark irrelevant layers as not applicable with a reason.
- **Data-flow trace:** follow relevant read and write paths, including validation, database operations, cached values, and response mapping. Describe proposed flows as planned when code does not exist yet.
- **UI state analysis:** include only when the ticket introduces or changes a UI; explain when not applicable to the backend release.
- **Pre-audit checks and code audit:** inspect actual file contents and call paths before classifying files as Must Change, Need Review, or No Change. Include evidence and trace changes to requirements.
- **Gap analysis:** separate confirmed facts, assumptions, proposed decisions, and open questions. Explain risks and what would resolve each uncertainty.
- **Change proposal and scope verification:** outline dependency-ordered changes and verification scenarios, then check that every proposed change supports a ticket requirement.

For multipart research, the overview owns shared scope, the part index, dependencies, cross-part handoffs, and consolidated decisions. Each part covers one concern and links its findings back to the relevant requirements.

## Evidence and maintenance

Read the ticket first, then relevant project documentation and implementation. Cite actual files, symbols, and line numbers when inspected. For a new project, explicitly record missing code or migrations; do not invent paths, query results, or test outcomes.

Link supporting images or PDFs according to [[assets/README|Assets guide]] when available. A ticket does not need attachments to be researched.

Use [[database/overview|Database overview]] and [[database/schema|Schema reference]] for data context, noting their planned or implemented status. For proposed data changes, follow [[database/maintenance|Database maintenance guideline]].

Update findings when requirements or implementation evidence changes. Keep proposed behavior separate from current behavior, and record executed checks with their observed results. When research is ready, link it from the ticket's plan using [[plans/README|Implementation plans guide]]. Link deployment notes when deployment-specific actions are identified.

Use Obsidian-compatible Markdown with `title` and `tags` frontmatter and internal wiki links. Keep each paragraph and list item on one line; preserve tables, diagrams, and code blocks.

## Document header template

Use this header above the applicable Research Protocol v2 sections:

```markdown
---
title: "<ticket-number> - <research title>"
tags: [research, service-scheduler]
ticket: <ticket-number>
status: draft
---

# <Ticket number> - <research title>

- Ticket: [[<ticket filename without extension>]]
- Goal and scope:
- Requirement IDs:
- Evidence baseline: <revision or inspected files, when available>
- Assumptions and open decisions:
- Related plan: <link when created>
```
