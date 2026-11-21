# Branch report lifecycle companion v1

GET /api/v1/ordering/store-lifecycle/closing-reports/{requestId} retains its
existing authentication, Branch scope, PcPos role and no-store behavior. The
response adds optional root lifecycle beside branchId, report and snapshot.
An old server can omit it; schema-34 servers return null for historical reports
without a saved companion. snapshot continues to project the unchanged original
Central-delivery packet. lifecycle is local Branch context, not acknowledged
Central content.

| Lifecycle field | Meaning |
| --- | --- |
| schemaVersion | 1 |
| branchId / requestId | Same authenticated Branch and original report request |
| storeSessionId / closingShiftId | Same store and terminal shift as snapshot |
| businessDate | Original store business date |
| openingCashAmount | Original store OpenStore cash amount |
| opened | Original store opening attribution |
| closesStore | Matches the base report's terminal closing |
| shifts | Nonempty ordered array known at the recorded close |

Each shift has shiftId, openingCashAmount (nullable), opened and closing. opened
uses the existing employeeId/deviceId/occurredAt/branchLocalDate shape. closing
uses the existing immutable ShiftClosing shape. All listed shifts have a closing;
only the terminal shift may close the store and its closing equals the base
snapshot closing. Earlier shifts never close the store. Shift IDs are unique;
the first shift's opening context/cash match the store opening. The terminal shift
matches closingShiftId. Ordering comes from original accepted operations, not
sorting wall-clock timestamps.

Optional shift cash is informational. It never adds to or resets store opening
cash. Each closing preserves its original expected/actual/difference; null actual
and null difference mean NotReconciled, while actual zero means Reconciled.
The UI may enumerate uncounted shifts from these saved facts without recalculating
sales, expected balance or financial totals from current state.

A missing companion row is null. An existing malformed or identity-inconsistent
row produces 503 without raw JSON/exception disclosure; it is never silently
replaced using live timeline data. A foreign Branch request remains 404. Reading
this field does not claim/retry/confirm work or modify accepted commands.

No change is made to the Central POST /api/v1/closing-reports schema-1 body or its
bounded size. Existing report JSON and receipt identity remain unchanged after
migration and repeated reads. The companion has its own local schema-version1;
it does not bump the base packet schema. Branch database schema 34 is required
for new capture/read support; Central stays at schema 2.
