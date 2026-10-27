# PostgreSQL Cloud event-ingestion session contract

## Status

Accepted

## Decision

Define schema-qualified, parameterized SQL for the PostgreSQL transaction
session introduced by ADR 0108.

The insert writes every immutable field from `BranchCloudOutboxEventEnvelope`
to `public.branch_cloud_events` and uses `ON CONFLICT DO NOTHING`. PostgreSQL
assigns `received_at`; it is not Branch event content and is excluded from exact
retry comparison.

When insertion reports no affected row, the session reads every row matching
either protected identity: global `event_id`, or `branch_id` plus
`branch_sequence`. The read projects every immutable event field in a stable
order so the store can distinguish an exact retry from conflicting identity
reuse. It never updates or deletes the original event.

`action` is stored as the canonical `TableSessionAction` name. The future
Npgsql session must bind explicit PostgreSQL types and use a read-committed
transaction so a statement following a completed uniqueness wait observes the
winning row.

## Boundaries

This slice defines SQL text and stable parameter names only. It does not open a
connection, begin or commit a transaction, execute SQL, register services, or
contact PostgreSQL. Automated tests inspect the contract without database,
network, or hardware access.
