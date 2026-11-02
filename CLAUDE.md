# Unified Service Scheduler

Automotive service-booking backend. Use [project scope](README.md), [architecture](docs/architecture.md), and the actual ticket as the baseline.

The API, persistence, discovery and availability, appointment booking and retrieval, and observability are implemented and locally verified. Human execution of the generated manual checklists and visual Grafana inspection remain pending. Inspect the checkout and recorded test evidence before making implementation claims. Setup and verified commands belong in [README.md](README.md) and [the setup guide](docs/setup-guide.md).

## Shared rules

The active rules in `.claude/rules/` apply throughout project work. Keep detailed policy in its owning rule or domain guide; skills and reviewers should link to it.

| Concern | Source of truth |
| --- | --- |
| Ticket discovery, research prerequisites, documentation format, evidence, and handoff | [Ticket workflow](.claude/rules/ticket-workflow.md) |
| Search priority and editing boundaries | [Code tools](.claude/rules/code-tools.md) |
| Application structure, persistence, booking integrity, time, cache, telemetry, and verification | [Coding conventions](.claude/rules/conventions.md) |
| Demo scope, identities, credentials, and dependency access | [Access and integrations](.claude/rules/auth-integrations.md) |
| Research protocol, data-flow tracing, and impact analysis | [Research workflow](.claude/rules/research-workflows.md) |
| Reproduction and investigation | [Debugging workflow](.claude/rules/debugging-workflow.md) |

## Task entry points

- Research a ticket: [research-ticket](.claude/skills/research-ticket/SKILL.md).
- Coordinate ticket phases: [orchestrator-ticket](.claude/skills/orchestrator-ticket/SKILL.md).
- Review changes: [clean-code-reviewer](.claude/agents/clean-code-reviewer.md).
- Create a supplementary manual checklist: [create-manual-test](.claude/skills/create-manual-test/SKILL.md).

Use [the documentation index](docs/README.md) to find domain references, templates, and record conventions. Read the guides relevant to the task rather than copying their rules into phase instructions.
