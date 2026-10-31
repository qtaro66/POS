# PC POS web client

React/TypeScript/Vite client for the branch cashier PC. It now includes a
protected read-only active-table and sent-order workspace. Production
login/pairing and order-entry integration are not wired up yet.

## Read-only workspace

`App` accepts an authenticated ordering client through its `client` prop and a
Branch-provided display label through `branchLabel`. Create a stable client after
successful authentication and replace it when the employee session changes.
The default entrypoint supplies no client, so it shows the sign-in state and
performs no Branch requests. Do not supply credentials through URLs, Vite
environment variables, global browser objects, or the development preview.

The workspace lists active sessions only, reads their accepted batches, groups
lines by DineIn/Takeaway, and shows persisted option/comment/cancellation data.
All financial values come from the Branch projection. No local totals are
calculated; missing/empty bills are not replaced with invented values. The
desktop order list scrolls independently while its current totals stay visible.

Manual refresh rereads both the active list and selected detail even when the
table state-transition version has not changed. Old financial content is cleared
before any refresh and stays hidden after read errors. Obsolete responses and
responses from a replaced client cannot restore old table data. Reads are
cancelled when the screen unmounts or is locked. There is no live-update/polling
claim: the screen labels amounts with the server read timestamp.

The screen-lock button hides data and blocks reuse of that same supplied client
within the mounted App. It does not stop the Branch Service or revoke server
credentials. A production authentication host must verify PIN and supply a fresh
authenticated client for re-entry; this slice does not implement that PIN flow,
auto-lock policy, or lock diagnostics. No transaction/payment state is changed.

The screen exposes no order, price, cancellation, payment or printer mutations.
It is not a complete floor plan and has no available-table inventory, basket,
menu entry, shift controls, history or device commissioning UI yet.

For local visual review only, run the dev server and open
`http://127.0.0.1:5173/?preview=sent-orders` (adjust the port if needed). The preview
is prominently marked as synthetic, has no credentials and uses no Branch or
printer transport. Its data module is dynamically imported behind Vite's DEV
flag and is absent from the production build. Reloading a preview is not a
production login/unlock workflow.

## Ordering API adapter

`src/api/orderingClient.ts` connects to the opt-in
[Branch ordering v1 API](../../docs/api/branch-ordering-v1.md). It supports:

- Preparing an OpenTable or whole-basket SubmitOrder command.
- Sending an immutable prepared command with its original request ID and UTC
  creation time, including on an explicit manual retry.
- Looking up a durable command result by request ID.
- Reading active table sessions and the session-frozen menu/current availability.
- Reading accepted batches, line/option details and authoritative bill-preview
  totals with `sessionDetail(tableSessionId)`.

Create the adapter only after the application has an authenticated device and
employee session. Pass that session in memory; never put credentials in Vite
environment variables, URLs, logs, localStorage, or source code. Credentials and
identity are snapshotted at creation. Discard the client when the employee
session ends or changes. Runtime prepared commands cannot cross client instances;
validated journal snapshots can be restored only by the original identity.

The adapter never automatically retries a mutation. Only a correlated, valid
HTTP 200/409 command receipt becomes durable. Timeouts, network failures, 503s
and malformed/mismatched replies remain uncertain. Transport 400/401/403/413
replies are blocked, not durable business rejections. A command-query 404 with
NoDurableResult remains unresolved, not accepted or rejected. Reconcile using
`client.commandStatus(command.requestId)`; do not create a new order on uncertainty.

Prepared commands retain a private immutable serialized basket snapshot.
Use the recovery adapter below for persistence before sending. Do not use the
low-level memory-only `execute` method alone for production sales.

Requests use HTTPS only, omit cookies, disable caches and reject redirects.
Default timeout is 10 seconds and response JSON is bounded to 2 MiB, configurable
through transport options. Request bodies must fit the server's 64 KiB limit.
Identifiers consisting solely of `.` or `..` are rejected to prevent URL dot
segment normalization. Other basic normalization and 128-character identifier
limits match the API.

No prices, totals, tax, or batch IDs are calculated or supplied by the client.
Both DineIn and Takeaway are retained per line. Server menu prices and
availability flags are returned unchanged. The includesAvailableTables=false
flag remains explicit; the active list is not a complete floor plan.

