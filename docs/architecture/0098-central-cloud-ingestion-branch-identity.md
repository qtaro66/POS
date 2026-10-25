# 0098: Central Cloud ingestion Branch identity binding

## Status

Accepted

## Decision

Represent the authenticated Central caller as a validated Branch identity and
require the ingestion request's normalized `BranchId` to match it using ordinal
comparison before persistence.

A mismatch raises `BranchCloudEventBranchMismatchException` and the ingestion
store is not called. A future HTTP endpoint must use this identity-bound handler
rather than the unauthenticated transport-neutral handler.

The authenticated identity is supplied by a future authentication adapter. It
cannot be created from an empty Branch ID and contains no credential or secret.

## Boundaries

This decision does not select an authentication scheme, token format, claim
type, certificate, or secret store. It does not activate an endpoint or define
HTTP status mapping, persistence, sequence-gap recovery, or retention.

Automated tests use in-memory identities and stores and contact no database,
network, or Branch hardware.
