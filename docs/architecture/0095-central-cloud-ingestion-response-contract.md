# 0095: Central Cloud ingestion response contract

## Status

Accepted

## Decision

Define a versioned Central response for successful Branch Cloud event ingestion.
The response contains schema version 1 and the durably persisted event ID,
Branch ID, and `branchSequence`.

Map both `Stored` and `AlreadyStored` persistence outcomes to the same response
shape. Both outcomes represent durable storage of the exact event, while the
Branch needs only the echoed identity to validate its acknowledgment. Do not
expose Central storage internals or duplicate-detection details in the success
contract.

## Boundaries

This decision defines the success response model and mapper only. It does not
activate an HTTP endpoint or define HTTP status codes, authentication, request
serialization, error responses, missing-sequence recovery, or persistence.

Automated tests call the mapper in memory and contact no database, network, or
Branch hardware.
