# PC POS web client

React/TypeScript/Vite client for the branch cashier PC. The visible application
is still the initial scaffold; production ordering screens and login/pairing UI
are not wired up yet.

## Ordering API adapter

`src/api/orderingClient.ts` connects to the opt-in
[Branch ordering v1 API](../../docs/api/branch-ordering-v1.md). It supports:

- Preparing an OpenTable or whole-basket SubmitOrder command.
- Sending an immutable prepared command with its original request ID and UTC
  creation time, including on an explicit manual retry.
- Looking up a durable command result by request ID.
- Reading active table sessions and the session-frozen menu/current availability.

Create the adapter only after the application has an authenticated device and
employee session. Pass that session in memory; never put credentials in Vite
environment variables, URLs, logs, localStorage, or source code. Credentials and
identity are snapshotted at creation. Discard the client and prepared commands
when the employee session ends or changes. Commands cannot be sent through a
different client instance.

The adapter never automatically retries a mutation. Only a correlated, valid
HTTP 200/409 command receipt becomes durable. Timeouts, network failures, 503s
and malformed/mismatched replies remain uncertain. Transport 400/401/403/413
replies are blocked, not durable business rejections. A command-query 404 with
NoDurableResult remains unresolved, not accepted or rejected. Reconcile using
`client.commandStatus(command.requestId)`; do not create a new order on uncertainty.

Prepared commands are memory-only and retain a private serialized basket
snapshot. They cannot be reconstructed across page reloads in this slice;
durable client recovery/request-ID storage belongs to subsequent UI work. Do not
use this foundation alone for production sales.

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

## Verification and remaining work

```powershell
npm run test --workspace apps/pos-web
npm run lint --workspace apps/pos-web
npm run build --workspace apps/pos-web
```

Tests use Node's built-in runner and injected fetch responses; they never
contact the network, printers, or a production Branch. The root verification
gate includes this workspace automatically. No new dependency is required;
Node 24 or newer is the repository baseline.

Types are currently handwritten mirrors of the v1 DTOs, with runtime shape and
identity validation. OpenAPI-generated client tooling, real Branch integration,
credential UI/lifecycle, accessible ordering screens, and durable request
reconciliation remain follow-up work. This adapter is not imported by App.tsx
and does not change the visible screen or activate any backend feature.
