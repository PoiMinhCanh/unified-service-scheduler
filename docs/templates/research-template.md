---
title: "Scheduler Research Protocol"
tags: [research, template, workflow, service-scheduler]
---

# Research Protocol — <ticket-number> <title>

> [!NOTE]
> Use this template for any scheduler ticket. Read the actual ticket, [project scope](../../README.md), [[architecture]], and relevant domain documentation first. Follow [[research/README|Research guide]] for naming and [[orchestrator-ticket-flow|Scheduler ticket workflow]] for execution. Examples below are illustrative, not inspected implementation or passing evidence.

- Ticket: [[<ticket filename without extension>]]
- Goal and scope:
- Requirement IDs, exclusions, and assumptions:
- Evidence baseline: <revision and inspected files>
- Related plan: <link when created>

## Current Architecture (Entry Points)

Describe affected entry points and components. Discover actual paths and symbols in the checkout; distinguish existing behavior from proposed additions. Missing scaffolding is a finding, not a reason to invent files, signatures, or line numbers.

### API and Dependency Flow

```text
HTTP request -> API validation -> scheduling use case
  Reads: EF Core query -> SQL Server -> response DTO
  Catalog reads: Redis hit, or SQL Server fallback -> response DTO
  Booking writes: infrastructure boundary -> locked SQL procedure
    -> revalidate eligibility and conflicts -> insert -> commit -> response DTO
```

Adapt this flow to the ticket. Availability reads are advisory; booking confirmation must use authoritative SQL Server validation. Future mutation paths must preserve the applicable booking invariants.

### Current Files

| Component | Actual file / symbol / lines | Relevant behavior | Evidence / missing implementation |
| --- | --- | --- | --- |
| <API, scheduling, persistence, cache, telemetry, or tests> | <inspected reference> | <current behavior> | <fact or explicit gap> |

### Current Code (Relevant Snippets)

Include only inspected snippets needed to explain the change, such as request validation, an EF query, procedure locking, or runtime grants. Label proposed code separately.

## Phase Gating Rules

| Phase | Required when | Evidence |
| --- | --- | --- |
| 0 — Scope Lock | Always | Ticket requirements, assumptions, and exclusions |
| 1 — Layer Coverage | Always | Applicability and evidence for each component below |
| 2 — Data Flow Trace | Data, API, or business behavior changes | Affected read/write and failure paths |
| 3 — UX State Matrix | UI changes | Relevant user-visible states; otherwise N/A |
| 3b — Pre-Audit Verification Gates | Always for candidate classification | Inspected contents and reachable call paths; explicit gaps for proposed additions |
| 4 — Code Audit | Always | Must Change / Need Review / No Change with requirement and gate evidence |
| 5 — Gap Analysis | Always | Open decisions, risks, and resolution evidence |
| 6 — Change Plan | Always | Dependency-ordered proposals and verification |
| 7 — Scope Verification | Always | Forward and reverse requirement traceability |

Mark an inapplicable section `N/A — <reason>`. For a new component, record `Missing — proposed addition` and describe intended behavior and verification. Do not report unexecuted checks as passed.

## Decomposition Decision (BEFORE Phase 0)

Choose one primary research entry point by concern and readability. Use a single document for a focused change whose dependencies fit together; use multipart research when separate concerns need substantial analysis or cross-part decisions. Cache, mapping, permission, and telemetry impacts require explicit assessment rather than assumed automatic coverage.

| Structure | Output | Responsibility |
| --- | --- | --- |
| Single document | `docs/research/<ticket>-research.md` | All applicable phases |
| Multipart | `docs/research/<ticket>/00-overview.md` plus numbered topic files | Shared scope, part index, dependencies, consolidated findings, and decisions in the overview; applicable analysis in each part |

When splitting existing research, update downstream links to the overview. Do not retain a competing primary document. Multipart research can run directly in the current session; delegate only when authorized and supported.

### Decomposition Strategy

| Concern | Illustrative coverage | Possible handoff |
| --- | --- | --- |
| Booking confirmation | API validation, resource selection, procedure, response mapping | Procedure contract and classified failures |
| Persistence foundation | EF mappings, migrations, constraints, indexes, runtime grants | Data and write-boundary contracts |
| Catalog caching | Query, payload, key scope, TTL, invalidation, outage fallback | Freshness and compatibility policy |
| Observability and verification | Logs, metrics, traces, test fixtures, operational checks | Signals and reproducible evidence |

### Multipart Workflow

