# Central closing report ingestion

This completes the receiver side of the Branch closing report delivery contract.
It adds PostgreSQL migration 2, immutable storage, authenticated bounded ingestion,
readiness checks and Branch-to-Central lost-acknowledgement integration coverage.
Branch SQLite stays at schema 33. Back Office remains a separate add-on.

## Boundaries and immutable identity

POST /api/v1/closing-reports uses the existing Central Cloud ingestion policy and
BranchCertificate mTLS scheme. The route is absent when CentralClosingReports is
disabled (the default). Enabled startup requires CentralCloudIngestion and
CentralBranchMtls, the matching authentication scheme and the existing Central
connection configuration. HTTP requests are rejected before HTTPS redirection;
authenticated Branch identity is checked against the envelope before storage.

The complete UTF-8 body is bounded to 64 KiB even when chunked. Parsing rejects
invalid UTF-8/JSON, duplicate properties at every depth, unknown schema versions,
incorrect stable report identity, invalid dates/timestamps, inconsistent closing
operation/identity and invalid cash reconciliation. Nullable legacy cash summaries
stay null. Nested financial and table snapshots are retained without recomputing
from current master data. No PIN, client-controlled employee impersonation claim,
or public report-read endpoint is introduced. Employee/device fields describe the
original Branch snapshot; transport authority is the authenticated Branch.

Report IDs must match the existing closing:HEX(branch):HEX(request) derivation.
Identity fields are canonical non-control strings up to 256 UTF-16 code units;
derived ASCII report IDs are bounded to 2000 characters to fit PostgreSQL index
tuples, including Unicode Branch/request input. Invalid Unicode surrogates in
property names and strings are rejected throughout the snapshot.
OccurredAt uses UTC ISO 8601 with up to seven fractional digits. The stored raw
JSON text is immutable, including whitespace and property order. Replaying a
semantically similar but byte-different body is a conflict; the Branch sender
already retries the exact saved body. PostgreSQL timestamp metadata is normalized
to microseconds while the original timestamp text remains in the payload.

## Persistence and concurrency

Migration 0002 creates public.closing_reports with primary report identity, unique
Branch/request identity and unique Branch/store/shift identity. Parameterized
inserts run inside a ReadCommitted transaction. INSERT ON CONFLICT DO NOTHING
waits for concurrent unique-key writers; the subsequent statement obtains a fresh
snapshot for comparison. Exactly one identical persisted record yields
AlreadyAccepted. Different content or conflicting identities yield HTTP 409 with
no overwrite. Accepted and AlreadyAccepted are returned only after commit.
A failed or uncertain commit never produces a success acknowledgement; retrying
the original body resolves a commit whose reply was lost.

The separate report table never consumes table-event sequence numbers. Its
received_at is Central receive time, not the business date or source closing time.
This implements durable receipt only, without report editing, deletion, retention,
or automatic notification processing.

## Schema and readiness

The migration catalog now targets version 2. Existing version-1 SQL is unchanged;
fresh databases apply versions 1 and 2, existing version-1 databases apply only 2,
and migration drift/newer schemas fail closed under the existing migration lock.
All CentralCloudIngestion-enabled deployments run the version-2 migration on
startup, even if closing report ingestion remains disabled. Operators must review
this shared Central upgrade; disabling the closing route does not undo migration.

When report ingestion is enabled, /health/ready includes its dedicated check and
each report request checks report readiness before persistence. The probe checks
schema version, required column types/non-nullability and nondeferrable unique
identities. Missing/incompatible storage produces 503 without revealing database
connection details or payloads. Existing table-event readiness retains its own
checks and follows the catalog version 2.

See ../pc-pos-central-closing-report-activation.md for activation, evidence and
rollback procedures. D21.3 approval is required before merge for contracts,
authentication wiring and the Central migration.
