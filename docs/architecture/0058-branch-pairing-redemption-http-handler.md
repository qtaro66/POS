# ADR 0058: Branch pairing redemption HTTP handler

## Status

Accepted.

Defines HTTP result semantics for the Phase 0 pairing-redemption boundary without
publishing a route.

## Context

The versioned request and successful-response contract now exist, and the Branch
runtime can resolve the atomic redemption service. The transport boundary must
reject unsupported public shapes while avoiding an oracle that distinguishes a
missing, expired, consumed, replaced, cross-Branch, or wrong-secret challenge.

## Decision

- Accept the schema-version-1 redemption request and the composed
  `BranchPairingRedemptionService` at one handler boundary.
- Return `400 Bad Request` before service access when the schema version is not
  supported or the Branch ID or device display name is empty.
- Pass the Branch ID, pairing ID, one-time token, and device display name to the
  application service. That service retains canonical pairing parsing and input
  normalization ownership.
- Return one empty `401 Unauthorized` result when the application service cannot
  redeem the challenge. Malformed, missing, expired, consumed, replaced,
  cross-Branch, and wrong-secret challenges therefore remain indistinguishable.
- Return `200 OK` with the minimal versioned redemption response only after the
  store reports committed success and the response mapper validates the device
  and permanent credential formats.
- Forward cancellation and propagate persistence, corruption, and operational
  failures. Do not recast them as authentication failures or successful
  responses.
- Do not log or include the one-time token or permanent credential in errors or
  telemetry.
- Do not add endpoint routing, anonymous-access metadata, TLS enforcement, rate
  limiting, request-size limits, OpenAPI publication, or Mobile storage in this
  slice.

## Consequences

The future route can expose stable success and failure semantics without
learning why a validly shaped challenge was rejected. Unsupported protocol
versions and missing non-secret request fields remain distinguishable client
errors, while infrastructure failures remain observable to operations.

Automated tests use generated synthetic credentials and contact no database,
network, or hardware.
