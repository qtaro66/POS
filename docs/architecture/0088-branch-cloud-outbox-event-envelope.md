# 0088: Branch Cloud outbox event envelope

## Status

Accepted

## Decision

Define a transport-neutral, immutable projection of a claimed Branch Cloud
outbox event. The envelope carries the durable event identity, Branch identity,
`branchSequence`, request identity, table-session identity, action, table
version, and the Branch-created UTC timestamp already persisted in the outbox.

The envelope is created from `BranchCloudOutboxLease`, but it deliberately
excludes retry count, lease owner, claim time, and lease expiry. Those values
are local delivery mechanics and must not become part of the Cloud event.

## Boundaries

This decision does not define JSON field names, schema-version negotiation,
HTTP routes, authentication, headers, response bodies, or a concrete delivery
client. Those transport contracts remain undefined and must not be inferred
from this internal projection.

Automated tests construct the projection in memory and contact no database,
network, or Branch hardware.
