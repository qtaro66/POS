# Durable closing report outbox

Schema 33 captures every accepted CloseShift and CloseStore as an immutable
report in the same SQLite transaction as lifecycle history and its cash/table
snapshot. An enqueue failure rolls back the close; a network failure cannot
participate in this transaction. Replayed commands cannot enqueue duplicates.
This implements the Branch persistence foundation of D04.11.6.

The opaque report ID is derived unambiguously from Branch and request IDs.
Payload schemaVersion 1 includes identity, business date, the original Closing
object and the saved cashSummary, including financials and table counts when
available. Nested snapshots retain their existing persisted property names.
Migration backfills accepted closes in history order using saved snapshots only;
missing historical summaries stay null rather than being recomputed.

Workers claim one available report using an immediate SQLite transaction and a
UTC lease. Only the current, unexpired lease and owner can confirm or defer it.
An expired lease can be reclaimed with the same report ID and unchanged payload.
Attempts increase on claim; retry records a bounded failure category and a future
availability time. Delivered reports remain as evidence. Pending counts include
leased reports. Different reports may be leased concurrently; consumers must
use report IDs for deduplication and business identity/time for presentation.

This queue is separate from table-session event synchronization and does not
consume or alter its sequence numbers. The authenticated PcPos status endpoint
exposes branch-scoped counts, never payloads, worker IDs or failure details.

No background HTTP sender, Central ingestion endpoint, notification creation,
Back Office screen or physical hardware integration is included. A future sender
must preserve payload and report ID, verify the Central acknowledgement before
confirmation, and retry uncertain acknowledgements with the same ID. Central
must durably deduplicate reports before acknowledging them. Consequently pending
reports remain pending until such a sender is integrated; status is not evidence
of Central delivery. Automatic report retention/deletion is not introduced.

Schema 33 and the new API contract require D21.3 approval before merge.
