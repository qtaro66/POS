# ADR 0013: Hosted per-printer print spool polling

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13 and D20
- Extends: ADR 0010, ADR 0011, and ADR 0012

## Decision

The Branch API hosts print spool polling because it is the only process permitted to mutate Branch SQLite. Polling is disabled by default until production rendering, condition probing, and transport adapters are explicitly registered.

When enabled, configuration must contain a non-empty ordinal-unique list of printer identifiers, a worker identifier prefix, a positive lease duration, and positive idle, blocked, and error delays. Startup fails closed unless exactly one `IPrintSpoolCycleProcessor` is registered.

The hosted service creates one long-running lane per configured printer. A lane awaits each cycle before requesting the next one, so a single printer is never processed concurrently by that service instance. Separate printer lanes run concurrently. Cross-process serialization remains enforced by the persistent per-printer lease from ADR 0011.

Each Branch Service instance generates one process-scoped worker identifier from the configured prefix, process ID, and a random suffix. All printer lanes in that process share the identifier while Print Attempts continue to record their own identifiers.

## Polling behavior

- `NoWork`: wait for `IdleDelay` before polling again.
- `Blocked`: record a warning and wait for `BlockedDelay`, preventing a cover-open or paper-out busy loop.
- processed terminal result: immediately request the next queued job.
- unexpected cycle failure: record the exception, wait for `ErrorDelay`, and keep the affected lane alive.
- host cancellation: pass the cancellation token into the active cycle and stop the lane. If cancellation crosses the first-byte boundary, ADR 0011 lease recovery remains authoritative and may resolve the attempt to `Unknown`.

## Safety boundary

Default configuration sets `Enabled` to `false` and contains no printer identifiers. Enabling polling without a registered processor fails startup. Automated service tests use an in-memory fake cycle processor and never resolve a hostname, open a socket, print paper, or open a cash drawer.

## Deliberately outside this slice

- production renderer, condition probe, and transport registrations
- RAW TCP, IPP, or LPR implementation
- discovery or automatic enrollment of printers
- lease renewal during long-running rendering or transport
- manual reprint or resolution of `Unknown`
- spool retention and quota enforcement
