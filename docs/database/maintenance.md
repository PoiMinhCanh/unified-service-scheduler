---
title: "Database Documentation Maintenance"
tags: [database, workflow, tickets]
---

# Keeping database documentation current

Update database documentation in the **same change as the ticket implementation** whenever a ticket adds, alters, renames, or removes a table, column, relationship, constraint, index, procedure, seed contract, or cached data shape. Apply this rule to fixes and follow-up tickets as well as new features.

## Ticket checklist

- [ ] Read [[database/overview|Overview]] and [[database/schema|Schema]] before designing the change.
- [ ] Record the ticket ID, business reason, and before/after data model. During research, mark proposals as planned; do not describe them as deployed schema.
- [ ] Update the schema reference for every affected table and column: physical name, SQL type and length/precision, nullability, default/generated value, purpose, PK/FK, uniqueness, checks, and relevant indexes. Document removals and renames, including their replacement or compatibility behavior.
- [ ] For each relationship, record both tables and columns, cardinality, optionality, FK name, delete/update actions, and cross-table integrity rules. Update the Mermaid diagram in the overview when relationships change.
- [ ] Add the migration ID/path and mapping/procedure references. Document existing-data backfill, seed changes, deployment order, compatibility, and recovery limits in [[database/migration|Migrations]].
- [ ] Review [[database/cache|Cache]] for affected keys, payloads, TTLs, and refresh/versioning behavior; record a reason when there is no cache impact.
- [ ] Run relevant migration, persistence, constraint, relationship, and booking tests. Include boundary/concurrency and runtime-permission checks when their guarantees are affected. Record actual commands and results in the ticket or its verification document; label unexecuted checks as not run.
- [ ] Replace planned details with implemented facts only after inspecting the mappings and migrations. Keep verification status separate from implementation status, and link the evidence.
- [ ] Update [[database|Database index]] if a document is added or removed. Check links and review docs against the migration diff before closing the ticket.

If a ticket has no database impact, record **Database impact: none** with a short reason in its research or review. No unrelated schema rewrite is required.

## Suggested change record

Keep a concise record in the ticket or its research/review document:

```markdown
### Database impact
- Ticket / requirement:
- Status: planned / implemented; verification: passed / failed / not run
- Tables and columns (before -> after):
- Relationships and integrity rules:
- Constraints, indexes, and procedures:
- Migration ID/path and EF mapping references:
- Existing data, seeds, compatibility, and recovery:
- Cache impact (or reason for none):
- Database documents updated:
- Verification commands, observed results, and evidence links:
```

Keep the schema reference as the current model. Keep historical change details in tickets and migration records. Do not invent tables, migration identifiers, code paths, or successful test results to fill gaps.
