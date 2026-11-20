# Cash settlement and durable store shifts

Cash settlement resolves its Branch store session and active shift in the same
immediate transaction as the payment commit. Once durable lifecycle history
exists, it is authoritative even while the store is closed or between shifts.
A stale fixture shift cannot bypass those states.

The Branch's local date must match the open store Business Date. The original
payment result is returned on an identical request replay after a shift closes,
without creating another payment, receipt or completion event.

The settlement retains the durable store/shift identifiers. Live cash summaries
and immutable closing summaries therefore include its applied amount, while
handover floats remain informational. Store close and settlement serialize so
that a committed sale is included in the closing snapshot, or the closed store
rejects the new sale.

For compatibility, the existing test shift source is used only if no durable
store history exists for the Branch. The settlement endpoint and receipt
simulation remain under the existing cash-sale-test feature flag. Production
payment, physical receipts, non-cash methods and Central lifecycle reports
remain integration work.
