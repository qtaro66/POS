# ADR 0004: RequestPayment Bill Preview and print outbox boundary

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D01.5, D12.5, and D20.3

## Decision

An accepted `RequestPayment` must capture an immutable Bill Preview payload, persist its snapshot, and enqueue exactly one cashier print job in the same SQLite transaction that changes the table session to `PaymentPending`, writes audit/cloud-outbox records, and stores the idempotency receipt.

The snapshot uses a versioned JSON payload and stores the SHA-256 of the exact UTF-8 bytes. It is labelled `ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ` and contains no receipt number. The print job references the immutable snapshot, has purpose `BillPreview`, target station `Cashier`, initial status `PrintQueued`, and zero attempts. It does not create a payment, sale, drawer event, or `Completed` table state.

Bill Preview content is captured through `ISqliteBillPreviewContentSource` using the same open `SqliteConnection` and `SqliteTransaction`. An implementation must read the authoritative order/bill projection through those supplied objects and must not open a second connection. This preserves the transaction boundary between the bill view being frozen and the state transition.

Snapshot and print-job identifiers are deterministic from `(branchId, requestId)`. The command receipt is checked first, so replaying the same request returns the original result without invoking the content source or inserting another snapshot or print job.

## SQLite schema version 2

Schema version 2 adds:

- `bill_preview_snapshots`
- `print_jobs`, including status, attempt count, retry/error fields, blocked reason, reprint linkage, and snapshot foreign key

Initialization migrates a valid version 1 database to version 2 inside one non-deferred transaction. Newer unknown versions remain rejected without being downgraded.

## Deliberately outside this slice

- `SubmitOrder`, Order Line persistence, and the authoritative bill calculator
- the production implementation of `ISqliteBillPreviewContentSource`
- a stable versioned schema for the JSON content beyond the opaque version/hash envelope
- printer routing from cashier station to a configured device
- PrintGateway dispatch, attempts, ACK/status handling, retry worker, and manual reprint
- PC manual Bill Preview while the table remains `Ordering`
- `CancelPaymentRequest` removal of the unused preview/payment snapshot
- Branch API endpoints and authorization

This slice contacts no printer. `RequestPayment` remains prohibited from production API wiring until an authoritative transactional content source exists and the remaining payment-lock workflow is implemented.
