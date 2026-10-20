# ADR 0064: Branch pairing HTTPS request guard

## Status

Accepted.

Rejects plaintext requests to the reserved pairing route namespace before
redirect middleware, routing, model binding, or endpoint execution.

## Context

The global HTTPS-redirection middleware is useful for ordinary navigation, but
redirecting a pairing POST is not an acceptable security boundary. A Mobile
client must never submit a one-time token over plaintext HTTP and rely on a
redirect to retry over HTTPS. The pairing namespace therefore needs a
fail-closed guard before the existing redirect middleware.

## Decision

- When `BranchPairing:Enabled` is true, reject non-HTTPS requests whose path is
  within `/api/v1/pairing` with HTTP 400 and `Cache-Control: no-store`.
- Return no response body and no `Location` header.
- Run the guard before global HTTPS redirection, routing, rate limiting, model
  binding, and endpoint execution. Do not read the request body.
- Leave unrelated paths under the existing global HTTPS-redirection behavior.
- Leave both pairing endpoints unmapped.
- Do not configure Kestrel listeners, certificates, IP SAN validation, Private
  CA installation, forwarded headers, or firewall rules in this slice.

## Consequences

An enabled Branch process cannot treat plaintext pairing requests as a redirect
path into the secure API, and sensitive request bodies are not consumed by the
application. Deployment must still expose the future pairing endpoints only on
the reserved-IP HTTPS listener with the correct certificate and network rules.

Automated tests use in-memory HTTP contexts and pipelines only. They contact no
network, database, or hardware.