1. Run Phase 0 for the whole ticket and create the overview with its part index.
2. Assess component coverage and assign concerns, dependencies, and handoffs.
3. Research independent concerns, then concerns that depend on their findings.
4. Keep each part's evidence, gaps, and proposed changes tied to requirements.
5. Consolidate audits, decisions, dependencies, and Phase 7 traceability in the overview.

## Phase 0: Scope Lock

### 0.1 Ticket Requirements

Preserve requirement IDs and quote the relevant requirement text accurately. Read acceptance criteria, business rules, assumptions, and dependency requirements wherever they appear in the ticket. A particular section heading is not required for a statement to be binding.

| Requirement ID / source section | Requirement text | Acceptance evidence | Relevant assumption |
| --- | --- | --- | --- |
| <ID or section> | <ticket text> | <observable outcome> | <assumption or none> |

### 0.2 Exclusions and Necessary Dependencies

Separate required behavior, necessary implementation dependencies, background context, and explicitly deferred features. Trace each proposed dependency to the requirement it enables. Record ambiguous intent and its behavioral effect rather than silently adding or discarding scope.

| Item / ticket source | Classification | Reason and requirement link |
| --- | --- | --- |
| <item> | <required / necessary dependency / context / excluded> | <evidence> |

### 0.3 Attachments

Inspect relevant images or PDFs with available tools. Record missing or unreadable attachments without guessing their contents. Attachments are optional; follow [[assets/README|Assets guide]].

| Asset / page | Findings | Requirement relevance |
| --- | --- | --- |

## Data Sources for New Fields

For added or changed fields, identify the authoritative source, derivation, validation, persistence, and response/cache mappings. Distinguish client input, server-calculated values, seeded configuration, and persisted snapshots. Consider alternative sources only where there is a real decision to make.

| Field | Source table / column or input | Type / nullability | Derivation and validation | Persistence / response / cache impact |
| --- | --- | --- | --- | --- |
| <field> | <source or proposed addition> | <contract> | <rule> | <affected paths> |

## Phase 1: Layer Coverage Sweep

Assess every component below for applicability. Use `Relevant — <evidence>`, `Missing — proposed addition`, or `N/A — <reason>`. A relevant component may require review without requiring a change. Search actual contents and call paths before assigning changes; paths depend on the implemented checkout.

### 1.1 Component Checklist

| Component | What to inspect | Search / evidence anchors | Status |
| --- | --- | --- | --- |
| API contract and validation | Controller routes, request/response DTOs, explicit timestamp offsets, Problem Details, OpenAPI | Route, DTO field, validation rule, error code | |
| Scheduling rules | Duration, active/qualified resources, ownership, dealership scope, opening/shift coverage, interval boundaries | Requirement IDs, rule functions, TimeProvider | |
| Resource selection and availability | Authoritative bounded SQL queries, stable candidate order, advisory availability | Eligibility predicates, overlap filters, attempt limit | |
| Domain model and EF mappings | Entities, SQL types, required relationships, snapshots, cross-table consistency | DbContext, entity configuration, FK/check definitions | |
| EF Core migrations and seeds | Model snapshot, migration SQL, existing-data compatibility, synthetic seed lifecycle | Actual migration IDs/files, initialization code | |
| Booking procedure and transactions | Technician-then-bay locking, full revalidation, both conflict checks, atomic commit/rollback | Procedure definition, isolation, UPDLOCK/HOLDLOCK | |
| Runtime permissions | Restricted runtime identity, procedure execution, denied direct appointment writes, separate setup identity | Grants or role definitions, connection configuration, permission tests | |
| Failure and retry handling | Capacity versus dependency errors, bounded deadlock/timeout handling, cancellation, unknown commit outcome | SQL error mapping, retry boundary, rollback path | |
| Redis catalog cache | Payload mapping, scoped/versioned keys, TTL, invalidation, bounded SQL fallback | Cache access, serialization, timeout configuration | |
| Configuration and packaging | SDK/package/image pins, configuration contract, Compose, SQL volume, initialization/readiness | Actual project/configuration files, health checks | |
| Telemetry | Structured logs, trace correlation, bounded metric labels, exporters and backend provisioning | Serilog events, Activity/Meter usage, Collector/Grafana configuration | |
| Unit tests | Pure rules, interval/time boundaries, deterministic clock | Actual test files/symbols and requirement coverage | |
| API and dependency integration tests | Real SQL Server/Redis, runtime identity, independent connections, race coordination, final state | WebApplicationFactory, Testcontainers, permission/concurrency assertions | |
| Operational verification | Restart persistence, outages, telemetry wiring, clean-checkout checks | Actual scripts, commands, recorded results | |
| Documentation and delivery | API/design/schema/cache changes, setup, test reports, optional deployment notes | Affected guides and ticket records | |
| UI or new integrations | Assess only if explicitly introduced or changed by the ticket | Actual UI/client or integration contract; otherwise N/A | |

