# Original production evidence in cash history

This extends D18.8 and D01.4 with read-only production snapshots associated with
the captured cash request's original table session. It prepares an evidence view
for D01.4.1 without implementing its separately authorized reprint command.

## Source and meaning

Read production_order_snapshots saved by SubmitOrder. Preserve original line
names, submitted quantities, fulfillment type, options, comments, table identity
and production station. Do not join current menu, printer configuration or order
rows to reconstruct those facts. A fully cancelled line can remain in its
original production snapshot even though the later chargeable bill omits it.
The original submitted quantity is not a current bill quantity or refund value.

Only station-routed submitted lines have these snapshots. An empty result does
not prove that no order was submitted. A snapshot proves captured submission
data, not successful physical printing or completed kitchen production. No print
job state, printer address, price, actor or device is inferred from other data.

Verify SHA-256 over the exact UTF-8 stored JSON before parsing. Support captured
schema 1 only, validate row/payload identities and timestamps, and bound JSON to
1 MiB. Invalid, unsupported or oversized selected snapshots are unavailable;
never truncate or rebuild them from current data. This integrity check does not
claim protection against an administrator replacing both content and its hash.

## Read boundary

Resolve the parent cash capture under the authenticated Branch first. Read only
its captured table session, including when its saved bill items are unavailable.
Complete provisioned PcPos identity is required. No additional PIN, read audit,
database migration, print job or business mutation is introduced. Responses are
no-store. Missing foreign parents disclose no existence information.

List filters select exact original batch, line and station identities. Detail
retains the complete original station snapshot, including other lines. Separate
high-water continuations bind the cash request, session, filters, limit, branch
and SQLite schema cookie. Later appends appear after refresh; maintenance or
database replacement requires a fresh traversal. Existing cash and event cursor
contracts remain unchanged.

Line filtering examines validated original snapshots rather than trusting an
unvalidated JSON extraction or current order-line index. A sparse line search
can therefore scan the session's matching snapshots; page size bounds returned
records, not total scan work. No production-scale latency claim is made.
Corruption in an examined snapshot makes the read unavailable even if that
snapshot might otherwise have failed the line filter; it is not silently skipped.

## POS view

The production panel has independent filters, paging and errors. A cancellation
line can focus its original production evidence without changing cash amounts
or clearing the saved bill. Missing client capability is explicitly unconnected.
Changing subject, client, scope or lock cancels or ignores prior responses.

No reprint, refund, cancellation or configuration command is included. D01.4.1
still requires an active authorized employee PIN for each future reprint, a new
linked print job and audit, original-station routing and the existing Unknown
rules. Back Office and Stock remain separate add-ons. D21.3 approval is required
before merging these new read contracts.
