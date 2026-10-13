# ADR 0002: Table session application command boundary

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D12.3-D12.5 and D20.2-D20.4

## Decision

`OpenTable` and `RequestPayment` enter the domain through `TableSessionCommandService`. Every command carries the required request, branch, device, employee, table-session, and client-time metadata.

The service executes each command through `ITableSessionCommandStore.ExecuteAtomicallyAsync`. An implementation of this contract must serialize competing writes and commit all affected business state, the idempotency receipt, audit record, and cloud-sync outbox record in one durable transaction. A result may be returned as `BranchAccepted` only after that transaction commits.

The transaction performs idempotency lookup before any domain mutation. Accepted and rejected results are both stored, so repeating the same `(branchId, requestId)` returns the original result without another transition or outbox record.

The stored receipt retains `deviceId`, `employeeId`, and `clientCreatedAt` alongside the server result so a request remains traceable even when it produces no domain transition, such as a concurrent `OpenTable` that joins the existing active session.

`RequestPayment` requires an exact expected table version. A stale version is rejected before mutation. The domain additionally rejects payment requests when a cancellation request remains open or when the table session is not in `Ordering`.

Concurrent `OpenTable` commands for the same available table are serialized. The first creates the session; later commands receive the existing active `tableSessionId` and store their own idempotent receipt without creating another session or transition.

## Transaction contract

The future SQLite implementation must enforce the following within one database transaction:

1. Look up `(branchId, requestId)` in the command inbox.
2. Load and validate table/session state.
3. Apply the domain transition with an expected-version predicate when required.
4. Insert the immutable audit record.
5. Insert the cloud-sync outbox record.
6. Insert the command receipt used for replay.
7. Commit before returning `BranchAccepted`.

## Deliberately outside this slice

- SQLite schema and durability settings
- aggregate rehydration after process restart
- `branchSequence` allocation
- HTTP endpoints and authorization
- print outbox records
- order batches, payment lines, and monetary calculation
- crash injection and recovery tests

These are not claimed as implemented. This slice defines and tests the Application boundary that the SQLite adapter must satisfy next.

Until the SQLite transaction, Bill Preview snapshot, and persistent print-outbox work are implemented, this service must not be wired to a production HTTP endpoint. In particular, accepting `RequestPayment` without the D01.5 Preview and print job in the same durable transaction would violate the requirements.
