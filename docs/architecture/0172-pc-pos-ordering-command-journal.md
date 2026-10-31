# PC POS durable ordering command journal

Before enabling mutation controls, preserve request identity across browser
reloads and lost responses. Branch deduplicates by branch/request ID, so generating
a replacement ID for an uncertain basket can create another order.

The client exports/restores a canonical, credential-free immutable command
snapshot. Restore requires the original HTTPS service origin, branch, device and
employee identity; it retains the exact serialized body and UTC creation time.
Known action names determine endpoint paths. Unknown/extra fields, changed
context, malformed lines and bodies larger than 64 KiB fail validation. Reusing
an ID with another body in the current client is rejected.

A separate IndexedDB database stores snapshots and pending/resolved markers,
partitioned by origin/branch/device. Add-if-absent and conditional resolve/delete
operations use transactions that resolve only on commit. A conflicting payload
cannot overwrite an existing request. Credentials and Branch receipts are never
written. No Branch schema, command contract or business rule changes.

The coordinator journals before POST and fails closed on storage errors. Recovery
queries Branch only. An explicit retry queries first and may resend only an
unresolved original payload after NoDurableResult, using the original employee.
Unavailable lookup stops retry. A previously resolved command never resends.
SubmitOrder status must correlate to the original table session; OpenTable may
return the existing session, following its original semantics.

Resolved entries remain until the future UI applies the durable receipt and
explicitly finalizes the journal entry. This avoids silently deleting recovery
evidence before basket cleanup. A resolve-write failure cannot undo Branch
acceptance; return its durable result and a local-recovery warning flag.
NoDurableResult is not a business rejection or evidence that a pending command
was never sent. There is no automatic retry, request expiry or offline production.

Tests exercise real IndexedDB transaction semantics through fake-indexeddb 6.2.5,
the same pinned version already used by Mobile. Injected fetch handles all HTTP;
no Branch, printer or hardware access. Test coverage includes cross-instance
reload, collisions/transaction abort, identity partitioning, lost response,
storage failure, status correlation, manual retry and retained resolved records.

No mutation UI, basket persistence, authentication provisioning or real-service
integration is activated. Browser storage is unencrypted, evictable and within
the same-origin trust boundary; structural validation is not a tamper-proof
signature. Do not treat it as a backup or authority for acceptance/pricing.
The authentication/recovery boundary and local persistence format need D21.3
review and explicit user approval before merge.
