# 0099: PostgreSQL schema for Central Branch-event ingestion

## Status

Accepted

## Decision

Define PostgreSQL migration version 1 for normalized Branch Cloud events. The
schema stores every field of `BranchCloudOutboxEventEnvelope`, assigns a durable
receipt timestamp, and enforces both identities required by ADR 0093: a globally
unique `event_id` and a unique `branch_sequence` within each Branch.

The migration is embedded in the Central persistence assembly and exposed in an
ordered migration catalog. It records version 1 only after the event table has
been defined. A future migration runner must apply each migration exactly once
inside a database transaction and must not infer success from the table name
alone.

## Boundaries

This slice does not execute the migration, connect to PostgreSQL, implement the
ingestion store, compare duplicate payloads, probe readiness, configure TLS or
credentials, or activate database access at runtime.

Automated tests inspect the embedded migration and contact no database, network,
or Branch hardware.
