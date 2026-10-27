# PostgreSQL Cloud event-ingestion store boundary

## Status

Accepted

## Decision

Implement the Central PostgreSQL ingestion store as a transaction coordinator
behind `IBranchCloudEventIngestionStore`. A session first attempts to insert the
complete immutable Branch event. A successful insert is committed and returns
`Stored`.

When a unique identity prevents insertion, the same transaction reads every
row matching either the global event ID or the Branch ID plus Branch sequence.
Exactly one field-for-field-equivalent logical event is committed and returns
`AlreadyStored`. Missing identity data fails closed. Multiple matches or any
different event content raises `BranchCloudEventUniquenessConflictException`
without committing.

The transaction session owns rollback-on-dispose behavior. Cancellation is
forwarded to session creation and every database operation.

## Consequences

- A durable acknowledgment is returned only after transaction commit.
- Reusing either protected identity cannot modify the persisted event.
- The persistence project now depends on the application synchronization
  boundary, consistent with Branch persistence adapters.
- The Npgsql transaction session, SQL commands, dependency-injection
  composition, and live PostgreSQL integration tests remain separate slices.
