# PC POS transactional basket storage

Prerequisite for a safe visible basket editor: a browser reload must retain its
unsubmitted lines, and a sent basket must not become editable while its original
command is unresolved. Clearing a journal entry before clearing an accepted
basket can otherwise create a duplicate order after a crash.

Local IndexedDB schema 2 adds baskets to the existing command journal database.
Migration preserves schema-1 commands and their exact payloads; Branch SQLite,
API contracts and business rules are unchanged. A shared transaction helper
resolves writes only on transaction completion and fails closed when unavailable
or blocked. Older schema-1 clients cannot open a migrated database; deploy the
updated app coherently rather than silently deleting or downgrading storage.

Each basket retains origin/branch/device/employee/table-session scope, frozen
sessionMasterDataVersion, a unique basket ID, local revision and normalized unpriced lines.
Unknown caller price/token fields are not persisted. Quantities, fulfillment,
options and comments use the same normalization as prepared SubmitOrder commands.
An editable basket may be empty; line JSON is capped at 64 KiB. A prepared full
command still must satisfy its own existing 64 KiB body limit.

Save/delete require compare-and-swap against the caller's read basket ID and
revision, captured before asynchronous work. A recreated basket has a new ID,
so an old tab cannot overwrite it even if its revision counter starts at 1.
Reject stale concurrent edits, changing the pinned master version, and edits/deletion
of a command-bound basket. Origin/device/employee partitioning is data scope,
not authentication or authorization against another same-origin script.

Staging validates a prepared SubmitOrder snapshot against the exact saved lines
and identity. In one read-write transaction, insert its journal entry and bind
the basket to the original request ID. A collision or any write failure aborts
both writes. No network call is made. The future explicit submission action can
then use the existing recovery retry method, which queries before sending.

Reconciliation requires a resolved journal marker and a genuine correlated
durable Branch response supplied by the authenticated recovery adapter. A valid
acceptance deletes the basket and its command together. Rejection retains the
original basket/version for review, advances its revision and unbinds it while
deleting the command atomically. Unknown/unavailable/NoDurableResult or mismatched
responses cannot clear either record. Standalone journal finalization refuses
entries referenced by a basket. Local markers/receipt validation are not a
cryptographic proof against malicious same-origin code.

Tests cover reloads, owner scope, concurrent revisions, frozen metadata, pending
edit/delete guards, identity/payload collisions, lost-response recovery, rejection
retention, schema-1 migration and transaction abort after intermediate writes.
All database faults use isolated fake-indexeddb instances and injected fetch;
no Branch database, production service, printer or cash drawer is contacted.

No UI editor, production session provisioning, employee takeover, retention
policy or automatic submission is activated. The host must validate current
session state, menu/availability and permissions before staging or reviewing a
rejection; Branch remains the final authority. This is not completion of the
entire D10.5 lifecycle. Browser storage remains unencrypted and evictable.
D21.3 explicit approval is required before merge for local schema migration and
the basket/recovery identity boundary.
