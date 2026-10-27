# PostgreSQL Cloud event-ingestion composition

## Status

Accepted

## Decision

Provide one persistence-layer composition method that registers the complete
Central PostgreSQL event-ingestion dependency graph:

- the shared `PostgresCentralConnectionFactory`;
- the Npgsql ingestion-session factory and its interface;
- the PostgreSQL ingestion store and `IBranchCloudEventIngestionStore`;
- `BranchCloudEventIngestionService`.

All registrations are singleton and idempotent. The interface registrations
resolve the same concrete instances, and composition shares the existing
connection factory with PostgreSQL migration services regardless of registration
order. Constructing and resolving the graph does not open a database connection.

## Consequences

- The Central API can compose persistence with one connection string.
- Preexisting registrations remain respected because composition uses
  `TryAddSingleton`.
- Migration and ingestion services cannot accidentally create separate pools
  when composed into the same service collection.

## Boundaries

This slice registers persistence services only. It does not read application
configuration, enable ingestion, contact PostgreSQL, change endpoint behavior,
or add readiness probes. Runtime activation remains a separate Central API
composition decision.
