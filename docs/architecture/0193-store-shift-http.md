# Store and shift HTTP commands

The D04.11 lifecycle uses the Branch's authenticated PC POS identity and
per-command active employee PIN validation. Opening a store also requires the
OpenStore permission published by Central. Older employee permission snapshots
omit this field and default to no permission.

The Branch determines its timestamp and local Business Date. The request
identifies the store, shift and idempotency key, operation and optional cash
amount. The employee identified by the presented PIN is the audited actor.
The same authenticated operator can submit a command with another active
employee's PIN under the D04.11.5 handover rules.

A retry with the same business input returns the original durable result.
Server-generated timestamps from a retry preserve the original audit time.
Changed business input with the same request ID conflicts. PIN values stay
outside the persisted command and result.

The read path requires an authenticated PC POS identity and uses that Branch
when reading the requested store. The read itself does not request a PIN.
Production payment entry, POS screens and lifecycle Central event delivery
remain integration tasks.

Validate at HTTP level with missing or invalid identity, device role, invalid
PIN, absent OpenStore permission, a distinct active PIN actor, malformed money,
replay after clock advancement and cross-Branch status lookup. Validate the
Central permission source and older snapshots independently.

Routes under the existing ordering identity policy:

- GET /api/v1/ordering/store-lifecycle/{storeSessionId}
- POST /api/v1/ordering/store-lifecycle

The POST body contains RequestId, StoreSessionId and ShiftId as UUID strings,
Operation as OpenStore, OpenShift, CloseShift or CloseStore, CashAmount as a
nonnegative decimal with at most two fractional places or null, and Pin.
OpenStore and CloseStore require CashAmount. Responses use the durable
StoreLifecycleResult, with optional Closing snapshot.

GET returns 404 for an unknown store in the authenticated Branch. POST returns
400 for malformed input, 401 for an invalid active employee PIN, 403 for an
unsupported device role or missing opening permission, 409 for an invalid
transition or changed replay payload, and 503 for a database availability error.
The routes return Cache-Control no-store.
