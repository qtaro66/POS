# ADR 0005: Authoritative bill calculation and SQLite preview source

- Status: Accepted
- Date: 2026-10-13
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D01.5, D05.6-D05.9, D08.3-D08.4, D09, and D10.1-D10.2

## Decision

Bill totals are calculated by the platform-owned `BillCalculator`; browser and mobile clients are not authoritative calculators. Inputs are immutable Order Line, Option, discount, and billing-policy snapshots. Prices, Topping prices, Service Charge, and reported VAT are VAT-inclusive.

The calculator applies one active discount per Order Line, then one table-session discount, then Service Charge. Fixed discounts retain requested and applied amounts separately and are capped at their eligible base. Percentage discounts are restricted to 0-100%. The global rounding adjustment is calculated once at bill level by flooring the total to a whole baht and is kept separate from discounts. Inclusive VAT is extracted for reporting and is never added to the amount due.

Intermediate monetary rounding is not hard-coded globally. Each table session stores `currencyScale` and `midpointRounding` as part of its calculation-policy snapshot. This avoids silently choosing a rounding rule that the requirements have not fixed permanently.

`SqliteBillPreviewContentSource` is the production content-source implementation for the transaction boundary established in ADR 0004. It reads the table-session billing policy, active Order Lines, Option snapshots, active quantities, discounts, and fulfillment types through the supplied SQLite connection and transaction. It then emits deterministic Bill Preview JSON containing line detail, Requested/Applied discounts, Charges, VAT, Rounding, and net total.

The transaction boundary checks for active Order Lines before capture and persists an idempotent `NoActiveOrderLines` rejection when none remain. The source rejects a session with no billing-policy snapshot. Because both checks and capture occur inside `RequestPayment`'s transaction, either the complete preview/state/outbox/receipt set commits or the table remains `Ordering` with no partial preview job.

## SQLite schema version 3

Schema version 3 adds:

- `table_session_billing_policy`
- `order_batches`
- `order_lines`
- `order_line_options`

Exact decimal snapshot values are stored as invariant text and parsed into .NET `decimal`; this avoids depending on SQLite floating-point arithmetic for financial results. Version 1 and version 2 databases migrate forward atomically without deleting existing sessions, Bill Preview snapshots, or print jobs. Unknown newer versions remain rejected.

## Deliberately outside this slice

- `SubmitOrder` command/API and the authoritative catalog resolver
- Master Data package activation and `sessionMasterDataVersion` capture during `OpenTable`
- Option availability, min/max selection, Sold Out, and branch-catalog validation
- Order-line cancellation, transfer, split, discount, and price-override command handlers
- Card Charge and Draft Payment Plan, which are calculated only after payment methods are selected
- PrintGateway dispatch and physical printing

There is intentionally no public writer for the new Order tables in this slice. Adding a writer before the Branch can resolve and validate Master Data would allow client-supplied prices or unavailable Options to become authoritative, violating D08-D09. The next slice must implement the catalog boundary and atomic `SubmitOrder` command before these tables are exposed through the Branch API.
