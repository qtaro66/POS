# ADR 0012: Persisted rendered print spool and worker cycle

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13 and D20
- Extends: ADR 0009, ADR 0010, and ADR 0011

## Decision

The Branch database stores one immutable rendered binary payload per Print Job and assigned printer before a transport attempt can start. The spool row binds the Print Job, source snapshot, printer, document identifier, exact bytes, SHA-256, and creation time.

Rendering always starts from the immutable Bill Preview or Production Order snapshot referenced by the Print Job. The worker verifies the SHA-256 of the exact stored UTF-8 JSON before handing it to a printer-specific renderer. A renderer must return the same document identifier as the source snapshot. Once persisted, retries and process restarts reuse the exact bytes rather than rebuilding from current Master Data or printer settings.

`PersistedPrintSpoolWorker.ProcessNextAsync` performs one bounded cycle for one printer:

1. acquire the per-printer lease from ADR 0011
2. resolve and verify the leased printer profile
3. load the existing spool or render and persist it
4. invoke the safe dispatcher with a durable Print Attempt lifecycle
5. release a blocked preflight without creating an attempt, or atomically persist the terminal attempt result

The outer hosted service is responsible for polling, cancellation, delay/backoff between blocked cycles, and running independent cycles for different printers. Keeping one cycle bounded makes shutdown and failure testing deterministic.

## SQLite schema version 10

Schema version 10 adds `rendered_print_payloads`. Updates are rejected by a database trigger. Controlled deletion remains possible for a future retention worker, but that worker must never delete payloads for `PrintQueued`, `Dispatching`, or `Unknown` jobs.

## Failure rules

- Missing or hash-invalid source data is blocked before rendering or transport.
- Rendering or spool persistence failure releases the pre-attempt lease with a diagnostic blocked reason and creates no Print Attempt.
- Hardware preflight failure reuses the persisted spool later and creates no Print Attempt.
- Once a transport attempt begins, ADR 0011 owns `Failed`, `PrintDispatched`, and `Unknown` transitions.
- Cancellation or an unexpected persistence failure is not converted into a safe retry result; the lease-expiry recovery rules remain authoritative.

## Safety boundary

Automated tests use fake renderers, condition probes, and transport adapters. Hostnames are non-routable test values. No test opens a network socket, prints paper, or opens a cash drawer.

## Deliberately outside this slice

- production ESC/POS raster rendering and Thai font assets
- RAW TCP, IPP, or LPR transport implementation
- hosted polling service registration and configuration
- lease renewal for long-running renders or sends
- terminal spool retention/quota enforcement
- manual reprint or resolution of `Unknown`
