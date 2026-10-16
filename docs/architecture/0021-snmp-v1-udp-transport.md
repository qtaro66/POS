# ADR 0021: Bounded UDP transport for SNMP v1

## Status

Accepted for local implementation; runtime composition remains deferred.

## Context

The bounded SNMP v1 protocol client needs a datagram boundary before it can query a real printer. The printer profile's existing port is the RAW TCP print port (normally 9100), so it must not be reused as the SNMP endpoint. A failed or delayed UDP exchange must also remain bounded and must not trigger an automatic retry.

## Decision

- Use a separate, validated SNMP UDP port; the conventional default is 161.
- Connect the UDP socket to the configured printer host so responses are accepted only from that peer.
- Apply independent connect and response timeouts while preserving caller cancellation.
- Send exactly one request and receive at most one datagram. Do not retry automatically.
- Reject empty requests, incomplete sends, empty responses, and requests or responses outside configured byte limits.
- Copy the received datagram before returning it across the transport boundary.
- Keep the socket session injectable. Automated tests use fake sessions and perform no DNS lookup, UDP traffic, or printer access.
- Do not register this transport in Branch API runtime composition yet. Community-secret injection and deployment configuration remain separate work.

## Consequences

This adds a production-capable network primitive without enabling hardware access in the running application. A later composition change must provide the community secret through an approved secret boundary, configure the SNMP endpoint and timeouts, and receive explicit approval before hardware probing is enabled.
