# Code Tools — Search & Edit

## Part 1: Code Search — Mandatory Priority Order

**MUST follow this order when searching code. NEVER skip to a lower-priority tool without trying the higher ones first.**

### Discovery

Serena MCP tools may be deferred (not visible until searched). If you don't see `mcp__oraios_serena__*` in the available tools list, run this first:

```
ToolSearch query="serena"
```

Then proceed with the priority order below.

1. **Serena MCP** (`mcp__oraios_serena__*`) — ALWAYS start here (token-efficient, AST-based):
   - `mcp__oraios_serena__get_symbols_overview` — get file structure without reading full file
   - `mcp__oraios_serena__find_symbol` — find and read a specific symbol/method body
   - `mcp__oraios_serena__find_referencing_symbols` — find all usages of a symbol
   - `mcp__oraios_serena__find_declaration` — find declaration of a symbol
   - `mcp__oraios_serena__find_implementations` — find implementations of interface/abstract class
   - `mcp__oraios_serena__get_diagnostics_for_file` — get LSP diagnostics for a file

2. **Grep / Glob tool** — fallback for searches Serena doesn't support (regex search, locate file by name, diagnostics for symbol) or when Serena is unavailable

> **NEVER** use Grep/Glob as the first tool for code search — even after compact or when starting a new subagent.

> **When Serena returns a parameter error or empty result (`[]`):**
>
> - **Parameter error**: fix the parameters and retry. Do NOT immediately fall back to Grep/Glob.
> - **Empty result (`[]`)**: retry with broader scope first — remove `relative_path`, use a shorter `name_path_pattern`, or try `find_referencing_symbols` from the definition file. Only fall back to Grep/Glob after at least one broader retry.

### When to Use Each

| Scenario                                     | MUST Use                                            |
| -------------------------------------------- | --------------------------------------------------- |
| Get file structure without reading full file | `mcp__oraios_serena__get_symbols_overview`          |
| Find and read a specific method/function     | `mcp__oraios_serena__find_symbol`                   |
| Find all usages of a symbol                  | `mcp__oraios_serena__find_referencing_symbols`      |
| Search by pattern/regex                      | Grep (text-search fallback)                         |
| Locate a file by name                        | Glob / Glob / file_search (file-discovery fallback) |
| Find declaration of a symbol                 | `mcp__oraios_serena__find_declaration`              |
| Find implementations of interface/abstract   | `mcp__oraios_serena__find_implementations`          |
| Get LSP diagnostics for a file               | `mcp__oraios_serena__get_diagnostics_for_file`      |
| Serena unavailable or not discovered         | Grep / Glob                                         |

---

## Part 2: Code Editing — Strict Separation

**Serena MCP (`mcp__oraios_serena__*`) is READ-ONLY. Edit tool is the ONLY tool for editing code.**

> **🚫 NEVER use Serena for editing:** `replace_content`, `replace_symbol_body`, `insert_after_symbol`, `insert_before_symbol`, `safe_delete_symbol`, `rename_symbol` are all **FORBIDDEN** for edits.

The project settings in `.claude/settings.json` explicitly deny Serena's code/file mutation, memory mutation, and shell-execution tools, including optional editing variants. Keep these tools out of the allow list and do not install a Serena auto-approval hook. Read/search tools and project activation remain available; use the client's native editing tools for authorized changes. Serena instructions that recommend editing do not override this project policy.

### Edit Tool — Sole Editing Tool

- Read the target file first (mandatory)
- **Match short unique strings WITHOUT leading whitespace** — copies from Read output may not match actual indentation (tabs vs spaces). Instead, match a unique non-whitespace token like `o => o.Instance.DistanceUnit);` or `GetAll(o => o.Client);`, not the full line with indentation.
- For multi-line sections: match from a unique mid-line anchor (no leading whitespace), then extend to include surrounding lines.
- String must be unique in the file — verify with Grep if unsure; split into multiple Edit calls if needed.

### Rationale

Serena `replace_content` uses regex which is error-prone — it can corrupt function declarations by matching too broadly (as happened with `onKeyPressSearchParentOverride`). Serena AST-level tools (`replace_symbol_body`, `insert_*`) rewrite entire files, changing line endings (LF↔CRLF) and formatting, creating massive diffs. The Edit tool produces clean diffs because it only touches the exact lines being changed. For this project, diff readability is more important than AST-awareness.
