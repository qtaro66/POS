# ADR 0065: Branch pairing secret response headers

## Status

Accepted.

Prevents HTTP caches from retaining pairing responses that contain bearer
secrets before either pairing endpoint is activated.

## Context

Pairing challenge issuance returns a one-time token, and successful redemption
returns a permanent device credential. Both values are bearer secrets. HTTPS
protects them in transit but does not, by itself, prevent a browser, intermediary,
or diagnostic cache from retaining the response.

## Decision

- Apply `Cache-Control: no-store` and `Pragma: no-cache` at the start of both
  pairing HTTP handlers.
- Apply the headers to success, validation failure, rejected challenge, and
  operational failure paths reached through those handlers.
- Keep the response bodies and status semantics unchanged.
- Leave both pairing routes unmapped.
- Do not add secret values to logs, telemetry, exception messages, or cache
  keys.

## Consequences

Future mapped pairing responses carry explicit cache prevention even when a
request fails after entering a handler. Deployment and clients must still avoid
persisting or logging one-time tokens and permanent device credentials outside
their intended secure storage boundaries.

Automated tests use synthetic secrets and in-memory HTTP responses only. They
contact no network, database, or hardware.
