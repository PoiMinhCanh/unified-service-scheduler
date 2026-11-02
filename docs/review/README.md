---
title: "Ticket Review Guide"
tags: [review, workflow, tickets]
---

# Ticket reviews

Use `docs/review/` for reviews of ticket changes: whether the implementation meets requirements, which concrete problems remain, and what verification supports the conclusion. Review findings and test results are separate evidence; a review without findings does not prove that all tests passed.

## File naming

Use `docs/review/<ticket-number>-review.md` for a ticket review. For example, ticket 1 uses `docs/review/1-review.md` when a review is performed.

Tickets live directly in `docs/tickets/`. Store their reviews directly in `docs/review/` using the per-ticket filename above.

Keep one primary review report per ticket and record follow-up reviews in that file. Broader assessments may use descriptive filenames; an assessment does not establish that a ticket's implementation has been reviewed or completed.

## Review contents

- **Scope and baseline:** link the ticket, research, and plan; identify the reviewed revision or diff, affected components, and exclusions.
- **Requirement coverage:** compare the change with acceptance criteria and requirement IDs. Record missing behavior, unintended scope, and relevant documentation gaps.
- **Findings:** describe actionable problems with severity, file/symbol or line references, the triggering scenario, expected versus actual behavior, impact, and a proposed correction when useful.
- **Verification:** record executed commands and observed results, distinguish existing evidence from checks performed during review, and label unexecuted checks as not run.
- **Outcome and follow-up:** state remaining findings, limitations, and what must be corrected or verified. Record re-review evidence when fixes are made.

Check applicable API contracts, validation, data integrity, persistence, concurrency, cache behavior, configuration, observability, and tests. For booking changes, examine qualification and resource eligibility, time boundaries, full-duration availability, transaction locking, and runtime permissions. Apply checks according to the ticket's actual scope.

For data changes, check compliance with [[database/maintenance|Database maintenance guideline]]. Check required deployment instructions against [[deploy-notes/README|Deployment notes guide]]. Use [[research/README|Research guide]] and [[plans/README|Plans guide]] to locate the ticket's supporting documents.

## Findings and evidence

| Severity | Meaning |
| --- | --- |
| Critical | A demonstrated problem with severe impact, such as data loss or a major integrity failure |
| High | A significant correctness or reliability problem affecting required behavior |
| Medium | A meaningful defect with narrower impact or specific triggering conditions |
| Low | A minor issue with limited practical impact |

Assign severity based on the actual scenario and impact. Keep optional suggestions separate from defects. Do not invent findings to fill the report; if none are found, say so and describe the limits of the review.

Use stable finding IDs such as `R-01`. Track each finding as open, fixed pending verification, verified resolved, or deferred with a reason. A proposed fix is not a resolved finding; cite the correction and relevant verification before marking it resolved.

For the new project, distinguish planned design from implemented behavior. Do not invent files, line references, database results, or passing tests. Record assumptions and uncertainties explicitly, and keep credentials and personal data out of evidence.

Use Obsidian-compatible Markdown with `title` and `tags` frontmatter and internal wiki links. Keep each paragraph and list item on one line; preserve tables, diagrams, and code blocks.

## Review template

```markdown
---
title: "<ticket-number> - Review"
tags: [review, service-scheduler]
ticket: <ticket-number>
status: draft
---

# <Ticket number> - Review

## Scope and baseline

- Ticket: [[<ticket filename without extension>]]
- Research / plan:
- Reviewed revision or diff:
- Scope, exclusions, and assumptions:

## Findings

### R-01 - <finding title>

- Severity / status:
- Requirement ID and file / symbol / line:
- Triggering scenario and evidence:
- Expected versus actual behavior:
- Impact and recommended correction:

## Requirement coverage

| Requirement ID | Evidence | Gap / remaining check |
| --- | --- | --- |

## Verification

| Command / check | Evidence source / revision | Observed result | Limitation |
| --- | --- | --- | --- |

## Outcome and follow-up

- Review conclusion and remaining risks:
- Required corrections or checks:
- Re-review revision, finding IDs, and resolution evidence:
```

Remove the example finding section when no findings are identified. Fill the report with actual evidence rather than template placeholders.
