# ADR 0034: Authoritative Branch command status query

## Status

Accepted.

Implements the read-only recovery boundary required by D20.1.

## Context

After a Mobile request starts sending, a timeout does not prove whether the
Branch committed or rejected it. Retrying as a new request could duplicate an
order. The durable `command_receipts` row is the authoritative result associated
with the original branch-scoped `requestId`.

## Decision

- Add an Application-layer `IBranchCommandStatusQuery` independent of SQLite,
  HTTP, operating systems, and Mobile storage.
- Query by both `branchId` and the exact original `requestId`; a receipt from
  another branch is not disclosed.
- Return the persisted `BranchCommandReceipt` unchanged so a duplicate request
  and a status query can converge on the same accepted or rejected evidence.
- Return no row as `null`. The query does not invent `Pending`, `Rejected`, or a
  new command result when the Branch has no durable receipt.
- Open SQLite in read-only mode. The query cannot create, migrate, or mutate the
  Branch database.
- Parse persisted enum values with ordinal case-sensitive semantics and fail on
  unsupported stored values.

## Consequences

A future authenticated Mobile endpoint can resolve `Unknown` using the original
`requestId` without resubmitting a new order. Endpoint authentication, response
mapping, transport-level not-found semantics, and Mobile IndexedDB behavior
remain separate slices.

Automated tests use temporary databases only and perform no network, printer,
cash-drawer, or production-data access.
