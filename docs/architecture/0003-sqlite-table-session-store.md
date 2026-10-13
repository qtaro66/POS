# ADR 0003: SQLite table-session command store

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D12.3-D12.5 and D20.2-D20.4

## Decision

The Branch Service persists the table-session command boundary through `Pos.Branch.Infrastructure.Sqlite`. Domain and Application projects remain independent of SQLite.

Each command opens a dedicated connection and a non-deferred SQLite transaction. This obtains the write transaction before the idempotency lookup, serializing competing commands across store instances and processes. The callback commits only after the session, command receipt, audit record, and cloud-sync outbox record have all been written. Any exception rolls back the complete callback.

The database uses WAL journal mode, `synchronous=FULL`, foreign keys, a finite busy timeout, and no provider connection pooling. Timestamps use round-trip ISO 8601 text and enum values use case-sensitive symbolic names. The active-table partial unique index provides a database-level backstop against two active sessions for one branch table.

The command receipt is the persistent inbox/deduplication record. Reopening the database and repeating `(branchId, requestId)` returns the original accepted or rejected result without repeating the domain transition or adding outbox records.

## Schema version 1

- `table_sessions`
- `command_receipts`
- `table_session_audit`
- `cloud_sync_outbox`, including retry count, next retry time, last error, and processing status
- `cancellation_requests`, limited in this slice to the lookup required to block `RequestPayment`

The aggregate is rehydrated from the persisted session snapshot without fabricating historical transitions. Its next valid transition advances the stored version with an optimistic `WHERE version = expectedVersion` predicate.

## Deliberately outside this slice

- Branch API dependency injection and HTTP endpoints
- `branchSequence` allocation
- schema migration beyond version 1
- cancellation-request command handlers
- cloud outbox dispatcher
- Bill Preview snapshot and persistent print outbox required by D01.5
- crash injection between operating-system flush boundaries
- database backup, restore, integrity-check, and retention jobs

The production prohibition in ADR 0002 remains: `RequestPayment` must not be exposed through a production endpoint until Bill Preview and its print job are included in the same durable transaction.
