# Scheduler debugging workflow

Use [project scope](../../README.md), [the system design](../../docs/architecture.md), and the actual ticket as the baseline. Application projects and executable commands remain pending; inspect the checkout before describing commands or endpoints as available.

## Reproduce and inspect

1. Read the ticket, relevant research, plan, and existing test results. Identify expected behavior, observed failure, revision, and synthetic reproduction data.
2. Discover actual project paths, configuration, and commands from the checkout and [setup guide](../../docs/setup-guide.md). Report missing scaffolding or dependencies as blocked or not run.
3. Reproduce with focused xUnit tests, WebApplicationFactory. Swagger UI and browser inspection supplement automated checks.
4. Inspect Problem Details error codes and trace IDs, Serilog JSON console events, and available OpenTelemetry traces and metrics. Discover telemetry endpoints from implemented configuration.
5. Correct the cause within scope, rerun affected checks, and record commands, observed results, and remaining gaps in the ticket's test report.

Use available HTTP tools for API requests and browser tooling for Swagger UI or Grafana interactions. If a tool is unavailable, continue independent checks with supported tools and report the limitation. A missing browser connection does not block backend verification.

## Database and booking investigation

- Use isolated local SQL Server and synthetic data. Configured MCP servers and inherited environment names do not establish project ownership or authorize database access.
- Inspect EF mappings, migrations, booking procedure SQL, and runtime grants before querying or changing state. Follow [migration guidance](../../docs/database/migration.md) and [database maintenance](../../docs/database/maintenance.md) for schema changes.
- Apply migrations and seeds with the separate privileged setup identity. Exercise API and procedure paths with the restricted runtime identity; do not grant direct appointment writes to bypass a failure.
- Inspect full-duration eligibility, customer/vehicle and dealership consistency, technician-then-bay locking, both overlap checks, commit/rollback, and final stored state.
- Reproduce concurrency and permission failures against real SQL Server using independent connections and coordinated requests. In-memory tests cannot establish these guarantees.
- Distinguish capacity conflicts from deadlocks, lock timeouts, database outages, and unexpected failures. Do not blindly retry a write whose commit outcome is unknown.
- Respect existing access restrictions for any separately authorized shared environment. Local reproduction instructions do not grant permission to modify shared data or schema.

## Cache and telemetry investigation

- Inspect Redis catalog hit, miss, expiry, invalidation, and recoverable-outage fallback. Availability and final booking validation use SQL Server; Redis is not the reservation lock.
- Check bounded cache and telemetry failure behavior with controlled outages in isolated dependencies. Export destinations must not become booking-success dependencies.
- Correlate logs and traces without recording credentials, request bodies, VINs, or customer contact details in reports. Keep metric labels bounded as specified in the design.

Follow [the testing guide](../../docs/test/README.md) and [integration checklist](../../docs/test/integration-test-template.md). Record Passed, Failed, Not run, or Blocked with reasons; generated scenarios are not executed results.
