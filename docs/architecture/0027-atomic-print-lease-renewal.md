# ADR 0027: Atomic print lease renewal

## Status

Accepted.

Extends ADR 0011 and ADR 0013 without changing their historical decisions.

## Context

The persisted print worker can hold a lease while loading a snapshot, rendering a payload, probing printer condition, and sending bytes. A fixed lease may expire during a slow operation. Recovery could then release the job, mark a pre-send attempt failed, or conservatively mark an in-flight send `Unknown` while the original worker is still active.

Matching only the worker identifier is insufficient because one process deliberately reuses its worker identifier across jobs and claims. A stale operation from an earlier claim must not renew a later claim owned by the same process.

## Decision

- The print dispatch store exposes an atomic renewal operation for an active lease.
- Renewal uses the persisted expiry as a compare-and-swap token in addition to print job, printer, and worker identifiers.
- Renewal succeeds only while the persisted lease has not expired and the job remains `PrintQueued` or `Dispatching`.
- The new expiry must strictly extend the expiry carried by the current lease token. A successful renewal returns a replacement immutable lease token containing that expiry.
- A stale, expired, released, reassigned, or terminal lease fails closed without changing persisted state.
- Renewal changes only `lease_expires_at`; it does not create attempts, increment counters, alter print status, or weaken first-byte uncertainty rules.

## Consequences

The persistence layer can now safely support a future heartbeat around slow rendering and transport. Callers must replace their old lease value after every successful renewal because that value becomes stale immediately. Heartbeat scheduling and cancellation integration remain a separate runtime change.

Automated tests exercise SQLite only. They do not resolve a printer host, open a network socket, print paper, or open a cash drawer.
