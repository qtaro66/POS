# ADR 0032: Branch-scoped Print Job attention query

## Status

Accepted.

Extends ADR 0031 with the bounded operational list required by D18.10.

## Context

The PC system-status view needs a count and actionable list of queued, failed,
unknown, or otherwise abnormal Print Jobs. Loading each job separately would
produce inconsistent counts and unbounded database work. The list also must not
probe printer hardware merely because a user opens a read-only page.

## Decision

- Add an Application-layer `IPrintJobAttentionQuery` with keyset pagination.
- Include `PrintQueued`, `Failed`, and `Unknown` jobs plus `Dispatching` jobs
  whose lease is missing or expired at the caller-supplied observation time.
- Query by branch and return a total attention count plus at most 200 items.
- Sort newest first by persisted creation time and Print Job identifier; the
  continuation cursor contains that stable pair.
- Return document snapshot identity, target station, assigned printer, retry or
  blocked evidence, and the latest immutable Print Attempt result.
- Read the count and page in one SQLite read transaction and open the database
  in read-only mode.
- Do not contact a printer, infer paper output, expose worker identity, or offer
  retry/reprint commands from this query.

## Consequences

The future D18.10 status endpoint can display a bounded authoritative list and
summary without N+1 queries. Endpoint authentication, UI presentation, command
permissions, and user-authorized retry/reprint flows remain separate slices.

Automated tests use temporary databases only and perform no network or hardware
access.
