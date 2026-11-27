# Durable single-line kitchen reprint queue

Implements the backend continuation of 0215 for D01.4.1, D18.2-D18.3 and
D20. The command requires provisioned PcPos identity and a fresh active PIN
approver with the latest cached ReprintKitchenOrder permission on every POST,
including an idempotent replay. Requester and approver can differ; neither
the session nor its claims change. PINs never enter persistence or responses.

## Atomic capture and recovery

The authenticated Branch resolves a captured cash sale, its original production
snapshot, exactly one original line and the original KitchenOrder job. Existing
production evidence validation checks the source hash and relational identity.
Copy the submitted line, including its full original quantity, options, notes,
table and station. Cancellation or current menu/table changes do not rewrite
this history. A cancelled line remains a historical original line.

One SQLite transaction saves a derivative snapshot, a linked KitchenOrder job,
an idempotency record and an immutable Queued audit event. Both requester and
PIN approver, device, reason, time and original/new references are retained.
The existing print_attempts ledger supplies sending/terminal outcomes through
the new job reference. Do not interpret Queued as a physical-print result.
The transaction changes no order, cancellation, stock, cash or outbox state and
never edits or retries the original print job, including an Unknown original.

The branch-scoped command ID binds all target, actor and reason fields. An
identical retry returns the original receipt; a changed command conflicts.
QueuedAt is server time on first acceptance, not a replay comparison field.
Status reads bind the authenticated requester and device and require no PIN.
They validate receipt, saved command, snapshot metadata and audit agreement;
inconsistent or incorrectly typed stored records fail closed. Replays also
validate that agreement instead of accepting a corrupted ledger as a receipt.

## Migration and rendering

Schema 36 adds kitchen_reprint_snapshots, kitchen_reprint_requests and
kitchen_reprint_audit with foreign keys and update/delete guards. Replaces
print-job reference guards to allow only linked copies of an original job in
the same Branch/session/station. Existing print_jobs and attempts are not rebuilt.
The migration is transactional. Schema readiness fixtures move from 35 to 36.

The payload store resolves copies only under an active lease and verifies their
snapshot hash and source relationships. PrintDocumentKind.KitchenOrderReprint
is appended without changing existing enum values. The raster renderer requires
exactly one line and forces a visible copy label. Kitchen fulfillment parsing
accepts the actual submit writer's dineIn/takeaway values and legacy numeric 0/1;
nullable option groups remain compatible with original snapshot content.

The existing spool saves rendered bytes before sending and routes through the
original station's active printer configuration. Unknown never automatically
retries. The hardware-free integration test runs the real queue, payload store,
raster renderer, dispatcher and worker using a capturing rasterizer and fake
transport which fails after first-byte notification. It checks copy labelling,
frozen content, retained payload/attempt, scoped status, idempotent replay and
no second dispatch or original business mutation.

## Remaining boundaries

The PC POS reprint button, command-local PIN form and browser command recovery
are subsequent UI work. Existing cash-history browser regression remains read
only; it includes the three new tables in its business fingerprint. No real
printer, drawer, network printer endpoint or production database is exercised.
D21.3 explicit user approval is required before merge for the new contracts,
schema migration, printing and security behavior.
