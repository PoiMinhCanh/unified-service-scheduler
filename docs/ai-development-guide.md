---
title: "AI development setup and usage"
tags: [ai, mcp, setup, workflow, service-scheduler]
---

# AI development setup and usage

This guide explains how we configure AI tools and work from a ticket in this repository. Our current configuration targets Claude Code: SQL Server access through MCP Toolbox, code navigation through Serena MCP, and repository skills that coordinate ticket delivery. Application setup remains in [[setup-guide|Local setup]]; the detailed phase contracts live in [[orchestrator-ticket-flow|Scheduler ticket workflow]].

## How the pieces fit together

| Component | Role | Repository configuration |
| --- | --- | --- |
| AI client | Reads the ticket, follows project instructions, and calls tools | [CLAUDE.md](../CLAUDE.md), `.claude/rules/`, `.claude/settings.json` |
| SQL Server MCP | Inspects the local project database through MCP Toolbox | `.mcp.json`, server `sqlserver` |
| Serena MCP | Retrieves C# symbols, references, and related code | `.mcp.json`, server `oraios/serena` |
| Ticket orchestrator | Coordinates research, verification, planning, implementation, review, and manual checklist generation | [.claude/skills/orchestrator-ticket/SKILL.md](../.claude/skills/orchestrator-ticket/SKILL.md) |
| Persistent documentation | Carries requirements, findings, decisions, and execution evidence between phases and sessions | `docs/tickets/`, `docs/research/`, `docs/plans/`, `docs/test/`, `docs/review/` |

The workflow takes inspiration from HumanLayer's [Getting AI to Work in Complex Codebases](https://github.com/humanlayer/advanced-context-engineering-for-coding-agents/blob/main/ace-fca.md): research before planning, plan before implementation, and preserve focused context in documents. Our repository adds explicit research and plan verification gates and requirement-linked test evidence. The repository skill and rules define our actual commands and behavior.

## Set up the tools

### 1. Prepare the application and AI client

Follow [[setup-guide]] to install the pinned .NET SDK, start Docker and SQL Server, and initialize the synthetic project database. Install Claude Code separately and open it from the repository root so it can discover `CLAUDE.md`, `.claude/skills/`, and `.mcp.json`. The AI tools are optional development dependencies; the API does not need them to run.

