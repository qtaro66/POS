# Frozen closing report lifecycle context

Schema 34 adds a Branch-owned companion snapshot to each newly accepted
CloseShift/CloseStore report. It captures original store opening attribution and
cash, the ordered shifts known at that close, their original opening attribution,
optional informational opening cash and completed closing records. This adds the
opening/all-shifts/NotReconciled facts required by D04.11.7 to the POS report view.
It does not claim that every D04.11.7 reporting field is complete.

## Capture and immutability

The lifecycle command first validates the existing StoreSession aggregate, writes
its accepted result and existing cash/outbox snapshots, then inserts
closing_report_lifecycle_snapshots in the same SQLite transaction. The context is
built from the aggregate's original operations and closings, including the closing
being committed. Failure to insert context rolls back the entire close, including
store state, accepted request, cash summary and outbox row. Retrying that original
uncommitted request remains safe. A replay of an already accepted request returns
without inserting or rewriting companion context.

The companion identity is branch/request, also unique per branch/store/shift,
with a foreign key to the original outbox report. It is insert-only in the Branch
application. Reads load it in the same transaction as the base report and validate
Branch/request/store/shift/business date, first opening and final closing identity.
Malformed/inconsistent saved context returns unavailable rather than reconstructing
facts from current history. Later shifts, reopened stores, employee/master changes
or delivery confirmation cannot change this frozen context.

Store opening cash remains the original OpenStore amount. Later optional shift
opening cash is informational and is never summed into expected store cash or
used to reset that balance. Null actual cash means NotReconciled; actual zero is
a completed count, not missing data. Stored operation order remains authoritative
when wall clocks regress; this feature does not invent a monotonic-time rule.

## Migration and transport scope

Migration 34 creates the companion table without backfilling old reports. Existing
outbox JSON, IDs, acknowledgements and retry state remain byte-for-byte unchanged.
An old report without a companion returns lifecycle:null, including after an
upgrade. The next close of an already open store can capture the original accepted
aggregate facts at that new close; this never rewrites an older report.

Context is deliberately not appended to schema-1 Central delivery payloads or
cash-summary JSON. It is returned as a separate optional root lifecycle field of
the authenticated Branch report-detail response, alongside snapshot and report.
It is local Branch data and is not covered by the existing Central receipt. The
POS explicitly distinguishes that scope. This avoids mutating already acknowledged
payload identities and avoids growing the bounded Central packet with arbitrarily
many shifts. Central schema remains 2; existing receipt and deduplication semantics
remain unchanged. Full context delivery/export is future integration work.

## Read-only report document

The existing POS saved-report detail now shows original opening metadata, ordered
shift closings and the list/count of uncounted shifts. A separate readable document
preview uses only frozen base snapshot and optional companion facts. Live delivery
metadata and current timestamps are outside that document. Missing legacy context
is visibly unavailable, never filled with today's store/shift information.

The client validates identities, unique shift ordering, first opening and terminal
closing agreement, attribution, reconciliation and safe-cent display values.
React renders text rather than raw stored HTML. Scope/filter/selection and refresh
cancellation continue to prevent stale report display. Read or preview failures do
not change lifecycle commands or clear unresolved requests.

There is no window.print call, browser-to-printer connection, export/download,
print-spool command or physical device interaction. A future printing command must
use the Branch PrintGateway and original report snapshot with its own authorization
and operator confirmation. Cancellation/drawer metrics, non-cash execution and
Back Office reports/notifications remain separate work. D21.3 approval applies to
Branch schema 34 and the optional authenticated read-contract extension.
