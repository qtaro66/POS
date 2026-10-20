# ADR 0062: Branch pairing service-origin configuration

## Status

Accepted.

Makes the Phase 0 Branch service origin an authoritative, startup-validated
configuration value before either pairing endpoint is activated.

## Context

The issuance handler must place the Branch service URL in the QR challenge.
Accepting that value from an HTTP parameter would let an untrusted caller choose
the origin embedded in a newly issued QR payload. Phase 0 instead requires one
operator-controlled HTTPS URL using the Branch's DHCP-reserved IP address.

## Decision

- Add `BranchPairing:ServiceOrigin` beside the existing pairing options.
- When pairing is enabled, fail startup unless the value is an absolute HTTPS IP
  origin with no credentials, path, query, or fragment.
- Resolve the issuance handler's service origin from validated options rather
  than from request data.
- Keep pairing disabled by default and leave both pairing routes inactive.
- Do not configure Kestrel listeners, certificates, certificate SAN validation,
  firewall rules, rate-limiter middleware, or endpoint mapping in this slice.

## Consequences

An enabled pairing runtime has one validated source for the URL encoded in QR
challenges, and a request cannot substitute another destination. Deployment
must still bind HTTPS to that origin and install a certificate containing the
reserved IP in its SAN before route activation.

Automated tests use temporary databases and synthetic private-network IP
origins. They contact no network, database outside the temporary test scope, or
hardware.
