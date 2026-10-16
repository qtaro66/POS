# ADR 0022: Environment-backed SNMP v1 community secret leases

## Status

Accepted for local implementation; runtime composition remains deferred.

## Context

SNMP v1 community values are credentials even though the protocol transmits them without encryption. They must not be committed in configuration, retained in long-lived option objects, included in logs or exceptions, or cached across requests. The protocol client also needs explicit ownership so it can clear its working secret bytes after success or failure.

## Decision

- `ISnmpV1CommunityProvider` returns a disposable, caller-owned `SnmpV1CommunitySecret` lease rather than bare memory.
- Disposing the lease zeroes its byte buffer and prevents later access.
- The protocol client disposes every lease and clears its encoded request and response working copies in `finally` blocks.
- `EnvironmentVariableSnmpV1CommunityProvider` maps exact `printerId` values to environment-variable names. Configuration may contain those names but never the community values.
- The provider reads the environment for every request so an externally rotated value is not cached by the process.
- Community text is UTF-8 encoded and must occupy 1-255 bytes. Exceptions do not include the printer identifier, environment-variable name, or secret value.
- Automated tests inject an in-memory environment source. They do not read or mutate the process environment and perform no network or hardware access.

## Consequences and limitations

Managed environment APIs expose the value as an immutable .NET string before encoding, so the process cannot deterministically erase that temporary string. The owned byte buffers are still cleared deterministically, which reduces retention but does not make SNMP v1 cryptographically secure. Deployment must protect process environment access and the branch network.

This slice does not bind mappings from application configuration, register SNMP services, enable polling, or contact a printer. Runtime composition must remain disabled by default and requires explicit approval before hardware probing.
