# POS Mobile PWA

Phase 0 Mobile client (React + TypeScript + Vite, requirement D17). This
foundation covers the application shell only:

- Device states: unpaired → paired but logged out → employee authenticated
  (requirement 3.3 / D11, D10.4).
- QR pairing entry and employee PIN login as UI flows; unexpected adapter
  failures are reported and the screen stays usable.
- Read-only active-table list with Branch connection, employee-data sync time
  and unsent-item (outbox) status (requirements 3.3, D10.1, D10.5, D20.1).

The delivery plan, pending decisions and required Backend capabilities are in
[BACKLOG.md](BACKLOG.md).

## Authenticated shell and accessibility

After login, `src/screens/AuthenticatedShell.tsx` shows three screens with a
bottom navigation (`src/components/BottomNav.tsx`): Tables, the current table
(after one is opened) and Status. The table list is loaded once per login
and shared by the screens; Refresh reloads it.

- Route state is kept in the browser history (`useShellNavigation.ts`), so
  the Android back gesture and the browser back button return to the previous
  screen. The URL never changes and a reload starts at the PIN login.
- Each screen (`src/components/Screen.tsx`) moves focus to its heading when
  shown, so screen readers announce it, and sets the document title.
- The table screen shows only what the last Branch list reports. Order lines
  and bill totals need a Branch capability that does not exist yet (CAP-10).
- The Status screen shows connection, employee-data sync time and the outbox
  counts by D20.1 state; the counts still come from fixtures.
- Targets are at least 44 px, focus is a 3 px outline, and text and control
  colors meet WCAG 2.2 AA in light and dark schemes;
  `src/accessibility.test.ts` checks the tokens and target sizes. Layout uses
  `rem`, wrapping and safe-area insets so it fits from 320 px wide, in
  portrait and landscape, at 200% text size.

## Adapters

All I/O goes through the interfaces in `src/adapters/ports.ts`. Only
deterministic fixtures (`src/adapters/fixtures.ts`) exist today; they contain
synthetic values only. Real adapters must call the Branch API through the
OpenAPI-generated client and must never open SQLite or reach printers.

The QR scanner port returns raw text. `src/domain/pairingQr.ts` parses it
against the Branch pairing QR contract, version 1
(`src/Branch/Pos.Branch.Api/Security/BranchPairingQrPayloadFormat.cs`):
canonical HTTPS IP origin, `#pair?v=1&branchId=…&pairingId=…&token=v1.…`, and
byte-for-byte canonical encoding. Change the Branch contract first, then mirror
it there and in `pairingQr.test.ts`.

When running the fixture build, the pairing button uses a built-in fixture QR
payload (RFC 5737 documentation address) and the fixture PIN is
`FIXTURE_ACCEPTED_PIN` in `fixtures.ts`.

## Branch API adapters

`VITE_MOBILE_ADAPTERS=branch` (build time) switches pairing and PIN login to
the Branch API (`src/adapters/branchApi/`); any other value keeps fixtures.

- `POST /api/v1/pairing/redemptions` and `POST /api/v1/employee-sessions`,
  schema version 1, HTTPS only, no cookies, no cache, redirects refused,
  10 s timeout. The device credential is sent as `Authorization: Device …`.
- The contract types are hand-typed from `Pos.Contracts.Security` until the
  endpoints are included in the OpenAPI description (D17).
- The paired device and its credential are kept in IndexedDB
  (`IndexedDbDeviceStore`); the employee session token is kept in memory only.
- The Branch Service answers 401 for every rejected pairing code and for a
  rejected PIN or revoked device, so those cases share one message each.
- Camera scanning, the active-table list and sync status still use fixtures:
  there is no camera adapter and no Branch endpoint for them yet.
- End-to-end use needs the Branch Service to serve this app from its own
  origin or to allow it through CORS; neither exists yet.

## Employee session expiry

The employee session ends at the `expiresAt` returned by the Branch login
response; Mobile never assumes a session lifetime of its own
(`src/domain/sessionExpiry.ts`, `src/components/useSessionExpiry.ts`).

- On expiry the in-memory session token is discarded without contacting the
  Branch Service, and the app returns to the PIN login with an expiry message.
  The device pairing and credential, and any locally held Draft/outbox items,
  are kept (requirement D10.5).
- Background timers can be delayed or suspended, so expiry is also rechecked
  when the app becomes visible, is restored from the page cache, regains
  focus, and before any pointer or keyboard input reaches the screen.
- A login that returns an already expired session is not opened. The Branch
  auth adapter also stops handing out a token at or after its expiry.
- There is no automatic token refresh: the employee enters the PIN again.
- Fixture sessions have no `expiresAt` and therefore never expire on the
  client. Pass `employee` with an `expiresAt` in `FixtureOptions` to exercise
  expiry with fixtures.

## Not implemented yet

Camera QR scanning, active-table and logout endpoints, token refresh,
IndexedDB draft/outbox, service worker and web app manifest, ordering,
`RequestPayment`, cancellation requests, and Thai localization (no Mobile
wording is confirmed in the requirements yet).

## Scripts

Run from the repository root: `npm run dev:mobile`, or
`npm run lint|test|build --workspace apps/mobile-pwa`.
