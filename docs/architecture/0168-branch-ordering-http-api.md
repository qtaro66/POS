# Branch ordering HTTP adapter and read models

Implement the first ordering API slice from D03, D12.3-D12.5 and D17. Route
commands through the existing `TableSessionCommandService`; preserve atomic
receipts, catalog rejection details, frozen pricing, append semantics and print
outbox behavior. Read queries live behind an Application interface and execute
read-only, branch-scoped SQLite queries. Menu reads share the existing catalog
snapshot reader helpers within one read transaction rather than duplicating
price or availability business rules.

Enablement is explicit and fail-closed: current initialized database, matching
device/employee authentication database paths, HTTPS, and the existing complete
identity policy. Identity fields in command bodies must match authenticated
claims. Transport limits do not replace authoritative batch validation.

There is no schema migration or production dispatch activation. The active
session list does not claim to be a floor plan: available-table inventory is
deferred until a versioned table registry exists. Request-payment, transfers,
cancellations, payments, sent-line detail and print status endpoints are outside
this slice. See [API usage and limitations](../api/branch-ordering-v1.md).
