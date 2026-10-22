# POS Mobile PWA

Phase 0 Mobile client (React + TypeScript + Vite, requirement D17). This
foundation covers the application shell only:

- Device states: unpaired → paired but logged out → employee authenticated
  (requirement 3.3 / D11, D10.4).
- QR pairing entry and employee PIN login as UI flows; unexpected adapter
  failures are reported and the screen stays usable.
- Read-only active-table list with Branch connection, employee-data sync time
  and unsent-item (outbox) status (requirements 3.3, D10.1, D10.5, D20.1).

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

## Not implemented yet

Camera QR scanning, Branch API adapters, persistent device-credential storage,
IndexedDB draft/outbox, service worker and web app manifest, ordering,
`RequestPayment`, cancellation requests, and Thai localization (no Mobile
wording is confirmed in the requirements yet).

## Scripts

Run from the repository root: `npm run dev:mobile`, or
`npm run lint|test|build --workspace apps/mobile-pwa`.
