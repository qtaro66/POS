# ADR 0001: Table session state machine

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D20.2

## Decision

The domain owns a `TableSession` aggregate with these persisted business states:

`Ordering`, `PaymentPending`, `PaymentInProgress`, `PartiallyPaid`, `Completed`, `Cancelled`, and `Closed`.

The aggregate enforces the D20.2 transition graph and records an immutable transition object for every state or table change. A transition includes the action, previous/current state, previous/current table, aggregate version, branch time, employee, device, and optional reason. The Application layer will add `branchSequence`, idempotency results, transaction boundaries, and persistent audit/outbox records.

`Available` is a table availability state before a session exists, not a persisted `TableSessionStatus`. `OpenTable` creates a session directly in `Ordering` and records the opening transition.

## Enforced rules

- Orders are accepted only in `Ordering`.
- `RequestPayment` is rejected while a cancellation request remains open.
- `CancelPaymentRequest` is allowed only before payment starts.
- A failed or cancelled payment returns to `PaymentPending`, not `Ordering`.
- `PartiallyPaid` returns to `PaymentPending` only after all confirmed payment lines are reversed.
- `CompletePayment` requires the caller to prove that the payable amount is fully applied.
- `CancelBill` is accepted only in `Ordering` or `PaymentPending`.
- `ChangeTable` keeps the same session identity and requires an available, different destination.
- `CloseTable` requires no remaining item and no outstanding balance.
- Terminal sessions cannot accept new orders or further transitions.

## Deliberately outside this slice

- `requestId` idempotency and replay results
- serialization/concurrency and expected-version checks
- database persistence, `branchSequence`, transactions, audit records, and outbox
- authorization and PC/Mobile channel enforcement
- order batches, cancellation workflow, payment lines, monetary calculation, and receipt/print jobs
- restore/rehydration of an aggregate from persistence

These concerns belong to later Application, persistence, and API slices. Keeping them out of the domain state object prevents coupling the business transition rules to SQLite, HTTP, a specific operating system, or printer hardware.
