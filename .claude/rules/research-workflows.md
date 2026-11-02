# Scheduler research workflow

These rules apply to the Unified Service Scheduler. Use the current ticket, [project scope](../../README.md), and [architecture](../../docs/architecture.md) as the baseline. Inherited examples in templates do not establish current classes, configuration, integrations, or requirements.

## Ticket and documentation first

1. Locate and read the actual ticket directly under docs/tickets before researching its implementation. Ticket filenames may include a number and title; do not assume an exact numeric filename.
2. Extract requirement IDs, acceptance criteria, scope, exclusions, assumptions, and any supporting assets. If the ticket cannot be found, report that gap before making requirement-dependent implementation decisions.
3. Read the relevant existing research, plan, architecture, and domain documentation before exploring implementation. Read only the documents relevant to the task; listing docs does not require reading every file.
4. Distinguish documented design from implemented behavior. Where code, migrations, or runtime evidence is absent, record that fact and label proposed paths or behavior as planned.
5. Keep findings and proposed changes tied to the ticket. Record unrelated defects separately rather than expanding implementation scope.

## Evidence and data-flow tracing

- Follow [the code-tool rules](code-tools.md) for implementation inspection. Verify actual file contents before classifying a file as affected, then trace the entry point, exact method signature, selected implementation, and relevant branches.
- Trace reads and writes through API validation, scheduling rules, EF queries/procedure calls, storage constraints, cache payloads, and response mapping as applicable.
- Search by the business concept as well as symbol names: duration, qualification, shift coverage, dealership ownership, overlap, confirmation, and catalog freshness.
- Check whether the same rule is enforced elsewhere, especially the API precheck and authoritative booking procedure. A fix to one path must not leave another path inconsistent.
- Include necessary shared dependencies when they are required for the ticket's behavior; a file does not need to be named in the ticket to be relevant. Conversely, a matching field name alone does not justify a change.
- Cite inspected files/symbols and line numbers where available. Separate observed facts, assumptions, proposed decisions, and unknowns. Never fabricate file paths, database results, or successful test evidence.

## Database and booking impact

For tickets involving missing/incorrect data, scheduling, or persistence, read [the database overview](../../docs/database/overview.md) and [schema reference](../../docs/database/schema.md) before drawing conclusions about relationships or database behavior. Consult [migration guidance](../../docs/database/migration.md) for schema changes.

Check the following when affected by the ticket:

| Concern | Research question |
| --- | --- |
| Ownership and dealership scope | Are the vehicle/customer relationship and service/technician/bay dealership relationships validated and enforced in persistence? |
| Time and duration | Are explicit offsets, UTC storage, local opening hours, controllable current time, positive duration, and snapshots handled consistently? |
| Eligibility and interval coverage | Are both resources active, the technician qualified, and the entire interval within one opening interval and shift? |
| Overlap and concurrency | Are technician and bay conflicts rechecked inside the protected transaction with the shared technician-then-bay lock order? |
| Write boundary | Does the runtime identity execute the procedure while direct appointment writes are denied? |
| Failure handling | Are rollback, resource reuse, bounded retries, timeout/deadlock classification, and unknown commit outcomes handled correctly? |
| Schema evolution | Which tables, columns, relationships, constraints, indexes, procedures, seeds, and runtime grants change? |

Follow [the database maintenance guideline](../../docs/database/maintenance.md) for every data-changing ticket. Schema documents describe a planned model until implementation evidence is recorded. Use only authorized data sources and isolated synthetic test environments for active verification; do not assume inherited database connections apply.

## Cache impact check

Assess cache impact for each ticket and record either concrete changes or a brief reason for no impact. Do not require a cache invalidation action merely because a database object changes.

