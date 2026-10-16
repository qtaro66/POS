# ADR 0024: Inert SNMP runtime composition

## Status

Accepted for dependency-injection composition; print-worker integration remains deferred.

## Context

Branch API has a disabled-by-default, startup-validated SNMP configuration contract. The protocol, UDP transport, community-secret lease, profile-specific snapshot reader, and condition evaluator can now be composed, but composition must not itself resolve secrets or contact a printer.

## Decision

- `AddSnmpPrinterStatus` always registers configuration validation.
- When `SnmpPrinterStatus:Enabled` is false or absent, it registers no SNMP runtime services.
- When enabled, it registers singleton implementations for the environment-backed community provider, bounded UDP transport, cryptographic request IDs, bounded SNMP v1 client, TM-T82X/M352A snapshot reader and evaluator, and `IPrinterConditionProbe`.
- Registration and service construction perform no environment-secret read, DNS resolution, UDP exchange, polling, printing, or hardware access.
- Do not register a hosted service or `IPrintSpoolCycleProcessor` in this slice.
- Treat `Enabled` and its option values as startup configuration. Changing them requires a process restart; runtime hot switching is not supported.

## Consequences

The complete printer-condition probe can now be resolved by a later print-worker composition boundary. The running Branch API remains unable to initiate SNMP work because no hosted process or print cycle consumes the probe. The next integration must preserve disabled defaults and must not silently activate printer access without complete print-worker configuration and explicit approval.
