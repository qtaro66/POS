# ADR 0061: Branch pairing redemption request limit

## Status

Accepted.

Adds a strict request-body limit to the still-inactive Phase 0 pairing
redemption endpoint.

## Context

The anonymous redemption route accepts a small JSON object containing one
Branch ID, pairing ID, one-time token, and device display name. Rate limiting
bounds request count, but it does not prevent one oversized payload from
consuming server buffers and JSON parsing resources.

## Decision

- Attach standard ASP.NET Core request-size metadata to the redemption endpoint.
- Limit the complete request body to 4,096 bytes. The canonical pairing fields
  require only a small fraction of this allowance, leaving room for JSON syntax,
  future compatible fields, and a reasonable device display name.
- Keep the limit as a public constant beside the route metadata so tests and
  future hosting code share one value.
- Let the hosting server reject oversized bodies before JSON model binding and
  before the redemption handler or SQLite store is reached.
- Return no pairing-specific details for an oversized request and never log its
  body.
- Keep the endpoint inactive. A later integration slice must verify the observed
  oversized-body response using the selected production server configuration.
- Do not change field-length policy, rate limits, Kestrel's global body limit,
  reverse-proxy limits, endpoint activation, or TLS configuration in this slice.

## Consequences

Every future mapped redemption route carries a testable 4 KiB body ceiling in
addition to its per-client attempt limit. Server or proxy limits may impose a
lower ceiling; a higher upstream limit does not override this endpoint metadata.

Automated tests inspect endpoint metadata in memory and contact no database,
network, or hardware.
