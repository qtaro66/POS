# ADR 0028: Print worker lease heartbeat

## Status

Accepted.

Extends ADR 0012, ADR 0013, and ADR 0027 without changing their historical decisions.

## Context

The persisted print worker may spend longer than one lease duration loading and validating snapshots, rendering a payload, probing printer condition, or sending bytes. ADR 0027 introduced atomic compare-and-swap renewal, but the worker did not yet invoke it. An expired lease could therefore be recovered while the original worker was still active.

## Decision

- After claiming a Print Job, the worker starts a lease heartbeat at one third of the configured lease duration.
- Each heartbeat uses the latest immutable lease token and replaces it only after atomic renewal succeeds.
- Profile lookup, source loading, rendering, spool persistence, preflight, and transport use an operation token linked to the caller and heartbeat ownership.
- If renewal fails, the heartbeat cancels the in-flight operation and the cycle fails closed. It does not release, complete, retry, or otherwise reinterpret the job; persisted lease-expiry recovery remains authoritative.
- Before releasing a blocked job or recording a terminal attempt, the worker stops the heartbeat and surfaces any renewal failure. This prevents renewal from racing a terminal database transition.
- Normal heartbeat shutdown does not cancel the operation token. Existing caller-cancellation behavior remains unchanged.

## Consequences

Slow work can retain its per-printer ownership without allowing a stale worker context to renew a newer claim. Heartbeat failure may leave a job or attempt nonterminal until the existing recovery path processes the lease, which is deliberate because recovery has the durable first-byte evidence needed to choose `Failed` or `Unknown` safely.

Automated tests control the heartbeat delay and use isolated SQLite databases, fake renderers, and fake transports. They do not resolve a printer host, open a network socket, print paper, or open a cash drawer.
