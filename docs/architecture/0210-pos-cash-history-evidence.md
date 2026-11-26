# Related evidence for captured cash history

This advances D18.8 and D01.4 with read-only evidence related to an already
captured cash transaction. It adds no cancellation, transfer, payment or print
command and does not change the saved receipt or bill.

## Sources and attribution

Two separate streams are available for the cash capture's recorded table session.
Session reads table_session_audit: original action, previous/current status and
table identity, version, actor, terminal, timestamp and recorded reason.
Cancellation reads immutable cancellation_confirmations.details_json and its
recorded actor/time. cancellation_requests is joined only to identify the related
session. Current request status, reason, production/order rows and master data
are not used to reconstruct confirmation facts.

Cancellation confirmation stores checked and cancelled production-count buckets,
requested quantity and quantity not cancelled. Confirmed versus PartiallyConfirmed
is derived from those recorded quantities, not today's mutable request status.
Zero-cancelled lines may be present in a partial confirmation; the event as a
whole must have some confirmed cancellation. Fully cancelled lines need not be
present in the later bill's chargeable-item snapshot.

The confirmation payload did not capture device identity, item names, cancellation
prices or a frozen reason. Those fields are not guessed from current records or
the cash operator's identity. This is quantity evidence, not a refund, financial
adjustment or a complete cancellation-value report.

## Subject, pagination and consistency

The API first resolves the captured cash request under the authenticated Branch,
then reads only that capture's table session. It cannot select an arbitrary
branch or session through query parameters. Complete employee/device identity
and provisioned PcPos role are required; Mobile is denied. No additional PIN or
read audit is created, and responses are no-store.

Sources have independent pages and cursors. Each cursor binds branch, cash
request, session, source, limit, schema cookie and first-page rowid high-water
position. Later appends appear on refresh. VACUUM/DDL invalidates the cursor;
manual database replacement/restore/deletion requires a fresh traversal. The
source rowid is a navigation hint and never an authorization credential.

Events are newest recorded sequence first within each source. Streams are not
merged into an invented global clock order. Event timestamps are original
observations; no new monotonic-clock assumption is applied. Related evidence may
include records appended after payment, such as subsequent session closure;
reading it never recalculates the capture's money.

All reads share the existing transaction/connection boundary. Selected corrupt
evidence is unavailable rather than partially projected. Cancellation JSON is
bounded to 1 MiB and validated for unique line identity, nonnegative Int32
buckets, requested/checked totals, componentwise cancellation bounds and remaining
quantity. The reader does not rerun today's table-transition state machine.

## POS behavior and compatibility

Captured detail shows separate session/table and cancellation-evidence views.
Changing source, refreshing or loading more affects only the evidence panel;
an evidence failure does not erase or change captured bill/financial detail.
Selection, scope, client replacement and lock cancel or ignore old requests.
Unsupported older clients show an explicit unconnected state rather than an
empty successful history.

The ordinary cash list, detail, item filters and cursor version 2 remain
unchanged. The new evidence cursor is an independent version-1 contract. Branch
schema 35 and Central schema 3 remain unchanged; there is no migration or new
dependency. New authenticated read contracts require D21.3 approval before merge.