Session detail retains server-provided quantities, cancellation state, fulfillment
types, batch identifiers and monetary values unchanged. Empty/all-cancelled
sessions have null bill-preview totals. A missing, mismatched or malformed detail
response is not replaced with basket-derived or zero-valued bill data. These
preview totals are not payment balances or receipts.

## Durable command recovery adapter

`createCommandJournal()` in `src/ordering/commandJournal.ts` uses a separate
IndexedDB database, `pos-pc-ordering-journal` version 1. It stores original
OpenTable/SubmitOrder payloads, IDs, UTC creation times, origin/branch/device/
employee identity and a pending/resolved marker. It never stores authentication
tokens or receipts. This local format is independent of Branch SQLite schemas.

Wrap an authenticated ordering client with
`createCommandRecovery(client, journal)` from `src/ordering/commandRecovery.ts`:

- `send(prepared)` waits for the journal transaction to commit before POST.
  Storage errors stop sending. Reusing a journaled ID requires recovery/retry;
  conflicting payloads cannot overwrite it.
- `list()` includes pending and resolved entries for the current origin, branch
  and terminal. Constructing/listing does not contact Branch or send orders.
- `recover(requestId)` performs a fresh GET status lookup only. NoDurableResult
  remains unresolved. SubmitOrder receipts must match the original session;
  OpenTable may legitimately resolve to a previously opened session.
- `retry(requestId)` first checks Branch. Only an explicit call, after
  NoDurableResult for a pending entry, can POST the original exact bytes and ID.
  Unavailable/malformed lookups stop it. The original employee must be signed in
  for resend; a different authorized operator on the same terminal may query.
  A previously resolved entry is never resent, even if Branch later lacks it.
- Durable results are retained until `finalize(requestId)` after the caller
  applies the result to its basket/workspace. Pending entries cannot be finalized.
  A failed local resolve write returns the real durable Branch result with
  `recoveryRecorded: false`; keep the entry and reconcile rather than resubmit.

The canonical snapshot codec rejects unknown fields, malformed/context-mismatched
or oversized payloads and derives paths from the action. It does not authenticate
browser storage against a malicious same-origin script. IndexedDB is unencrypted,
can be evicted or cleared, and is not a Branch backup. There is no automatic
replay, expiry or background resend. The future UI must prevent a fresh order ID
for an unresolved basket and coordinate applying receipts with local basket
cleanup; that UI, basket persistence and session provisioning are not implemented
here. This adapter is not connected to App's read-only entrypoint.

## Verification and remaining work

An authenticated host may opt into the command-inspection drawer with
`<App client={orderingClient} journal={createCommandJournal()} />`. The typed
props require a full ordering client when supplying a journal; the recovery
adapter is constructed internally from that same client. The existing read-only
client integration remains supported without a journal. Default production
startup still supplies no authenticated client and remains at sign-in.

The drawer lists original command IDs, session/employee identity, UTC timestamps,
item/option codes, quantities, fulfillment and comments. Local resolved markers
are explicitly historical, not fresh acceptance. Each operator-triggered check
performs GET status only; it shows accepted/rejected/unresolved/unavailable
outcomes and local-write warnings. No retry, delete, finalize, receipt, payment
or printing control is exposed. Resolved entries remain for future basket
reconciliation. Closing, refreshing, locking and changing client identity cancel
or invalidate obsolete reads. Storage errors are distinct from empty lists.

For synthetic visual review, run the development server and open
`?preview=command-recovery`. The injected preview handles all reads in memory,
disables mutations/deletion and makes no external fetch or IndexedDB writes.
Its fixture module is excluded from production builds. Item codes are shown
because snapshots do not contain authoritative display names or current prices.

```powershell
npm run test --workspace apps/pos-web
npm run lint --workspace apps/pos-web
npm run build --workspace apps/pos-web
```

Adapter/controller tests use Node's built-in runner. React screen tests use
Vitest/jsdom and Testing Library, at the exact versions already used by the
Mobile workspace. Tests never contact a Branch, printer or production service.
The root verification gate includes both test suites automatically. Node 24 or
newer is the repository baseline.

Types are currently handwritten mirrors of the v1 DTOs, with runtime shape and
identity validation. OpenAPI-generated client tooling, real Branch integration,
credential UI/lifecycle, order-entry screens, and visible recovery/basket
coordination remain follow-up work. The workspace uses the supplied client's read methods;
this does not activate backend features or provide a production session provider.
