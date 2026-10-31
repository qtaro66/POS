# Active session detail for PC POS sent-order views

Before connecting the D18.6 sent-order tab, expose persisted Branch data rather
than reconstructing accepted orders from a browser basket. Add a read-only
session-detail query and opt-in HTTP route under the existing ordering feature.

A single SQLite read transaction captures the active session, open-cancellation
flag, durable batch receipts, lines/options and current financial projection.
Reuse `SqliteBillPreviewContentSource` and its existing calculator rather than
implementing client-side totals or changing calculation rules. Check its version
before projecting the in-memory JSON; do not persist a preview or print job.

Require an accepted receipt for every returned batch. Scope reads to the
authenticated branch and exclude closed sessions. Preserve cancelled lines and
current/original quantities; only active quantities enter current amounts. Batch
subtotals are before bill-level adjustments, while session preview totals include
the existing bill-level rules. Empty/all-cancelled sessions have null preview
totals because the calculator requires active lines; its invariant remains intact.

The read timestamp is request time, not a historical database timestamp. Receipt
and sequence ordering remain the authoritative event references. No schema,
payment/cancellation workflow, printer operation or financial algorithm changes.

D10.1 current totals remain estimates during Ordering. The existing tableVersion
is a state-transition version and does not advance on every SubmitOrder append;
clients must refresh this projection after mutations rather than assuming the
version alone validates cached financial content. Fixing or extending that
version model is separate work, not a silent change in this read-only slice.

Tests use generated minimal-API delegates, synthetic principals and real temporary
SQLite databases. Discount/cancellation cases seed persisted state directly;
they do not validate those command workflows or real credentials/devices. The
PC adapter adds a validated session-detail read, preserving the server data
without computing money. Visible UI integration remains follow-up work. Contracts
and authenticated financial-data exposure require D21.3 approval before merge.