1. Read [the cache reference](../../docs/database/cache.md). Initially only service catalogs are cached; availability and final booking validation remain authoritative SQL Server operations.
2. Identify affected catalog fields, serialization, dealership scope, seed data, and freshness behavior. Do not assume cache key names or numeric IDs exist before implementation.
3. For an affected cache, document the actual key pattern, payload/version, TTL, source data, invalidation trigger, and deployment timing.
4. For seeded catalog changes, plan affected-key invalidation or a cache namespace version change. Document the TTL freshness bound if invalidation fails.
5. Verify bounded Redis connectivity/timeout fallback to SQL Server and the relevant degradation signals. Keep programming errors distinct from recoverable cache outages.
6. Update the cache reference and the ticket's deployment notes when actions are required. Do not add database cache-registry entries or reuse previous-project refresh dashboards.

The ticket's planned key example is scheduler:v1:dealership:{id}:service-types with an initial configurable absolute TTL of ten minutes. Treat it as a proposed contract until code and configuration implement it.

## Tests and operational impact

Read [the testing guide](../../docs/test/README.md), the ticket's verification matrix, and existing reports before proposing tests. Locate actual executable tests by requirement, component, and scenario.

Use [the backend template](../../docs/test/backend-test-template.md) for scenario and execution records and [the integration template](../../docs/test/integration-test-template.md) for dependency-specific checks. SQL Server locking, procedures, and permissions require real SQL Server; in-memory providers and SQLite do not prove them.

Record expected behavior separately from observed results. A checklist or generated test file is not proof of execution. Include relevant regression/boundary checks, final persisted state, and the revision/environment for actual runs. Mark blocked or unexecuted checks explicitly.

When a ticket needs migrations, backfills, cache invalidation, configuration changes, or other deployment-specific actions, follow [the deployment guide](../../docs/deploy-notes/README.md). Write one docs/deploy-notes/<ticket-number>-deploy-notes.md file per ticket that needs it, not a shared checklist for multiple tickets. Do not invent commands before scaffolding exists.

## Research phases and applicability

Use [the research template](../../docs/templates/research-template.md) for the phase structure and [the research folder guide](../../docs/research/README.md) for naming and multipart organization. Apply the phases to the scheduler's actual components; old UI/layer examples are not mandatory architecture.

| Phase | Required coverage |
| --- | --- |
| 0: Scope lock | Exact ticket requirements, acceptance criteria, exclusions, assumptions, and supporting assets |
| 1: Layer coverage | Relevant API, scheduling, persistence, migrations, cache, configuration, observability, and test components; mark irrelevant layers with a reason |
| 2: Data-flow trace | Required for changed fields or behavior crossing data paths; identify reads, writes, authoritative state, and cached copies |
| 3: UI state analysis | Required only when a ticket actually introduces or changes a UI; explain why it is not applicable to the initial backend-only release |
| 3b: Pre-audit gates | Inspect candidate contents and trace actual signatures/implementations before the code audit; record absent code rather than inventing a call chain |
| 4: Code audit | Must Change / Need Review / No Change, with evidence and requirement traceability |
| 5: Gap analysis | Unknowns, proposed answers, rationale, impact, and decisions requiring clarification |
| 6: Change proposal | Dependency-ordered changes and verification scenarios; the detailed implementation plan follows the plans guide |
| 7: Scope verification | Every proposed change supports a requirement or necessary dependency; flag unrelated work separately |

Complete scope, coverage, evidence checks, audit, gap analysis, change proposal, and scope verification for ticket research. Mark conditional phases as not applicable with a reason instead of inventing a workflow.

## Supporting assets and output

Read referenced images and relevant PDF pages when they support the requirements. Follow [the assets guide](../../docs/assets/README.md), resolve paths against the actual ticket/document, and record missing or unreadable attachments without guessing their contents. Attachments are optional; do not require an image reader for a text-only ticket.

Save research using the folder guide: docs/research/<ticket-number>-research.md, or docs/research/<ticket-number>/00-overview.md and numbered topic files for multipart work. Keep one primary research entry point and link its parts.

Use frontmatter with title/tags, internal links, evidence tables where useful, and concise prose for decisions and reasoning. Keep each paragraph and list item on one line. Include requirement traceability for every Must Change item and clearly identify unresolved decisions; do not disguise assumptions as verified facts.

When research or implementation decisions change, update related plans and documents in the same ticket change. See [the plans guide](../../docs/plans/README.md) and [review guide](../../docs/review/README.md) for downstream records.
