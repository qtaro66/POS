# Original kitchen print evidence in cash history

This extends D18.8 and D01.4 with read-only observations of the original kitchen
print job and its recorded send attempts. D20 uncertainty rules remain unchanged.
No print, retry, reprint, acknowledgement or drawer command is introduced.

## Original source and scope

Resolve the authenticated Branch's captured cash request and its table session,
then resolve the selected original production snapshot with the existing exact
UTF-8 content-hash and payload validation. Read only the KitchenOrder job whose
snapshot, session, submit request and original station agree with that source.
Never reconstruct those identities using current menu or printer configuration.

Current schema 35 guards and uniqueness permit one original kitchen job for a
snapshot/station. Return a nullable original job rather than a speculative
paginated list of future reprints. Missing evidence means no matching original
job was read, not proof that printing never occurred. Selected reprint metadata,
inconsistent identities or excess original rows are unavailable. This contract
does not claim compatibility with a future reprint execution/storage design.

Preserve recorded job identity, request, snapshot, station, status, attempt count
and creation time. Creation time is its own observation and is not replaced by
the production snapshot's submitted time. Assigned printer, worker, lease data,
network addresses, raw errors, payload bytes and hashes are not exposed.

## Attempts and uncertainty

Read attempts only after resolving that original job inside the same scoped
transaction. Preserve attempt identity, recorded number, state, automatic-retry
flag and start/first-byte/finish times. Reject malformed selected types, unknown
states, invalid counts, missing required lifecycle timestamps and unsafe retry
flags. A Failed observation must precede first-byte evidence; automatic retry
can only be recorded for such a Failed attempt. Unknown never permits it.

Do not invent a monotonic clock order or replay today's dispatch state machine
to rewrite old observations. Connecting/Dispatching are live observations;
terminal states retain their recorded evidence. PrintDispatched means data was
sent, not that paper was confirmed. A legacy Completed value is displayed as a
recorded status, without claiming the Phase 0 hardware capability is verified.

The retry flag is recorded evidence, not authorization for a staff action.
Unknown remains uncertain and is never automatically retried by this view.
Any future reprint must follow its separate PIN/permission, original reference,
audit and new-job requirements.

## Continuation and POS behavior

Attempt pages use descending recorded row sequence and a version-1 high-water
cursor bound to Branch, cash capture, table session, snapshot, original job,
source, limit and SQLite schema cookie. Later attempts appear after refresh.
The opaque cursor uses a SHA256 fingerprint of its ordered subject identities
so valid long Unicode identifiers cannot overflow the continuation bound. This
unkeyed fingerprint binds navigation scope; authorization remains in the queries.
High-water membership does not freeze mutable job/active-attempt status: each
page is a fresh observation with its own asOf. Immutable source identities must
still agree when an observed job status or attempt count changes.

DDL/VACUUM invalidates the cursor. Manual database replacement, restore or
deletion requires a fresh traversal. Cursor positions never authorize access.
Existing cash, event and production continuations remain independent.

Bill lines and cancellation evidence can focus the original production view.
Within its detail, original job evidence and attempt paging load independently
from cash/bill/production data. Errors remain local; missing older capabilities
are explicitly unconnected. Reader, subject, scope, selection, refresh and lock
changes abort or ignore stale responses. No action button sends or reprints data.

## Validation and governance

Safe tests exercise actual submission/cash and dispatch-store transitions with
fake adapters or local fixture data. They check pre-send failure, first-byte
progress, Unknown, successful dispatch, scoped reads, corrupt snapshots, source
validation, high-water/maintenance binding and unchanged database fingerprints.
No physical printer, network printer endpoint or production data is exercised.

No database migration, dependency or production printing behavior changes.
Branch schema 35 and Central schema 3 remain unchanged. D21.3 explicit approval
is required before merging these authenticated read contracts. Reprint execution,
hardware acceptance, refunds and Back Office/Stock remain separate work.
