# Read-only PC POS printer inventory

## Scope

After verified MERGED PR #292, expose configured printer inventory on D18.10's
System page, preserving D13.1 identity and D13.3 status-evidence rules. This is a
read-only configuration view from SQLite through authenticated HTTP to PC POS.
It is not a printer probe, health check, print test or profile editor.

## Source and composition

The application query boundary is independent of database, browser and printer
implementations. The SQLite adapter opens the existing print-runtime database in
ReadOnly mode, with no pooling and one deferred transaction for counts and rows.
Both active and inactive profiles are included, scoped by authenticated branch;
printerId remains identity, never IP/hostname. Optional blank Product Code and
Firmware values normalize to null, matching existing profile-reader semantics.

The query is registered by AddPrintRuntime only under the existing
PrintStatusQueries enablement. It uses PrintRuntime:DatabasePath, the same source
as printing configuration/status queries, rather than assuming that the ordering
database path necessarily points at those profiles. Read-only status composition
requires no dispatcher, transport, polling or retention worker. Existing startup
database validation remains in force. No flag is enabled by this change.

## Endpoint and safe contract

GET /api/v1/ordering/printer-inventory is mapped only when both BranchOrdering and
PrintStatusQueries are enabled. It reuses complete-identity authorization and the
ordering HTTPS/no-store guards. Branch comes only from claims. One optional
afterPrinterId cursor is accepted; caller branch/limit overrides, repeated/invalid
parameters and nonempty GET bodies are rejected. Pages are fixed at 50 records.
Reader failures receive generic 503 responses and request cancellation propagates.

The schema-v1 response has server read time, total/active configuration counts,
items and nextAfterPrinterId. Public fields include stable ID, display name,
station, safe hostname/IP and port, protocol/profile, manufacturer/model,
Product Code/Firmware, paper width, configured status mode/drawer support,
configured Active flag and stored update time. Command bytes, status-command
parameters, secret references, connection strings and raw errors are omitted.

Host projection accepts plain ASCII DNS/IPv4/IPv6 forms without resolving them.
URLs, credential-bearing or unsupported endpoint strings become null, not an
error/credential leak or an Offline verdict. This only restricts public display;
the saved profile and dispatcher inputs are unchanged. Non-ASCII DNS/other
endpoint formats are not displayed by this projection. A profile with an omitted
host still retains its ID/station and other safe metadata for investigation.

Inventory has no device-status result or device-observation timestamp. Active,
SNMP mode, drawer support and configured firmware are settings, not evidence
that a device is online, supports a sensor in practice, opened a drawer or printed
paper. Client projection discards unknown/private fields, including a spurious
live-status field. Overall System health remains unknown.

## Paging and desktop lifecycle

Rows and cursor comparisons use printer-ID BINARY ordering. The client compares
UTF-8 bytes to match SQLite even where JavaScript UTF-16 ordering differs. Cursors
filter only reads and cannot select a branch or grant operational rights. Each
page is a fresh transaction, not a historical snapshot; configuration/counts can
change between pages. Refresh starts at page one and only one bounded page is kept.

The System panel reads on entry, explicit refresh and next-page actions, with no
polling. Loading clears old counts/rows. Unsupported, unavailable and observed
empty states remain distinct; a failed read never becomes zero printers. Each
row displays Unknown with an explicit statement that no device was inspected.
The read timestamp and stored configuration update time are not Health Check
timestamps. There are no configuration, socket, DNS, SNMP, printer or drawer actions.

Ownership, cancellation and epochs prevent stale results crossing refresh,
navigation, identity replacement or screen lock. Paging restores focus to the
new list. Returning to ordering preserves the durable basket and sends no command.

## Evidence and limits

API/storage tests cover both feature flags, policy/identity/HTTPS/body/query
guards, branch isolation, active/inactive configuration, 50+1 SQLite pagination,
host credential redaction, omitted command fields, generic failures/cancellation,
missing-database protection and unchanged database bytes. Composition assertions
confirm the query uses the configured print database without transport/dispatcher
services. Client/UI tests cover public projection, unsafe endpoints, UTF-8 cursor
order, count/protocol limits, paging/focus, unavailable versus empty, stale reads,
identity replacement, lock and basket preservation. Desktop checks use synthetic
fixtures excluded from production bundles and contact no external device/service.

This does not implement live printer/PrintGateway health, job-specific completion,
paper/cover/cutter sensors, PIN-protected device tools, profile administration,
backup/checkpoint metrics or production login/bootstrap. It adds no migration,
dependency, worker activation or change to printing/routing/retry/Unknown rules.
Explicit D21.3 approval is required before merge for contracts, printing and the
authenticated diagnostics surface.
