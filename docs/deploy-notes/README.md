---
title: "Deployment Notes Guide"
tags: [deploy-notes, workflow, tickets]
---

# Deployment notes

Store deployment instructions here when a ticket needs deployment-specific steps, such as database migrations, data backfills, configuration changes, cache invalidation, service updates, or special verification and recovery steps.

Use **one file per ticket**, named `<ticket-number>-deploy-notes.md`. For example, ticket 1 uses `docs/deploy-notes/1-deploy-notes.md`. Create the file only when the ticket needs deployment notes; a ticket with no deployment-specific actions does not need one.

## Maintaining notes

- Create or update the notes in the same change as the implementation, once deployment requirements are known. Keep planned steps separate from executed results.
- Link the ticket, relevant implementation or migration files, and related database documentation. Use actual paths and commands; mark unresolved details as pending.
- Include prerequisites, dependency order, migrations/backfills, configuration, cache impact, smoke checks, and recovery steps. Use `Not applicable` with a reason for sections that do not apply.
- Record configuration variable names and where values are supplied; keep credentials and other secrets out of the notes.
- Read the notes before deployment. After deployment, record the target environment, deployed revision, execution date, and observed verification results. Never mark an unexecuted check as passed.
- For schema changes, also follow [[database/maintenance|Database maintenance guideline]].

## Per-ticket template

Copy this template into `<ticket-number>-deploy-notes.md` and replace the placeholders. The filename example does not mean ticket 1 has deployment notes or has been deployed.

```markdown
---
title: "<ticket-number> - Deployment Notes"
tags: [deploy-notes, service-scheduler]
ticket: <ticket-number>
status: planned
---

# Ticket <ticket-number> - Deployment notes

## Scope and references

- Ticket: [[<ticket filename without extension>]]
- Change summary:
- Implementation revision / artifact:
- Related migration, schema, and cache documentation:

## Prerequisites and deployment order

- Target environment:
- Required services, permissions, and configuration:
- Dependencies on other tickets / deployment order:
- Compatibility, downtime, and backup requirements:

## Deployment steps

1. Database migrations / backfills: list actual migration IDs, commands, execution identity, and order; or explain why not applicable.
2. Application / configuration: record the artifact, commands, and configuration changes.
3. Cache: list affected keys, invalidation or version changes, and timing; or explain why not applicable.

## Post-deployment verification

- [ ] Confirm expected application and migration versions.
- [ ] Run ticket-specific smoke checks and record expected versus observed results.
- [ ] Check affected persistence, health endpoints, logs, metrics, and traces as applicable.

## Rollback or forward recovery

- Failure conditions that trigger recovery:
- Application / configuration recovery steps:
- Database compatibility, data-loss limits, and restore or forward-fix steps:
- Cache recovery and verification:

## Execution record

| Environment | Date / time and zone | Revision | Actions and observed results | Evidence / follow-up |
| --- | --- | --- | --- | --- |

No deployment executed yet. Replace this statement with actual results after execution.
```