Install Git and [uv](https://docs.astral.sh/uv/getting-started/installation/) for the Serena launch configuration. Check the executables in the same environment that launches the client:

```powershell
git --version
uvx --version
dotnet --list-sdks
docker compose ps
```

### 2. Configure SQL Server MCP Toolbox

Download the Windows executable matching your machine from [MCP Toolbox v1.8.0](https://github.com/googleapis/mcp-toolbox/releases/tag/v1.8.0). The checked-in configuration expects `C:\toolbox\toolbox.exe`; place the binary there or adjust the client configuration to its installed location.

The `sqlserver` entry in `.mcp.json` launches Toolbox directly with:

```powershell
& 'C:\toolbox\toolbox.exe' --prebuilt mssql --stdio
```

The client normally starts this process. A manual stdio launch waits for MCP input; it is not an HTTP service or an interactive SQL prompt. Check the binary independently with `& 'C:\toolbox\toolbox.exe' --version`.

Configure these environment values for the MCP server:

| Variable | Local project value |
| --- | --- |
| `MSSQL_HOST` | `127.0.0.1` for the host connecting to Compose SQL Server |
| `MSSQL_PORT` | `1433`, unless you changed the published port |
| `MSSQL_DATABASE` | `ServiceScheduler` |
| `MSSQL_USER` | A restricted project inspection identity; the current `scheduler_app` identity has SELECT access on `dbo` |
| `MSSQL_PASSWORD` | The matching local password |

The entry selects `127.0.0.1:1433`, database `ServiceScheduler`, and user `scheduler_app`, with the matching local runtime password configured directly as `MSSQL_PASSWORD`. The client passes these values to Toolbox without a launcher script. Update that password if the local SQL login password changes. This configuration contains a local credential; remove it before sharing or committing the file. Compose and the MCP client use separate configuration sources; the client does not need to load `.env` for this direct setup.

Use synthetic local data and restricted reads for investigation. Use the application's privileged setup path for migrations and seeds, following [[database/migration|Migration guidance]]. A configured connection does not authorize unrelated database access. The MCP connection is separate from the API's setup/runtime connection strings.

### 3. Configure Serena MCP

The [Serena project](https://github.com/oraios/serena) provides semantic code retrieval over MCP. Our `.mcp.json` uses a local Serena checkout at `D:\serena`, Python 3.13 through `uvx`, the `claude-code` context, and an absolute project path. For a matching installation:

```powershell
# Clone once, choosing an appropriate local tools directory.
git clone https://github.com/oraios/serena D:\serena

# This is the launch command used by the client; adapt both paths.
uvx -p 3.13 --from D:\serena serena start-mcp-server --context claude-code --project D:\Keyloop\unified-service-scheduler
```

Update `--from` and `--project` in your client configuration for your machine. The local checkout is not pinned by `.mcp.json`; record the Serena revision when troubleshooting version differences. Follow the upstream setup instructions for that revision, including C# language-server dependencies. Serena's local project configuration lives under `.serena/`, which is ignored by Git; ensure it selects the C# language backend for this solution.

Project policy uses Serena for read/search operations and project activation. Use the client's native editing tools for changes. [.claude/rules/code-tools.md](../.claude/rules/code-tools.md) defines search priority and editing boundaries; `.claude/settings.json` denies Serena mutation and shell tools. Those client permissions must be preserved if adapting the setup to another client.

### 4. Check connectivity before starting a ticket

Restart or reconnect the AI client after changing MCP configuration. Confirm both `sqlserver` and `oraios/serena` connect in the client's MCP status view. Then ask it to:

1. Confirm the active Serena project is this checkout and retrieve the symbols from an existing C# file.
2. Use SQL Server MCP to run `SELECT DB_NAME() AS DatabaseName;` and confirm `ServiceScheduler`.
3. Inspect a small amount of schema metadata using the restricted identity, without modifying data.

These are setup checks to perform on your machine, not a record that connections were verified by this document. Never include passwords or full secret-bearing configuration in screenshots, prompts, or committed evidence.

## Start work from a ticket

Create a Markdown file directly under **`docs/tickets/<ticket-id> - <title>.md`**. The existing folder is `docs/tickets/` (plural). For example, `docs/tickets/5 - Appointment Cancellation.md` would be a new ticket; it is not an existing feature or ticket in this checkout.

Give the ticket YAML frontmatter with `title`, `tags`, and `ticket`, then define its objective, current baseline, stable requirement IDs, acceptance criteria, exclusions, assumptions, and verification cases. Link [[architecture]] and relevant database documents rather than copying their contents. Keep ticket numbers unique so the orchestrator can resolve an exact match.

For a selected work item, run each command in the AI client's conversation, inspecting the output before continuing:

```text
/orchestrator-ticket research <id>
/orchestrator-ticket verify research <id>
/orchestrator-ticket plan <id>
/orchestrator-ticket verify plan <id>
/orchestrator-ticket implement <id>
/orchestrator-ticket review <id>
/orchestrator-ticket test <id>
```

Replace `<id>` with the actual work-item ID. `review` is optional. These slash commands are repository skills for a compatible client, not PowerShell commands. Other clients need equivalent instruction/skill configuration; `.mcp.json` alone does not install the orchestrator.

| Phase | Result to inspect |
| --- | --- |
| Research | Current-code evidence, requirement coverage, proposed changes, assumptions, and gaps |
| Verify research | Recorded coverage/traceability check against the current ticket and research inputs |
| Plan | Ordered changes, dependencies, test coverage, commands, and pass criteria |
| Verify plan | Recorded requirement mapping and executable verification criteria |
| Implement | Code, tests, affected documentation, and executed automated/operational evidence |
| Review | Requirement and correctness findings, with outstanding issues recorded |
| Test | Manual checklist for human execution; generating it does not execute cases |

A command authorizes its selected phase. The orchestrator stops at the earliest missing, failed, blocked, stale, or unrecorded prerequisite and supplies the command needed to resolve it. Existing documents alone do not prove that a verification gate passed. Changes to ticket, research, or plan inputs invalidate affected verification. An explicit request for the whole workflow permits proceeding in order while gates pass; see [[orchestrator-ticket-flow]] for the full contract and phase selectors.

Implementation owns automated tests and operational checks, including recording failures and rerunning affected checks after fixes. The final `test` phase only generates manual instructions. Human testers execute those cases and record results separately.

## Find outputs and resume work

| Record | Location |
| --- | --- |
| Ticket | `docs/tickets/<id> - <title>.md` |
| Research | `docs/research/<id>-research.md` or `docs/research/<id>/00-overview.md` with linked parts |
| Plan | `docs/plans/<id>/plan.md` or `docs/plans/<id>/README.md` with phase files |
| Execution evidence | `docs/test/<id>-test.md` |
| Review findings | `docs/review/<id>-review.md` |
| Generated manual checklist | `docs/test/<id>-manual-test.md` |

Keep one primary research entry point. When resuming in a fresh session, supply the ticket ID, primary research and plan paths, selected phase, verification status, and unresolved gaps. Ask the agent to reread those records against the current checkout. This keeps context focused while preserving decisions and evidence outside the chat history.

Naming and maintenance details are in [[research/README]], [[plans/README]], [[test/README]], and [[review/README]].

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Toolbox fails to start | Binary location, Windows architecture, version, and configured arguments |
| SQL connection fails | SQL readiness, published port, `127.0.0.1`, database initialization, and placeholder/overridden environment values |
| SQL access is denied | Required read permissions on the intended database; preserve the restricted identity boundary |
| Serena fails to start | `uvx` availability in the client environment, Python 3.13, local checkout path, and upstream dependency setup |
| Serena cannot find project symbols | Correct absolute project path, C# configuration, language-server startup, and actual source files |
| Skill is unavailable | Client support for repository skills, repository-root working directory, and the files under `.claude/skills/` |
| A phase refuses to continue | Recorded prerequisite status and freshness; run the exact prerequisite command reported by the orchestrator |

This guide changes no application schema, migrations, runtime permissions, or cache behavior.
