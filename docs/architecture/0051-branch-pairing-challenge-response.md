# ADR 0051: Branch pairing challenge response

## Status

Accepted.

Defines the versioned public response that can later be encoded into a Phase 0
QR pairing presentation without exposing an HTTP route.

## Context

D11 requires the QR challenge to carry the HTTPS Branch Service URL, Branch
identifier, public `pairingId`, and five-minute one-time token. Phase 0 uses a
DHCP-reserved IP address whose certificate contains the same IP SAN. The
transport boundary must not leak the creator employee identifier, PIN, a
permanent device credential, or any private-key material.

Issuance is already composed behind disabled-by-default configuration, but an
endpoint cannot be exposed safely until the authenticated employee boundary and
durable audit behavior exist.

## Decision

- Add a schema-version-1 `BranchPairingChallengeResponse` containing the Branch
  Service URL, Branch ID, canonical pairing ID, one-time token, and UTC expiry.
- Accept only an absolute HTTPS IP origin for the Phase 0 service URL. Reject
  hostnames, credentials, paths, queries, fragments, and non-HTTPS schemes.
- Normalize the accepted origin to an absolute URI ending in `/` while retaining
  an explicit non-default port.
- Validate the canonical pairing ID and one-time-token format at the mapper
  boundary and dispose the parsed secret immediately after validation.
- Exclude the creator employee ID and all permanent credentials from the public
  response.
- Do not define QR image generation, a serialized QR payload, endpoint routing,
  authorization, employee sessions, or audit persistence in this slice.

## Consequences

The future authenticated issuance endpoint can return a minimal stable response
whose fields are safe for the PC POS to present as a QR challenge. The endpoint
must still derive Branch and employee identity from authentication, record the
issuance audit durably, and avoid logging the one-time token.

Automated tests use generated synthetic tokens and contact no network, database,
or hardware.
