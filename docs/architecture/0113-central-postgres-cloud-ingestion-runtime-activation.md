# Central PostgreSQL Cloud-ingestion runtime activation

## Status

Accepted

## Decision

Use one Central API service-composition entry point for Cloud ingestion. It
binds and validates the existing ingestion options, conditionally composes the
PostgreSQL migration runtime, and conditionally composes the PostgreSQL event
store. `Program.cs` calls this complete runtime boundary before building the
host.

When `CentralCloudIngestion:Enabled` is `true`, startup requires the configured
authentication scheme and `ConnectionStrings:CentralDatabase`, registers the
ingestion service and store, and runs schema migrations through the existing
hosted startup service. Options validation confirms that the authentication and
persistence dependencies are present before the application serves requests.

When disabled, no PostgreSQL connection factory, ingestion store, or migration
hosted service is registered. The repository default therefore remains an
offline-safe Central API with no database dependency.

## Consequences

- The enabled endpoint can resolve its complete application and persistence
  dependency graph.
- Schema migration remains fail-closed before successful host startup.
- Repeated composition remains idempotent.
- Registering and validating the dependency graph does not itself contact the
  database; the migration hosted service opens the first connection when the
  host starts.

## Boundaries

This slice activates existing composition only. It does not provide production
credentials or authentication handlers, add a live PostgreSQL integration test,
or expose a database readiness endpoint.
