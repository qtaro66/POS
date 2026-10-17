# ADR 0025: Disabled-by-default print runtime composition

## Status

Accepted for Branch API composition; deployment enablement and hardware commissioning remain separate operations.

## Context

The persisted print worker, ESC/POS raster renderer, RAW TCP adapter, safe dispatcher, and SNMP condition probe now exist as separately tested components. The Branch API still needs one explicit composition boundary that cannot silently activate against a printer or an unprovisioned database.

## Decision

- `PrintSpoolPolling:Enabled` remains the single activation switch for the hosted print worker and defaults to `false`.
- `AddPrintRuntime` always registers startup validation but registers no stores, printer provider, transport, dispatcher, persisted worker, or cycle processor while polling is disabled.
- When polling is enabled, `PrintRuntime:DatabasePath` must be an absolute path to an existing Branch database file. This boundary does not inspect or migrate its schema.
- When SNMP is enabled, the runtime uses the composed SNMP condition probe. Otherwise it registers a fallback that returns `StatusUnsupported` only for profiles explicitly configured with `PrinterStatusMode.Unsupported` and returns `Unknown` for every status-capable profile, causing dispatch to remain blocked.
- The enabled composition registers the SQLite dispatch and rendered-payload stores, SQLite printer configuration provider, RAW TCP ESC/POS adapter, safe dispatcher, persisted worker, and exactly one polling cycle processor as singletons.
- Composition and dependency resolution do not initialize or mutate the database, read community secrets, resolve DNS, open TCP/UDP sockets, poll status, send bytes, print paper, or operate a cash drawer.
- Configuration is startup-only. Enabling or changing it requires restarting the Branch API.

## Consequences

The runtime now fails closed at startup when its database-file prerequisite is incomplete and fails closed per job when a profile requires an unavailable condition probe. Default development and CI configuration remains inert. A deployment may activate real polling only after provisioning and migrating the Branch database, printer profiles, bundled font paths, any required environment-held SNMP communities, and explicit operator approval.

Automated tests inspect validation and service registrations only. They do not start the enabled hosted worker or contact hardware.

## Deliberately outside this slice

- automatic database creation, migration, or printer-profile provisioning
- production deployment enablement
- physical Windows/Linux printing and SNMP commissioning
- lease renewal during long rendering or network operations
- IPP/LPR adapters, cash-drawer commands, and job-specific completion acknowledgement
