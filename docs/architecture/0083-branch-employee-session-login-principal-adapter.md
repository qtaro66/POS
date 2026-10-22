# 0083: Branch employee session login principal adapter

## Status

Accepted

## Decision

The employee-session login transport derives Branch and device identity only
from exactly one pair of non-empty claims on the same authenticated identity. Missing,
anonymous, incomplete, or ambiguous device claims return the same bodyless HTTP
401 response and do not invoke PIN validation.

The adapter creates a `VerifiedBranchDevice` from those trusted claims and
delegates the presented, already parsed PIN to the existing employee-session
login HTTP handler. Branch and device identifiers are never accepted from route,
query, or body data. Employee identity continues to come only from successful
PIN validation.

All outcomes use the shared no-store/no-cache response-header policy. Valid
requests preserve cancellation, durable session issuance, response mapping, and
operational-failure behavior from the existing login handler.

## Boundaries

This decision does not define the PIN request contract, model binding, the
device authentication scheme, authorization metadata, HTTPS enforcement, rate
limiting, route mapping, or runtime activation.

Automated tests use synthetic claims and PINs with in-memory collaborators.
They do not contact branch hardware, a database, or the network.
