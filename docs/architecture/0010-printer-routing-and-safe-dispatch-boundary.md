# ADR 0010: Printer routing configuration and safe dispatch boundary

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.1-D13.4 and D20.3-D20.4
- Extends: ADR 0009

## Decision

The Branch database owns the mapping from an exact, case-sensitive `branchId + productionStationId` to one active `printerId`. The permanent printer identifier is independent of its mutable hostname/IP address. A partial unique index prevents two active printers from serving the same station at the same Branch.

The versioned printer profile stores the protocol and physical/rendering constraints required by D13, including endpoint, compatibility-profile identifier, manufacturer/model, paper width, DPI, printable raster width, render mode, Thai encoding mode, cutter command, status mode and command profile, drawer capability and pulse command, timeouts, retry limit, and retry backoff. A printer-profile audit table is reserved for the future authorized configuration command.

`SafePrintDispatcher` is platform- and transport-independent. It resolves the active profile, verifies that the returned Branch and station exactly match the request, checks the reported printer condition, selects an adapter by protocol, and observes the boundary immediately before the first byte may be sent.

The dispatcher returns decisions for a persistent worker to apply:

- no active profile, profile mismatch, unavailable adapter, failed status probe, or an unsafe preflight condition: `Blocked`; leave the job queued and do not create a Print Attempt
- transport failure before the first-byte observer fires: `Failed`; automatic retry is allowed only when the profile permits at least one retry, while the persistent worker remains responsible for enforcing the retry count and backoff
- any failure after the observer fires: `Unknown`; automatic retry is forbidden
- adapter completion after the observer fires: `PrintDispatched`; this proves only transport completion
- adapter completion without the mandatory observer signal: `Unknown`; fail closed because bytes may have been sent

`Ready` and `StatusUnsupported` may dispatch. `Offline`, `CoverOpen`, `PaperOut`, `CutterError`, and `Unknown` block before sending. Phase 0 never emits `Completed` automatically.

The rendered payload owns an immutable byte copy and records its SHA-256. Business documents remain Unicode; rendering to printer bytes is a separate boundary.

## SQLite schema version 8

Schema version 8 adds:

- `printer_profiles`
- one-active-printer-per-Branch-station uniqueness
- `printer_profile_audit`
- indexes for Branch/station lookup and audit history

No real IP, MAC address, serial number, credential, or private device evidence is included in repository fixtures.

## Safety boundary

This slice defines decisions and persistent configuration only. Tests use fake probes and transport adapters and never open a socket or contact a printer. Printer drivers, browser clients, and Mobile clients do not receive database or hardware access.

## Deliberately outside this slice

- ESC/POS/RAW TCP, IPP, and LPR transport implementations
- Unicode-to-raster rendering and bundled Thai font assets
- SNMP/DLE EOT condition-probe implementations
- persistent Print Attempt claim/update records and per-printer worker serialization
- retry scheduling/backoff enforcement and manual reprint commands
- authorized printer-profile mutation and audit-writing API
- Windows/Linux service hosting and physical commissioning
