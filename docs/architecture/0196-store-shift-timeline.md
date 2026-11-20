# Store and shift timeline

The PC POS store workspace reads a Branch-scoped timeline from the persisted
lifecycle command history. Opening commands provide the store and shift
attribution and timestamps. Closing entries use the saved closing snapshots.
Read requests do not require an additional employee PIN.

The timeline orders shifts by their durable command sequence and retains
store/session identifiers. It uses the current Business Date's recorded shifts
and identifies active shifts separately from completed ones. The workspace
shows opening and closing attribution, local display times and elapsed time,
using the Branch's observation time for an active shift.

The closing viewer reads the saved snapshot for the selected store and shift.
Later shifts, retirement of another request and current cash changes must not
rewrite an earlier closing. Null actual cash and difference display as
NotReconciled.

This closes the lifecycle timeline and recorded-closing display portion of
D18.9. Full D04.11.7 reports still need immutable sales, payment method,
tax/discount and cancellation summaries captured at closing, together with
Central lifecycle events. The saved lifecycle snapshot is the available source
for expected/actual/difference and closing attribution.
