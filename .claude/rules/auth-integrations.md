# Scheduler access, testing, and dependencies

Use [project scope](../../README.md) and [the system design](../../docs/architecture.md) as the baseline. These rules describe selected scope; application implementation and runtime verification remain pending.

## Authentication and data integrity

- The initial release is an unauthenticated local demo using synthetic data. Authentication, customer access control, and authenticated dealership roles require separately scoped requirements and tests.
- Validate customer/vehicle ownership and dealership consistency in the application and persistence boundary. These are data-integrity checks, not authorization.
- Appointment retrieval is dealership scoped; a booking outside the requested dealership returns `404` according to the ticket contract.
- Define routes through ASP.NET Core controllers and publish the implemented OpenAPI contract. No database route-registration table or legacy login/session workflow is required.
- Before expanding beyond the local demo, define authentication, authorization, deployment, and data-access contracts in a separate ticket.

## Database access and configuration

- Use SQL Server 2022 and EF Core 10 migrations, including procedure SQL, constraints, indexes, and grants where required. Follow [migration guidance](../../docs/database/migration.md).
- Use a separate privileged setup identity for migrations and synthetic seeds. The runtime principal receives required reads and booking procedure execution, with no direct appointment writes, schema-altering privileges, or owner/admin role.
- Keep credentials in local configuration sources. Record actual configuration names and loading behavior in [the setup guide](../../docs/setup-guide.md) once implemented; do not inherit another application's configuration tables, accounts, or endpoints.
- A configured tool or connection is not permission to access an unrelated environment. Use isolated project dependencies for active verification.

## Testing

- Use xUnit for pure rules, WebApplicationFactory for API integration, and real SQL Server and Redis through Testcontainers for relevant dependency checks.
- Verify the booking procedure and denied direct writes using the runtime identity. Use independent connections and coordinated concurrency tests, asserting final stored state as well as API responses.
- Follow [the testing guide](../../docs/test/README.md) and the ticket's acceptance matrix. Executable tests belong in application test projects; ticket reports record observed results and unexecuted checks separately.
- Manual Swagger or Grafana checks supplement automation. Use synthetic seed identifiers and implemented endpoints; inherited browser-test credentials and defaults do not apply.

## Dependency boundaries

| Dependency | Initial responsibility |
| --- | --- |
| SQL Server | Authoritative data, transactional booking procedure, persistent confirmations |
| Redis | Optional service-catalog cache with bounded SQL fallback |
| Serilog | Structured JSON console logs and request/trace correlation |
| OpenTelemetry and Collector | Bounded asynchronous metrics and trace export |
| Prometheus, Tempo, and Grafana | Metric storage, trace storage, and provisioned operational views |
| Docker Compose and GitHub Actions | Reproducible local dependencies and automated verification |

Redis does not provide booking locks. External dealership integrations, payments, notifications, and background jobs are outside the initial release. Add integrations only through scoped requirements, documented contracts, configuration, failure behavior, and tests.
