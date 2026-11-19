# Store and shift domain lifecycle

D04.11 defines one store session with one active shift at a time.
OpenStore creates the first shift and requires a nonnegative opening float.
Opening a subsequent shift preserves the session and Business Date.
Its optional float is handover information.

Closing uses cumulative effective committed cash applied across the store
session, added to the original opening float. An omitted mid-day count produces
NotReconciled with null actual and difference. Store closing requires a count.
Closing snapshots preserve the employee, terminal and Branch time.
A partially paid table prevents closing a shift or store.

The domain foundation establishes these transitions and reconciliation
snapshots. Integration must load the current state and committed cash total
inside the same Branch transaction, serialize competing commands, persist the
snapshot and audit with deduplication, and publish the corresponding outbox
event atomically. Rehydrate the same IDs after restart.

Before invoking the domain, the Branch authenticates an active employee's PIN
for each command and checks the extra OpenStore permission. It resolves the
Branch timezone and local date. The POS shows the final actual count for
confirmation before committing.

Remaining integration includes Branch storage and endpoints, authentication,
the PC POS store-and-shift screen, reporting and Central delivery. The deployed
acceptance sequence remains in
[the combined Windows runbook](../pc-pos-cash-and-day-close-acceptance.md).
