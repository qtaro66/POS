# Central PostgreSQL Cloud-ingestion configuration

## Status

Accepted

## Decision

Add a Central API composition boundary that registers the PostgreSQL Cloud-event
ingestion graph only when `CentralCloudIngestion:Enabled` is `true`.

Enabled deployments must provide the existing named connection string
`ConnectionStrings:CentralDatabase`. The ingestion graph and migration runtime
therefore share the same `PostgresCentralConnectionFactory` and connection pool.
Missing or blank configuration fails immediately during service composition.

When ingestion is disabled, the method returns without registering PostgreSQL
or application ingestion services. The repository default remains disabled, so
default application startup still has no Central database dependency.

## Consequences

- Persistence configuration follows the same feature flag and database identity
  as schema migration configuration.
- Repeated registration is idempotent and does not create duplicate stores or
  application services.
- Constructing and resolving the enabled graph remains offline until an
  ingestion request or migration startup opens a connection.

## Boundaries

This slice defines configuration-driven composition but does not call it from
`Program.cs`, change endpoint activation, contact PostgreSQL, or add readiness
probes. Runtime activation remains a separate change.
