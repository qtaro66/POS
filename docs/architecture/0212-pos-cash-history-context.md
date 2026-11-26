# Recorded shift and receipt context in cash history

This advances D18.8 and D01.4 by combining two independent read-only evidence
views for an already captured cash sale. It does not change D04.11 store/shift
rules, receipt rendering, settlement or printing behavior.

## Captured subject

Resolve the original cash request under the authenticated Branch in the same
read transaction. Use its saved table session, store session, shift, business
date and receipt identity. Do not select the current store, current shift or
current business date as a replacement. Missing foreign parents are 404 without
existence disclosure. Complete provisioned PcPos identity is required; there is
no extra PIN or read audit. Both GET responses are no-store and accept no body
or query overrides. A missing legacy bill-item snapshot does not prevent these
reads when the core cash capture remains valid.

## Shift context

Read recorded store_lifecycle_commands for the captured store and shift. Expose
original operation request, action, actor, terminal, time, branch-local date,
nullable opening/counting amount and original closing values. Normalize actions
from the existing stored numeric enum into named read-contract values.

A valid shift can have at most one opening and one closing. Read up to three
selected records and reject excess or duplicate slots instead of silently
truncating. Empty, opening-only or closing-only evidence does not establish the
current shift state or prove that missing operations never happened. Order is
recorded sequence, not an invented monotonic timestamp chronology.

Validate selected command/result JSON, required fields, unique property names,
captured identities, business date, action/result agreement and original closing
context. Bound each JSON document to 1 MiB. Do not replay today's domain state
machine or recompute expected cash from current sales. Closing actual amount and
difference may both be null for an unreconciled CloseShift. A recorded difference
must agree with original actual minus expected amounts; displayed values remain
the recorded decimals. CloseStore requires the recorded count.

OpenShift result JSON can legitimately retain the previous shift's Closing.
That inherited field is not projected as a closing for this shift. OpenStore's
result must have no closing and its opening date must match the business date.
Closing amounts describe the whole recorded shift/store operation, not the
selected receipt alone. This view does not replace a complete closing report.

## Receipt simulation evidence

The current cash implementation captures a simulated receipt queue identity,
not a physical receipt print job. Return its original queue time and captured
QueuedForSimulation observation separately from an optional later simulation
result in test_receipt_simulation_results.

That result table is created lazily by the existing simulation command. History
checks for its existence through sqlite_master without creating it. A missing
table or missing result row produces simulation=null, meaning no result was
recorded. It does not mean a physical printer is queued or failed.

Return a recorded Simulated result only with nonblank plain text bounded to
128 KiB UTF-8; preserve CR/LF/TAB and reject other controls. Failed requires null
text. Read exactly the stored text: do not call the simulation command, render a
fallback receipt or read current menu/configuration data. This storage has no
captured result timestamp or content hash; neither is invented. It is recorded
simulation evidence, not proof of physical printing success.

## POS behavior and validation

Each panel loads, refreshes and fails independently. Changes to reader, captured
subject, scope or lock abort or ignore prior reads; a refresh hides stale content
immediately. Unsupported older clients show an explicit unconnected state.
Names and receipt text render as plain text. Cash/bill detail stays unchanged
when context is absent, malformed or unavailable.

Automated checks exercise actual durable store/shift and simulation commands as
fixture setup, followed by read-only queries. Isolation, absent optional storage,
unchanged database/schema fingerprints, corrupt sources, safe rendering, stale
responses and client capability replacement are checked without hardware.

Branch schema 35 and Central schema 3 remain unchanged. No migrations,
dependencies, command permissions or write routes are added. D21.3 explicit
approval is required before merging the two read contracts. Reprint, physical
printing/drawer acceptance, refunds, Back Office and Stock remain separate work.
