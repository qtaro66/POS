# ADR 0029: Rendered print spool retention store

## Status

Accepted.

Extends ADR 0012 and implements the binary-spool boundary from requirement D19.4 without selecting a production retention period or quota.

## Context

Rendered ESC/POS payloads can be materially larger than Print Job and Print Attempt metadata, especially in raster mode. D19.4 requires at least 90 days of Print Job and Print Attempt metadata, a separately measured retention and quota policy for terminal binary payloads, and permanent protection from automatic deletion while a job is queued, dispatching, unknown, or otherwise still retryable.

The measured production payload distribution and final retention period are not yet available. Hard-coding either value would turn a planning baseline into an unapproved business policy.

## Decision

- Provide a SQLite retention store that inspects total, state-protected, age-retained, and deletable rendered payload counts and bytes.
- Accept the deletion cutoff, maximum total bytes, and maximum rows per pruning transaction from the caller. This slice defines no runtime defaults and schedules no background cleanup.
- Treat only `PrintDispatched`, `Completed`, and retry-exhausted `Failed` jobs as terminal candidates. A `Failed` job with `next_retry_at` remains protected.
- Never delete a payload for a leased job or a job in `PrintQueued`, `Dispatching`, or `Unknown`.
- Delete terminal candidates oldest first, stop when the requested quota is reached or the transaction batch limit is exhausted, and report whether the quota target was reached.
- Delete only `rendered_print_payloads` rows. Preserve Print Job, Print Attempt, immutable source snapshot, and audit metadata.
- Recheck terminal eligibility in the delete statement inside the same immediate transaction, failing closed if a selected row is no longer eligible.

## Consequences

Capacity can be measured before a production policy is approved, and a future scheduled retention service can enforce explicit settings without duplicating database safety rules. A target may remain unmet when protected or age-retained payloads exceed the quota; the result reports this condition rather than deleting unsafe data.

Automated tests use isolated SQLite databases and synthetic payload bytes. They do not resolve a printer host, open a network socket, print paper, or open a cash drawer.
