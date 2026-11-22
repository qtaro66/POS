# Branch closing report queries

These GET routes live under /api/v1/ordering and require the existing complete
Branch/employee/device identity policy and PcPos role. They use the authenticated
Branch only, return Cache-Control: no-store and accept no request body.

## List: /store-lifecycle/closing-reports

| Query | Rule |
| --- | --- |
| status | All (default), Pending or Delivered; case sensitive |
| lifecycleStatus | All (default), Pending, Delivered or NotRecorded; case sensitive |
| storeSessionId | Optional exact D-format GUID |
| shiftId | Optional exact D-format GUID |
| businessDate | Optional valid yyyy-MM-dd |
| limit | Integer 1..50; default 20 |
| cursor | Optional opaque base64url continuation, max 4096 characters |

Omit unused query values. Empty, duplicate or unknown parameters are invalid.
A cursor is bound to all filters and limit; changing any of them starts a new
first-page query. The response contains branchId, asOf, items and nextCursor
(nullable). Empty results are successful; a storage failure is not an empty page.

`status` filters the original report receipt. `lifecycleStatus` independently
filters the frozen opening/store/shift companion receipt. All supplied filters
are combined with AND. In particular, `status=Delivered&lifecycleStatus=Pending`
finds reports whose original packet is confirmed while their companion is still
unconfirmed. The list item's deliveryState continues to describe only the
original report; open detail for independent lifecycle delivery diagnostics.

Lifecycle Pending includes queued work, a live or expired lease, and delayed or
due retries. It requires a recorded companion and unconfirmed context job.
Delivered requires a recorded companion and its own confirmed context job.
NotRecorded selects historical reports with neither a captured companion nor a
context job; it never infers success or a fabricated zero from an absent field.
Missing/orphaned/mismatched context jobs and malformed stored delivery metadata
fail closed as 503 for the authenticated branch, including when another filter
would hide that row. Corrupt data in another branch is outside the read scope.

New continuations use cursor version 2 and bind lifecycleStatus together with
all existing filters, branch, limit and high-water position. Old version-1
cursors are rejected with 400; refresh starts a new traversal. The saved
report/context packets do not change. Servers predating lifecycle filtering
reject the new query parameter instead of silently broadening the list. A
filter-capable server without per-row metadata cannot satisfy the new client's
filtered-response validation. Deploy the compatible Branch API before the
matching POS web client.

Each item contains requestId, reportId, storeSessionId, shiftId, businessDate,
operation (CloseShift/CloseStore), createdAt, deliveryState, attemptCount,
availableAt, leaseExpiresAt (nullable), deliveredAt (nullable) and lastFailure
(nullable). No raw payload, lease owner/token, URL, connection string or server
exception is returned. Dates/times are normalized to UTC transport timestamps;
createdAt describes original creation, asOf is the current read time.

Each list item now also includes lifecycleDelivery. It is null only for a
historical report with no recorded companion or context job. Otherwise it
contains contextId, deliveryState, attemptCount, availableAt, leaseExpiresAt,
deliveredAt and lastFailure for the independent companion queue. ContextId must
equal lifecycle: followed by the item's reportId. The existing item-level
deliveryState and other delivery fields continue to describe the original
report; a confirmed original report alone does not confirm the companion.

Companion states use the same five state names below. Companion failures are
TransportUnavailable, CentralRejected, AcknowledgmentUnavailable and
ParentUnavailable; unknown stored failures are sanitized to null. ParentUnavailable
indicates bounded original-parent recovery, never a need to close the store again.
Each page's metadata is read at its asOf within the existing SQLite transaction.
Only returned rows receive detailed companion projections; the separate branch
integrity validation still scans branch context metadata before LIMIT.

For compatibility, web clients treat an absent lifecycleDelivery property from
an older server as unknown, distinct from explicit null/NotRecorded. A filtered
lifecycle response without verifiable metadata is rejected rather than accepted
as matching. For Pending, require a present unconfirmed companion; for Delivered,
require its independent receipt; for NotRecorded, require explicit null. All
allows absent historical-server fields but never fabricates confirmation.

| Delivery state | Interpretation |
| --- | --- |
| Queued | Unconfirmed work; may include a retry that is already due |
| InFlight | Current unexpired lease; network outcome not established |
| RetryScheduled | Unconfirmed attempt delayed until availableAt |
| LeaseExpired | Claim expired; receipt remains uncertain until recovered |
| Delivered | Matching acknowledgement recorded at deliveredAt |

lastFailure reports the previous completed attempt's known category only:
TransportUnavailable, CentralRejected or AcknowledgmentUnavailable. Unknown stored
values are projected to null, including RetryScheduled rows; null does not imply
there was no failure. A report may have reached Central before an uncertain or
lost response, so pending/lease expiry must never be described as definitely not
received by Central.

## Detail: /store-lifecycle/closing-reports/{requestId}

requestId is an exact D-format GUID. The response has branchId, report (the item
above, with current delivery state) and snapshot. Snapshot contains schemaVersion,
reportId, branchId, requestId, storeSessionId, shiftId, operation, businessDate,
closing and cashSummary. It is a typed projection of the original saved JSON,
not a reconstruction. Nested closing and financial fields use camelCase matching
the existing store-and-shift read contracts. Legacy missing cash summaries stay
null; optional saved financial/table totals stay null when absent. Unknown future
stored fields remain in storage but are not exposed by this v1 typed projection.

Detail also retains its top-level lifecycleDelivery. Its report.lifecycleDelivery
now matches that projection in the same read transaction. Clients accept the old
omitted nested field, but reject contradictory nested and top-level companion
metadata. Neither projection is included in the immutable document snapshot.

A missing report or another Branch's requestId returns JSON 404
ClosingReportNotFound, preventing cross-Branch existence disclosure.

## Settings: /store-lifecycle/closing-reports/delivery

The response contains branchId, asOf and deliveryEnabled. This is true only when
automatic report delivery is enabled for the authenticated Branch. It is false
when disabled or configured for another Branch. It is a configuration indicator,
not a worker heartbeat, Central connectivity check or overall synchronization
result. No network request to Central is made by these read routes.

## Failures and consistency

403 rejects incomplete identity or non-PcPos role. Invalid filters, GUIDs or
cursors return JSON 400. SQLite or inconsistent saved detail storage returns 503
without sensitive error text. The normal Branch identity/transport middleware
continues to apply; handler actions do not inspect Request.IsHttps.

Pagination freezes the initial sequence high-water bound, not delivery status.
Newly appended reports appear after refresh; reports can change Pending/Delivered
membership during traversal, independently for both receipt types. Clients should clear stale pages on filter/scope
changes, validate response identities and fetch detail afresh after delivery
changes. Reads perform no queue mutations and do not authorize privileged retry.