### 1.2 Workflow Change Checklist

| Check | Evidence to collect | Status |
| --- | --- | --- |
| All callers and entry points | Actual routes, signatures, DI registrations, procedure callers | |
| Rules enforced across boundaries | API validation versus authoritative SQL validation and constraints | |
| Read and write consistency | Response mapping, duration snapshots, uncached availability, catalog freshness | |
| Failure paths | Rollback, candidate retries, lock waits, dependency errors, unknown commits | |
| Verification coverage | Requirement-linked tests, deterministic setup, final stored-state assertions | |

## Phase 2: Data Flow Trace

Trace every affected read/write path and its validation, identity, transaction boundary, mapping, and failure behavior. An API precheck alone does not establish database integrity. Include direct procedure callers when booking behavior is affected. Replace illustrative flows with inspected file/symbol references; label future flows as proposed.

### 2.1 READ Paths

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Service catalog | API -> Redis; on miss or recoverable outage -> SQL -> response mapping | Key scope, TTL, payload and bounded fallback | |
| Availability | API -> scheduling query -> SQL resources/shifts/appointments -> response | Authoritative full-duration checks; no reservation | |
| Appointment retrieval | API -> dealership-scoped SQL query -> response DTO | Stored associations and out-of-scope retrieval behavior | |

### 2.2 WRITE Paths

| Path | Flow to inspect | Important boundary | Evidence / gap |
| --- | --- | --- | --- |
| Booking confirmation | API -> validation/selection -> procedure -> locked revalidation -> insert/commit -> response | Restricted runtime identity; atomic technician and bay reservation | |
| Procedure execution without API prechecks | Runtime connection -> booking procedure | Required invariants remain enforced | |
| Migration / synthetic seeding | Privileged initialization -> schema/procedure/grants/seeds | Separate identity; compatibility and cache invalidation | |

### 2.3 Path Coverage Verification

| Affected path / requirement | Value source and rule enforcement | Failure / cleanup behavior | Verification scenario | Observed evidence or Not run |
| --- | --- | --- | --- | --- |

## Phase 3: UX State Matrix

Apply only when the ticket changes a UI, including relevant Swagger interactions. A backend-only change can mark this phase N/A; request/response validation still belongs in the API and data-flow analysis.

| Interaction / field | Initial state | Success state | Invalid input / conflict | Dependency failure | Evidence / planned check |
| --- | --- | --- | --- | --- | --- |

Record defaults, required/optional values, displayed UTC/local times, and error behavior only where relevant. Do not introduce a frontend or authentication workflow through this template.

## Phase 3b: Pre-Audit Verification Gates

> [!IMPORTANT]
> Before classifying existing files, inspect their contents and actual call paths. A filename match or populated evidence column does not prove relevance. For missing components, record proposed additions and validation criteria rather than claiming these gates passed against nonexistent code.

### Gate 1: Candidate Internal Structure Verification

| Candidate file / symbol | Why it matched | Actual relevant contents | Requirement link | Verdict / evidence |
| --- | --- | --- | --- | --- |
| <inspected file> | <route, entity, SQL or configuration match> | <actual behavior> | <requirement> | <Must Change / Need Review / No Change> |

Inspect query predicates, procedure bodies, migration definitions, cache payload mappings, permission grants, and tests as applicable. A shared entity name does not mean every file needs a change.

### Gate 2: Call Path and Boundary Verification

1. Identify each affected entry point and the implementation selected by DI.
2. Trace actual method signatures, overloads, branches, and SQL procedure calls.
3. Record cache-hit, miss/fallback, transaction, retry, and error paths where relevant.
4. Confirm the target code executes on the path under review and identify bypasses.

| Entry point | Actual call chain / signatures | Identity / transaction / branch | Reaches target? | Consequence and evidence |
| --- | --- | --- | --- | --- |
| <route or procedure caller> | <inspected symbols and lines> | <actual boundary> | <yes / no / unresolved> | <impact> |

## Phase 4: Code Audit

Classify existing files using the two gates and trace every proposed change to a requirement or necessary dependency. List new components separately with their planned purpose and verification. Keep uncertain applicability under Need Review.

| Classification | File / symbol / lines, or proposed component | Current behavior / explicit gap | Proposed change or reason for no change | Requirement IDs | Gate evidence / remaining verification |
| --- | --- | --- | --- | --- | --- |
| <Must Change / Need Review / No Change / Proposed Addition> | <reference> | <fact or gap> | <action> | <IDs> | <evidence> |

