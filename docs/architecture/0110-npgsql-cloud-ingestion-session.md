# Npgsql Cloud event-ingestion transaction session

## Status

Accepted

## Decision

Implement `IPostgresBranchCloudEventIngestionSessionFactory` with Npgsql and the
shared `PostgresCentralConnectionFactory`. Each persistence attempt opens a
read-committed PostgreSQL transaction and executes only the parameterized SQL
defined by ADR 0109.

The insert binds explicit PostgreSQL types for all immutable event fields. A
single affected row means the insert succeeded; zero means a protected identity
already exists. Any other affected-row count fails closed. The subsequent
identity read occurs in a separate statement in the same read-committed
transaction so a uniqueness wait can observe the committed winning row.

Persisted action names are parsed case-sensitively and must be defined
`TableSessionAction` values. UTC event timestamps are normalized to PostgreSQL's
microsecond precision before insertion and retry comparison. This prevents a
100-nanosecond .NET tick that PostgreSQL cannot preserve from turning an exact
retry into a false conflict.

Commit is explicit. Disposing an uncommitted session rolls it back, then always
disposes the transaction and connection. Connection, transaction, and command
failures propagate without being translated into durable acknowledgments.

## Boundaries

This slice implements the Npgsql transaction session but does not register the
factory or store in dependency injection, activate ingestion persistence at
runtime, add readiness integration, or contact a live PostgreSQL server during
automated tests.
