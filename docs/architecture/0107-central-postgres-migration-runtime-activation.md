# Central PostgreSQL migration runtime activation

## Decision

The Central API registers `CentralPostgresMigrationStartupService` as an
`IHostedService` only when `CentralCloudIngestion:Enabled` is `true`.

When enabled, host startup runs the PostgreSQL migration executor before the
application reports a successful start. A connection failure, migration
failure, or schema drift therefore fails startup instead of allowing the
Central ingestion runtime to operate against an unknown schema.

The default configuration remains disabled. In that state, the migration
startup service and PostgreSQL migration dependencies are not registered, and
startup does not contact PostgreSQL.

## Consequences

- Enabled deployments must provide `ConnectionStrings:CentralDatabase`.
- Registration is idempotent; repeated composition adds one hosted service.
- Migration execution still uses the existing PostgreSQL advisory-lock and
  checksum validation boundaries.
- Production credentials and live PostgreSQL integration tests remain outside
  this repository slice.
- This decision does not add ingestion persistence or readiness endpoints.
