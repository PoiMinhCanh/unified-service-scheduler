---
title: "Unified Service Scheduler - Documentation Index"
tags: [documentation, index, service-scheduler]
---

# Documentation index

Start with [README](../README.md) for the application overview and quick start, then use the guides below for setup, design, and development.

## Project guides

| Document | Purpose |
| --- | --- |
| [Project README](../README.md) | Features, API overview, technology, and quick-start commands. |
| [[setup-guide\|Local setup]] | Prerequisites, configuration, initialization, testing, and troubleshooting. |
| [[architecture\|Architecture and system design]] | Components, domain rules, API contracts, booking concurrency, and telemetry. |
| [[database\|Database index]] | Database relationships, schema, migrations, cache, and maintenance. |
| [[database/overview\|Database overview]] | Relationships and booking integrity rules. |
| [[database/schema\|Schema reference]] | Tables, columns, constraints, indexes, and booking procedure. |
| [[database/migration\|Migration guide]] | Migration files, setup commands, compatibility, and recovery. |
| [[database/cache\|Catalog cache]] | Cache keys, payload, expiry, failure behavior, and invalidation. |

## Development workflow and records

| Document or folder | Purpose |
| --- | --- |
| [[ai-development-guide\|AI development setup and usage]] | Optional Claude Code, SQL Server Toolbox, Serena, and workflow setup. |
| [[orchestrator-ticket-flow\|Scheduler development workflow]] | Research, planning, implementation, review, and verification process. |
| `docs/tickets/` | Feature requirements and acceptance criteria. |
| [[research/README\|Research guide]] | Research conventions and supporting evidence. |
| [[plans/README\|Planning guide]] | Implementation plans, dependencies, and phase records. |
| [[test/README\|Testing guide]] | Test reports, manual checklists, commands, and observed results. |
| [[review/README\|Review guide]] | Findings, corrections, and review records. |
| [[deploy-notes/README\|Deployment notes]] | Change-specific operational actions and execution records. |
| [[database/maintenance\|Database maintenance]] | Documentation requirements for data and cache changes. |
| [[assets/README\|Assets guide]] | Supporting images, diagrams, and PDFs. |
| [[templates/research-template\|Research template]] | Reusable structure for technical research. |

Shared guides describe the current project. Requirements, implementation history, verification results, and acceptance status belong in their dedicated development records. Active repository instructions are in [CLAUDE.md](../CLAUDE.md).

## Maintaining navigation

Open `docs/` as the Obsidian vault. Folder-qualified wiki links start at that vault root, such as `[[database/schema|Schema reference]]`; repository paths in prose and commands retain the `docs/` prefix. Escape the alias separator as `\|` inside Markdown tables.

Update this index when documents are added, renamed, or removed, and check local link targets after changes. Keep shared references focused on how the application works and how to use it.
