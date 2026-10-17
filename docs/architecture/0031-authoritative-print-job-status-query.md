# ADR 0031: Authoritative Print Job status query

## Status

Accepted.

Implements the read-only status-query boundary required by D20.3 and prepares the
system-status views described by D18.10 without exposing a public endpoint yet.

## Context

After dispatch starts, a client cannot infer the authoritative result from a
timeout or connection failure. The Branch database owns both the current Print
Job state and the immutable Print Attempt history. A status query must return
that persisted evidence without contacting printer hardware or reconstructing a
result from logs.

## Decision

- Add an Application-layer `IPrintJobStatusQuery` boundary that is independent
  of SQLite, HTTP, operating systems, and printer protocols.
- Query by both `branchId` and `printJobId`; a job from another branch is not
  disclosed.
- Return the current job status, retry and blocked information, printer
  assignment, safe lease timing, reprint linkage, and the complete ordered
  Print Attempt history.
- Do not expose the lease owner or other worker identity.
- Read the job and attempts in one SQLite read transaction so the result is one
  consistent database snapshot.
- Open SQLite in read-only mode. A query cannot create, migrate, or mutate the
  Branch database.
- Parse persisted enum values with ordinal case-sensitive semantics and fail
  rather than silently mapping an unknown value.

## Consequences

Mobile recovery and the future PC system-status API can depend on one
authoritative query model. An HTTP endpoint, authorization, pagination for
operational lists, and manual reprint commands remain separate slices.

Automated tests use temporary databases only. They do not resolve a printer,
open a network socket, send bytes, print paper, or operate a cash drawer.
