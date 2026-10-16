# ADR 0023: Disabled-by-default SNMP runtime configuration

## Status

Accepted for configuration binding only; runtime composition remains deferred.

## Context

The SNMP protocol, UDP transport, snapshot reader, condition mapping, and community-secret boundary now exist as isolated components. Before dependency-injection composition can safely use them, Branch API needs one validated configuration contract that distinguishes the SNMP endpoint from RAW TCP port 9100 and references secret environment-variable names without containing secret values.

## Decision

- Bind `SnmpPrinterStatus` in Branch API and validate it at startup.
- Keep `Enabled` false by default. Disabled configuration registers options only and performs no secret lookup, DNS resolution, UDP operation, polling, or hardware access.
- Configure a separate UDP port, connect timeout, response timeout, and request/response datagram limits.
- When enabled, require at least one exact `printerId` to environment-variable-name mapping.
- Require portable uppercase ASCII environment-variable names under the `POS_SNMP_COMMUNITY_` prefix; configuration stores names only, never community values.
- Do not register `ISnmpV1CommunityProvider`, `ISnmpV1DatagramTransport`, `IPrinterConditionProbe`, a cycle processor, or a hosted service in this slice.

## Consequences

Branch API can now reject incomplete enabled SNMP configuration without activating the network path. A later runtime-composition change must construct the already-tested components only when explicitly enabled and must preserve the disabled startup behavior. Real-printer probing remains a separately approved hardware action.
