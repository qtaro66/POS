# Cash history related evidence v1

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/events

Requires the existing complete identity and provisioned PcPos role. Branch and
table-session scope come from the authenticated principal and captured cash
request. No body, extra PIN, read audit or mutation is introduced. Responses use
Cache-Control: no-store.

| Query | Values |
| --- | --- |
| source | Session (default) or Cancellation, case-sensitive |
| limit | 1..50, default 20 |
| cursor | Opaque bounded base64url continuation for this source and subject |

Unknown, duplicate, blank or invalid values return 400. Captured request IDs
remain canonical bounded text, not GUID-only. Foreign or missing parent cash
requests return 404 CashHistoryNotFound without existence disclosure; incomplete
identity or other roles receive 403. Unavailable/corrupt evidence returns generic
503 without raw data, worker identifiers or internal errors.

Page fields: schemaVersion=1, branchId, asOf, cashRequestId, tableSessionId, source,
events and nullable nextCursor. Each event has eventId, source, requestId,
occurredAt, employeeId, nullable deviceId, nullable session and nullable
cancellation. Event requestId is the original operation/cancellation identity,
not necessarily the parent cashRequestId. Event IDs are canonical up to 1024
characters because session audit IDs are composite; underlying IDs are bounded
to 256. Timestamps are UTC and recorded text is rendered as plain text.

Session events use eventId=session: followed by original audit ID, have captured
deviceId and non-null session, and cancellation=null. Session fields: action,
nullable previousStatus, currentStatus, previousTableId, currentTableId,
tableVersion and nullable reason. Actions/statuses are the existing captured
TableSessionAction/TableSessionStatus enums. Reasons allow CR/LF/TAB, at most
4096 characters. No current transition matrix is applied to rewrite old facts.

Cancellation events use eventId=cancellation: followed by their requestId,
deviceId=null, session=null and non-null cancellation. Cancellation contains
outcome (Confirmed or PartiallyConfirmed) and lines. Each line has orderLineId,
checked, cancelled, requestedQuantity and notCancelledQuantity. Both count
objects contain notStarted, inProgress, completed and notApplicable.

Counts are nonnegative Int32; requestedQuantity is positive; checked buckets sum
to requested quantity. Cancelled buckets cannot exceed the corresponding checked
bucket. notCancelledQuantity equals requested minus cancelled buckets, and at
least one unit across the event is cancelled. All zero remaining means Confirmed;
otherwise PartiallyConfirmed. Zero-cancelled lines remain valid within a partial
event. Current mutable request status is not returned as historical evidence.
No device, product name, reason or cancellation amount is fabricated when absent
from the immutable confirmation. Cancellation payload parsing is bounded to1MiB.

Each source uses its own recorded-sequence ordering and high-water cursor,
including schema-cookie scope. Mixing cash requests, sessions, sources or limits
invalidates the cursor. Refresh includes later records; timestamps need not form
a global chronology across streams. An empty page means no matching recorded
evidence was read, not proof that an operation never happened. Existing manual
maintenance/restore traversal boundaries continue to apply.

Cash item/detail contracts and their cursor version2 do not change. D21.3 approval
is required for this new read contract before merge. There are no printing,
refund, reversal or cancellation commands on this route.
