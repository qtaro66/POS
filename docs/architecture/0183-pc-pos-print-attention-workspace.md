# Read-only PC POS print attention workspace

## Scope

After verified MERGED PR #289, expose the existing branch-scoped print attention
query and integrate it into D18.10's System page. This is one complete read-only
slice from SQLite through authenticated HTTP to desktop pagination. It does not
introduce printer commands or infer that paper was printed.

## Endpoint and deployment prerequisites

`GET /api/v1/ordering/print-attention` is mapped only when both existing settings
`BranchOrdering:Enabled` and `PrintStatusQueries:Enabled` are true. The latter
already registers `IPrintJobAttentionQuery` through `AddPrintRuntime`; enabling
status reads does not require polling, retention or a transport dispatcher.
Deployments still provide the existing validated `PrintRuntime:DatabasePath`
for the intended branch database and the existing ordering identity setup.
No flags are enabled by this change and no startup or migration behavior changes.

The endpoint inherits the complete-identity authorization policy and ordering
HTTPS/no-store guards. The handler resolves branch exclusively from authenticated
claims. Caller branch/page-size filters and nonempty GET bodies are rejected.
Only a single optional `cursor` query parameter is accepted. No additional PIN is
required to inspect the read-only status. Authorization of operational commands
is unchanged; no retry, reprint, profile mutation or device test endpoint is added.

## Contract and pagination

The new schema-v1 `BranchPrintAttentionResponse` carries server `observedAt`,
branch-wide `totalCount`, at most 50 items and an optional opaque `nextCursor`.
Items include job/request/session/snapshot references, document purpose, station,
assigned printer ID, persisted job state, creation time, next scheduled time and
last attempt state/time. Public reasons are a closed set derived from persisted
state: AwaitingDispatch, DispatchBlocked, SendFailed, SendOutcomeUnknown and
DispatchLeaseExpired. Raw LastError, BlockedReason and LatestErrorCode are never
serialized; storage/reader failures receive a generic 503 response. Cancellation
of the HTTP request still propagates. The handler never leases or updates a job.

The existing query includes PrintQueued, Failed, Unknown and Dispatching jobs
whose lease has expired or is missing. Completed/dispatched jobs and active
dispatch leases are not part of this attention list. The predicate, ordering and
keyset semantics remain in the existing SQLite implementation.

The base64url cursor carries version, observation time and the last creation-time
and job-ID key. Its length, fields, UTC offsets, identifiers and version are
validated; future observations are rejected. It is a read filter, not a secret
or an authorization capability. Every page still scopes the query using the
authenticated branch, even if a cursor originated in another branch. No client
cursor can select another branch or raise the fixed page size.

Next pages retain the original observation time for lease-expiry classification.
They are not a database snapshot: persisted job states and total counts may change
between page reads. Refresh starts a new first page/observation. The UI explicitly
states this limitation, retains only one bounded page and does not accumulate an
unbounded local history. It rejects repeated next cursors and changed observation
times rather than mixing incompatible pages.

## PC POS presentation and lifecycle

The System page reads on entry and explicit refresh/next-page actions, with no
polling. It displays counts, references, document, station/device, timestamps,
persisted status and a safe public reason. Unknown outcomes and expired dispatch
leases require investigation; this screen never sends or retries a job.

Loading, unavailable, unsupported and observed-empty results are distinct. Old
rows/counts disappear before a refresh or page request; failures are not rendered
as zero jobs. Empty results do not imply healthy printers. The overall System
status remains unknown because health/backup/device telemetry is still missing.
Assigned printer IDs identify stored configuration, not a live device connection.

Client ownership, cancellation and epochs prevent late responses crossing refresh,
page navigation, screen lock or authenticated client replacement. Leaving System
unmounts/cancels the observer. The durable basket remains unchanged on return.
The existing HTTPS transport retains its response byte limit, timeout, no cookies,
no-store and redirect rejection. Only public validated response fields reach view
state. Development fixtures remain separate from production bundles.

## Evidence and remaining work

Tests cover endpoint flags/metadata, identity/HTTPS/body guards, branch isolation,
UTC/keyset cursor boundaries, safe reason mapping, diagnostic redaction, storage
failure/cancellation and real SQLite 50+1 pagination with an unchanged database.
Client/UI tests cover protocol bounds, malformed responses, paging, refresh,
unavailable/empty distinction, late callbacks, identity replacement, lock and
basket-preserving navigation. Desktop visual checks use synthetic data only.

This does not complete printer/device health, queue management, operational PIN
commands, backup monitoring, central outbox diagnostics or production login
bootstrap. Existing dispatch/lease/retry and Unknown policies are untouched.
Explicit D21.3 approval is required before merge for the new public contract,
printing surface and authenticated endpoint. No database migration or dependency
change is introduced, and no automated test contacts hardware.
