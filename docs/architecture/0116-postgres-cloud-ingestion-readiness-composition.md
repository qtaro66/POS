# PostgreSQL Cloud-ingestion readiness composition

## Status

Accepted

## Decision

Register the Npgsql Cloud-ingestion readiness probe as part of the existing
Central PostgreSQL ingestion dependency graph. Both the concrete adapter and
`IPostgresCloudIngestionReadinessProbe` resolve to the same singleton and share
the existing `PostgresCentralConnectionFactory` with migrations and ingestion.

The Central API continues to compose this graph only when
`CentralCloudIngestion:Enabled` is `true`. Disabled startup registers neither a
PostgreSQL connection factory nor a readiness probe and remains independent of
the Central database.

## Consequences

- Enabled Central runtime consumers can resolve one readiness probe without
  creating another Npgsql pool.
- Repeated composition remains idempotent.
- Registering the probe does not open a connection or contact PostgreSQL.

## Boundaries

This slice registers the probe only. It does not execute it during startup,
expose readiness over HTTP, change endpoint behavior, or add a live PostgreSQL
integration test.
