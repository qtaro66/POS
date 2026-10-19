# ADR 0052: Branch pairing QR payload format

## Status

Accepted.

Defines a deterministic Phase 0 QR payload carrying the Branch pairing
challenge without sending the one-time secret in the initial HTTP request.

## Context

D11 requires a pairing QR to include the HTTPS Branch Service URL, Branch
identifier, public `pairingId`, and random one-time token. Encoding the token in
the URL query would send it to the web server and commonly expose it to access
logs, reverse proxies, and diagnostics before the Mobile pairing flow consumes
it.

The versioned challenge response exists, but QR image generation and endpoint
exposure remain separate concerns.

## Decision

- Define the canonical payload as the Phase 0 HTTPS/IP origin followed by the
  fragment `#pair?v=1&branchId=...&pairingId=...&token=...`.
- Put all pairing fields in the URL fragment. Browsers do not send fragments in
  the HTTP request target, so the one-time token is not included in the initial
  request to the Branch Service.
- Percent-encode only the Branch identifier using the platform's canonical URI
  escaping. Keep the already canonical Base64url pairing ID and token unchanged.
- Require fixed field order, exact names, one occurrence of each field, schema
  version 1, a canonical HTTPS/IP origin, and a byte-for-byte canonical
  round-trip. Reject aliases, reordered or duplicated fields, noncanonical
  escaping, and unknown versions.
- Validate the pairing ID and token with the existing cryptographic format
  boundary and dispose parsed secret bytes immediately.
- Do not include employee identity, PIN, permanent device credential, expiry,
  or private-key material in the QR payload. Expiry remains authoritative at the
  Branch and is available separately in the challenge response for PC display.
- Do not generate QR images, open a route, log payloads, redeem tokens, create
  devices, or issue permanent credentials in this slice.

## Consequences

PC POS can later render one deterministic string as a QR code, and Mobile can
load the Branch PWA without placing the one-time secret in the HTTP request.
Browser history, screenshots, clipboard tools, and client-side telemetry can
still expose the fragment, so future UI code must avoid persistence and logging
and should replace the visible URL after consuming the payload.

Automated tests use generated synthetic tokens and contact no network, database,
or hardware.
