# 0096: Central Cloud ingestion request contract

## Status

Accepted

## Decision

Define schema version 1 of the Central Branch-event ingestion request. The
request contains event ID, Branch ID, `branchSequence`, request ID,
table-session ID, table-session action, table version, and the Branch-created
UTC timestamp already present in the immutable outbox envelope.

Represent the action as its exact case-sensitive `TableSessionAction` name.
Reject numeric enum representations, unknown names, unsupported schema versions,
non-positive sequences or table versions, empty identifiers, and non-UTC
timestamps before persistence.

Map the validated request into `BranchCloudOutboxEventEnvelope`; no local Branch
lease or retry metadata is accepted by the Central contract.

## Boundaries

This decision defines the request model and mapper only. It does not activate an
HTTP endpoint or define HTTP method, status codes, authentication, body-size
limits, error responses, persistence, or missing-sequence recovery.

Automated tests call the mapper in memory and contact no database, network, or
Branch hardware.
