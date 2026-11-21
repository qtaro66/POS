# Unified Data Sent acceptance

This bundle covers the complete POS observation path: atomic local source reads,
authenticated HTTP response, strict client parsing, controller refresh/cancellation,
Sidebar and System Status, Home's existing summary, accepted-command refresh and
sender/receiver recovery evidence. No physical hardware is required.

## Source matrix

Exercise each of table events, closing reports and sequence replay in empty,
pending and confirmed states. Expected State is Pending whenever any source is
pending, Current only with confirmed history and no outstanding work, otherwise
NoHistory. Verify these particularly misleading combinations:

- Recent table acknowledgement plus an unconfirmed report remains Pending.
- Both normal queues confirmed plus outstanding replay remains Pending.
- Central saved the report but its reply was lost: Branch remains Pending until
  the original body is resent and AlreadyAccepted is durably recorded locally.
- Disabled/mismatched sender configuration with pending records stays visible and
  never becomes Current merely because another channel's worker is enabled.
- Unknown processing/failure metadata remains pending and exposes only a count.
- Any missing/corrupt source makes the whole modern observation unavailable;
  neither a recent legacy timestamp nor another readable queue is a fallback.

Check oldest-pending and latest-receipt timestamps with Z/+00:00 variants and
same-millisecond seven-digit differences. Foreign Branch data, including malformed
foreign timestamps, must not influence counts or maxima. Compare persisted state
before/after reads and after cancellation: no claims, retries, edits or removals.
The query tests also compare concurrent atomic writer generations to ensure the
three source reads cannot be mixed into one false observation.

## POS refresh and recovery

1. Open the POS. Data Synced retains its employee-cache coverage. Data Sent shows
   combined source counts, latest receipt and the current observation time.
2. Submit an order or successfully close/recover a shift/store. Observe a new
   status read after the Branch result. A failure or exception in that read must
   not change the accepted command result or retain an otherwise cleared request.
3. While the page is visible, allow sequential polling to run. Hide it, return,
   reconnect, refresh manually and change employee/Branch scope with delayed old
   requests. Aborted old responses cannot populate the new scope or revive stale
   Current. Hung requests become unavailable at the observation deadline.
4. Confirm System Status has channel breakdowns, replay counts, pending age
   references and safe configuration warnings. Home's table-specific detail stays
   qualified and cannot conceal report/replay backlog. Sidebar warning text must
   remain legible against its dark background. Do not add global cards to the
   main store-and-shift workspace.
5. Run a legacy port without deliveryOverview: only the old table receipt is
   described. Run a modern reader returning 404 or malformed payload: it remains
   unavailable and never silently invokes that legacy reader.

## Evidence and limits

Automated evidence uses temporary real SQLite databases, mapped Branch API
request delegates, synthetic sender/receiver HTTP handlers and browser unit/DOM
fixtures. The extended wire tests cover counted CloseStore and uncounted
CloseShift through lost acknowledgement, Branch reopen, duplicate receipt and
unified Current after confirmation. Central storage in those tests is simulated.

This observes recorded local receipt boundaries for three POS sources, not all
Cloud data or current network health. No live PostgreSQL, deployed mTLS handshake,
printer, drawer or payment-terminal result is claimed. Follow the existing Central
activation runbook for staging. Branch schema remains 33 and Central schema 2.
Prepare a clean final full-gate commit and handoff; D21.3 review applies to the new
read contract, while Antigravity owns publication and remote checks.
