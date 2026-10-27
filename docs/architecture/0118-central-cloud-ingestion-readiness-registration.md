# Central Cloud-ingestion readiness registration

## Status

Accepted

## Decision

Register the Central PostgreSQL ingestion health-check adapter only when
`CentralCloudIngestion:Enabled` is `true`. The registration uses one stable name,
an `Unhealthy` failure status, and the `ready` tag for later endpoint filtering.

Repeated runtime composition produces one health-check registration and one
adapter singleton. The adapter resolves the readiness probe already present in
the enabled PostgreSQL ingestion graph. Disabled Central startup registers no
adapter and no database readiness check.

## Consequences

- The enabled runtime has a standard readiness registration without creating a
  second PostgreSQL connection pool.
- Endpoint mapping can later select readiness checks by the stable tag.
- Registration alone does not execute the probe or contact PostgreSQL.

## Boundaries

This slice does not map a health endpoint, define its response body or cache
policy, alter authentication, or add a live PostgreSQL integration test.
