# 0093: Central Cloud event-ingestion boundary

## Status

Accepted

## Decision

Define a transport-neutral Central persistence boundary for Branch Cloud events.
The store accepts an immutable `BranchCloudOutboxEventEnvelope` and returns the
persisted event ID, Branch ID, and `branchSequence` only after the event is
durable.

The first successful persistence returns `Stored`. An exact retry of the same
event returns `AlreadyStored`, allowing the Branch to treat either result as a
durable acknowledgment.

Cloud persistence must enforce globally unique event IDs and unique
`branchSequence` values within each Branch. Reuse of either identity with
different event content raises `BranchCloudEventUniquenessConflictException`,
does not modify the original event, and must never be mapped to a durable
acknowledgment.

## Boundaries

This decision defines application and persistence semantics only. It does not
select a Central database, create a migration, activate an HTTP endpoint, or
define authentication, request serialization, response mapping, missing-sequence
recovery, or retention.

Automated tests validate the boundary in memory and contact no database,
network, or Branch hardware.
