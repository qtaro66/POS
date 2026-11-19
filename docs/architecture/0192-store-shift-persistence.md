# Durable store and shift lifecycle

The Branch persists the D04.11 domain lifecycle through the existing SQLite
command store. A command request is scoped to its Branch and records its
operation, employee, terminal, Branch timestamp, local Business Date and result.
Repeating the same request returns its saved result. Reusing a request ID with
changed input is rejected.

State reconstruction uses persisted history so that a new command-store
instance retains the original store and shift IDs. Lifecycle writes run in the
existing immediate transaction. Closing reads committed applied cash for the
store session and checks partially paid tables in that transaction before
applying the domain transition.

The migration adds durable lifecycle storage. Existing database migrations
remain supported. D21.3 approval is required before merging the schema change.

The existing cash-sales records provide the current cash source. Production
payment entry, authenticated lifecycle endpoints, the PC POS store-and-shift
screen, and lifecycle Central events still need integration. The Branch must
authenticate the current active employee and OpenStore permission before
invoking these commands. The caller supplies the trusted Branch time and local
calendar date.

Run the persistence regression tests for request replay and conflict, reopening
the database, competing lifecycle commands, rollback after rejected closure,
Branch isolation and cumulative close totals. The deployed acceptance sequence
remains in [the Windows runbook](../pc-pos-cash-and-day-close-acceptance.md).