## Phase 5: Gap Analysis

Use `Gap`, `Risk`, or `Decision` to classify entries, and `Open`, `Resolved`, `Moot`, or `Deferred` for status. Explain decisions, resolution evidence, and any deferred requirement impact. Examples of relevant uncertainties include physical schema choices, retry limits, cache budgets, and test coordination; do not invent settled values.

| ID | Type | Question / risk | Proposed answer / mitigation | Requirement impact and blocking dependency | Status / evidence |
| --- | --- | --- | --- | --- | --- |

## Phase 6: Change Plan

Order changes by dependency and include verification with each step. Coordinate EF mappings/migrations, procedure contracts, and runtime grants. A working booking slice must include its protected write path; tests must not depend on temporary unrestricted writes. Link the detailed plan using [[plans/README|Plans guide]].

| Step | Requirement IDs | Actual or proposed files / components | Change and dependency | Verification / completion criteria |
| --- | --- | --- | --- | --- |

Record database and cache impact using [[database/maintenance|Database maintenance]] and [[database/cache|Cache guidance]], or a reason for none. Include configuration, telemetry, and delivery impacts as applicable. Use [[test/README|Testing guide]] for evidence and [[test/integration-test-template|Integration test template]] for real-dependency checks. Discover actual commands after scaffolding; pending commands are not executable evidence.

## Rollback or Forward Recovery

> [!IMPORTANT]
> Include an explicit recovery procedure for migrations, seed/backfill changes, data transformations, and other persistent effects. Follow [[database/migration|Migration guidance]] and link [[deploy-notes/README|deployment notes]] when operational steps are needed. Otherwise mark this section N/A with a reason.

Choose rollback, forward recovery, or backup restoration based on actual schema/data impact. Document failure triggers, source/target versions, backup prerequisites, execution order, required identity, compatibility, and possible data loss. Keep EF mappings/model snapshot, schema, procedures, and grants coordinated. A down migration or code revert does not restore deleted data; do not delete migration history as recovery.

| Step | Objects / migration IDs | Recovery action and command / working directory | Prerequisites / identity | Compatibility and data-loss limits | Verification / observed evidence |
| --- | --- | --- | --- | --- | --- |

Verify recovery against real SQL Server using isolated synthetic data before describing it as verified. Check schema/migration state, data preservation, application behavior, booking protection, and runtime permissions as applicable. Record revision, dependency versions, commands, and observed results in the ticket test report. Pending steps remain planned or Not run.

## Handoffs to Other Parts

For multipart research, record discoveries or decisions that change another part's assumptions, inputs, or scope. Keep the overview synchronized whether work runs directly or is delegated.

| Source part | Finding / decision and evidence | Target part | Required action | Dependency / status |
| --- | --- | --- | --- | --- |

## Phase 7: Scope Verification

Verify both directions: every ticket requirement has research coverage, and every proposed change traces to a requirement or necessary dependency. Preserve exclusions and documented assumptions. Label unresolved interpretation explicitly; do not reject a requirement because it appears in a note or under a different heading.

### 7.1 Change-to-Requirement Trace

| Change / affected component | Requirement ID / source | Direct requirement or necessary dependency | Research evidence / proposed verification |
| --- | --- | --- | --- |

### 7.2 Scope Checklist

- [ ] Every requirement and acceptance criterion has coverage or an explicit unresolved gap.
- [ ] Each proposed change has a requirement link or an explained necessary dependency.
- [ ] Assumptions, exclusions, and deferred extensions remain explicit.
- [ ] Optional improvements are separated from required work.
- [ ] Existing behavior, proposed additions, and executed verification are distinguished.
- [ ] Database, permissions, cache, telemetry, and test impacts are assessed where applicable.
- [ ] Proposed paths and commands are labelled; no implementation or passing evidence is invented.

### 7.3 Items Flagged for Removal

| Item | Why it falls outside scope | Follow-up, if any |
| --- | --- | --- |

## Dependencies

| Dependency | Requirement / component blocked | Resolution needed | Status / evidence |
| --- | --- | --- | --- |

## Decisions Made

| Decision | Reason / alternatives | Requirement impact | Date / evidence |
| --- | --- | --- | --- |

## Out of Scope

| Item | Ticket exclusion or reason | Follow-up, if any |
| --- | --- | --- |

## Files Referenced

List actual inspected paths/symbols and supporting documents. Keep proposed files separate and include evidence limitations. Update research when requirements or inspected implementation changes.
