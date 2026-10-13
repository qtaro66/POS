# ADR 0008: Atomic SubmitOrder batch at the Branch boundary

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D03, D08, D09, D10, and D12

## Decision

One Mobile or PC cart submission is one `SubmitOrder` command and one immutable Order Batch. The Branch accepts or rejects the complete batch inside the same serialized SQLite transaction used by the table-session command boundary.

The Branch performs these operations in order:

1. replay a previously persisted receipt for the same `branchId + requestId`
2. require the exact `tableSessionId` to exist and remain `Ordering`
3. reject a reused `branchId + orderBatchId`
4. resolve every line through the authoritative Catalog resolver
5. reject the complete batch with persisted structured details if any line fails
6. allocate the next per-branch sequence under the serialized transaction
7. persist the Order Batch, Order Lines, Option snapshots, cloud outbox record, and accepted receipt atomically

Names, prices, Option membership, and Option prices come from the session's frozen Master Data version. Current availability and Sold Out checks still come from current Branch state. Client values cannot overwrite these authoritative snapshots.

`SubmitOrder` does not change the table-session state and does not increment its state-machine version. SQLite `BEGIN IMMEDIATE` provides serialization with `RequestPayment`: a batch committed first is visible to the Bill Preview; a payment request committed first changes the session to `PaymentPending`, causing the entire later batch to be rejected.

Every accepted batch preserves `requestId`, `orderBatchId`, `tableSessionId`, `deviceId`, `employeeId`, `clientCreatedAt`, `serverReceivedAt`, and a unique monotonically increasing `branchSequence`. Each line preserves its fulfillment type, comment, quantities, and resolved Item/Option snapshots.

## SQLite schema version 6

Schema version 6 adds:

- `command_receipts.error_details_json` for replayable structured rejection details
- `command_receipts.branch_sequence` for replaying the accepted Branch sequence
- `order_line_options.option_group_id` so an immutable Option snapshot retains its source Group
- `order_batches.branch_sequence`, `client_created_at`, and `server_received_at`
- migration backfill for existing batch sequence and timestamps, plus a partial unique index protecting non-null per-branch sequences

Catalog rejection details use schema version 1 and identify the line sequence, validation code, sellable Item, and Option Group when available.

## Deliberately outside this slice

- public Branch API endpoints and transport contracts
- kitchen/production Print Job creation and routing
- Recipe snapshots and Stock Movements because D07.2-D07.3 remain undecided
- employee/device authentication and pairing enforcement at the API boundary
- Master Data package download, validation, and activation
- cancellation, transfer, price override, discounts, and order-line mutation commands

This implementation is not yet a complete production ordering path until the API, production printing, and the confirmed Recipe/Stock rules are added.
