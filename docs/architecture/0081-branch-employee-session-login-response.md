# ADR 0081: Return employee-session credentials in a minimal versioned response

## Status

Accepted.

## Context

The application login boundary returns a plaintext employee-session token after
durable issuance. A future HTTP transport needs a stable public response without
leaking PIN input, trusted Branch identity, device identity, or persistence
details.

## Decision

- Define schema version 1 of `BranchEmployeeSessionLoginResponse` with only the
  employee identifier, public session identifier, session token, and UTC
  expiry.
- Map only canonical session identifiers and tokens, and require the token's
  embedded session identifier to match the separate public identifier.
- Reject blank employee identifiers and non-UTC expiry values.
- Exclude Branch ID, device ID, PIN, verifier, and internal lifecycle metadata
  from the public response.
- Do not define the PIN request body, HTTP status mapping, cache headers,
  endpoint metadata, rate limiting, cookies, transport header, or route
  activation in this slice.

## Consequences

Future login transport can serialize one minimal, versioned secret response
without reimplementing token consistency checks. Secret-response cache controls
and request-body handling remain mandatory later boundaries before any endpoint
can be activated.
