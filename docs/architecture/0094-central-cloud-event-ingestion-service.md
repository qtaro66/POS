# 0094: Central Cloud event-ingestion service

## Status

Accepted

## Decision

Place an application service in front of the Central Cloud event-ingestion store.
The service forwards the immutable Branch event to persistence and returns only
a non-null durable persistence result whose event ID, Branch ID, and
`branchSequence` exactly match the submitted envelope.

A missing result or mismatched identity fails closed. A future transport must not
issue a durable acknowledgment when the service fails this validation. Exact
duplicate handling and uniqueness conflicts retain the semantics defined in ADR
0093.

## Boundaries

This service does not select or connect to PostgreSQL, create a migration,
activate an HTTP endpoint, or define authentication, serialization, response
mapping, missing-sequence recovery, or retention.

Automated tests use an in-memory store and contact no database, network, or
Branch hardware.
