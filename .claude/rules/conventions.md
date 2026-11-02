# Scheduler coding conventions

Use [project scope](../../README.md) and [the system design](../../docs/architecture.md) as the baseline. These rules describe the selected design; they do not claim application code, migrations, or verified commands already exist.

## Application structure and dependency injection

- Use one ASP.NET Core API with feature-oriented scheduling use cases and a small infrastructure boundary.
- Use constructor injection and ASP.NET Core's built-in dependency injection. Register application dependencies in the implemented composition root; record actual registration paths once scaffolding exists.
- Keep HTTP controllers focused on request/response handling. Keep scheduling rules separate from HTTP, provider-specific transactions, and database error mapping.
- Use EF Core DbContext directly for reads and invoke the booking procedure through infrastructure. Do not require a generic repository or unit-of-work abstraction.
- Split responsibilities when a component becomes difficult to understand or test. Introduce abstractions and shared helpers when actual behavior justifies them, rather than adding layers by default.

## Asynchronous I/O and error handling

- Use asynchronous database/cache operations and propagate cancellation tokens through supported boundaries. Do not block asynchronous work with Result, Wait, or Thread.Sleep.
- Treat cancellation separately from application faults. Do not use Task.Run as a substitute for asynchronous I/O.
- Map validation, capacity conflicts, dependency failures, and unexpected faults to the ticket's explicit API contracts using consistent Problem Details responses.
- Classify database errors narrowly. Do not convert every SQL or persistence exception into a booking conflict.
- Never use empty catch blocks. Catch recoverable dependency errors at the boundary that can handle them; preserve unexpected failures for controlled error handling.
- Use the selected Serilog structured logging pipeline, with appropriate request/business events and trace correlation. Avoid duplicate exception logging at every layer and avoid exposing stack traces or configuration in API responses.
- Do not introduce a background-job system for the synchronous booking workflow. Future background work requires a scoped requirement and a documented execution model.

## Persistence and migrations

- Use EF Core 10 with the SQL Server provider and SQL Server 2022 as selected in the ticket. Pin compatible package and container versions during scaffolding.
- Use parameterized SQL and provider-supported parameter binding for procedure calls. Never concatenate untrusted values into SQL.
- Use AsNoTracking for read-only entity queries when tracking is unnecessary. Keep dealership, resource, time-window, and overlap filtering in bounded database queries; do not load the full appointment table for in-memory filtering.
- Use EF migrations and the model snapshot together. Include procedure SQL, keys, checks, indexes, and grants through migrations where required.
- Review generated SQL and document existing-data compatibility, backfill, deployment order, and recovery limits. Do not replace migration tracking with a blanket requirement for IF NOT EXISTS guards.
- Use separate setup and runtime database identities. Apply migrations and synthetic seeds with the privileged setup identity; the API must not run with owner/admin privileges.
- Follow [the database maintenance guideline](../../docs/database/maintenance.md) and update schema, relationships, migration records, and cache impact in the same change as implementation.

## Booking integrity and time handling

- Validate customer/vehicle ownership and dealership consistency in both the application and persistence boundary.
- Derive the end time from the configured positive service duration and copy that duration into the confirmed appointment.
- Use an injected TimeProvider for current-time decisions. Require an explicit input offset, normalize instants to UTC, and evaluate local opening hours using the dealership's IANA time zone.
- Use half-open intervals: existing.StartUtc < requested.EndUtc AND existing.EndUtc > requested.StartUtc. Adjacent appointments are allowed; both technician and bay must remain available throughout the interval.
- Confirm only through the booking procedure's short SERIALIZABLE transaction. Lock the existing technician row first, then the bay row with UPDLOCK and HOLDLOCK; revalidate eligibility and both overlaps before inserting and committing.
- Restrict runtime appointment writes to the same-owner booking procedure. Grant the required reads and procedure execution, with no direct appointment INSERT, UPDATE, or DELETE.
- Bound candidate attempts and deadlock retries, roll back before another attempt, and distinguish resource conflicts from exhausted retries/lock timeouts. Do not blindly retry a write after an unknown commit outcome.
- Do not use cached availability, process-local locks, Redis locks, a unique start-time key, or an appointment concurrency token as the sole overlap defense.
- Any future appointment mutation must preserve the resource-locking protocol and atomicity guarantees.

## Cache and observability

- Use Redis through IDistributedCache for service catalogs initially. Booking correctness, qualification, shifts, and final duration/allocation validation use authoritative SQL Server state.
- Apply a bounded cache-operation budget and fall back to SQL Server on recoverable Redis connectivity/timeout failures. Do not hide unrelated programming errors.
- Record cache key patterns, payload versions, TTLs, source tables, invalidation triggers, and refresh timing in [the cache reference](../../docs/database/cache.md).
- Use Serilog JSON console output and the ticket's OpenTelemetry/Collector/Prometheus/Tempo/Grafana design. Export asynchronously with bounded behavior; telemetry must not become a booking-success dependency.
- Use bounded metric attributes such as route templates and outcome codes. Exclude customer details, raw request bodies, credentials, and unbounded identifiers from telemetry where specified by the design.

## Code clarity and verification

- Prefer clear names, guard clauses, and methods with one responsibility. Explain non-obvious business decisions and transaction boundaries in comments.
- Keep pure scheduling calculations independently testable, while authoritative reservation checks execute in SQL Server. Pure C# interval tests do not replace database overlap protection.
- Use the implemented solution/project paths and the pinned SDK for restore/build/test/publish. Do not invent commands or require inherited framework-specific build steps.
- Run meaningful xUnit and API integration checks appropriate to the change. Use real SQL Server for migrations, procedure locking, and runtime permissions, and real Redis for relevant lifecycle/outage checks.
- Coordinate concurrency tests with independent contexts/connections and barriers rather than arbitrary sleeps. Assert final database state as well as HTTP responses.
- Follow [the testing guide](../../docs/test/README.md); record actual commands, observed outcomes, and unexecuted checks separately. A successful build alone does not establish booking correctness.
- Keep credentials in local configuration sources and use synthetic data. Review tracked files rather than assuming ignore rules remove already tracked secrets.
