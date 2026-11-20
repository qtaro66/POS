# Open tables in store and shift summaries

The Branch workspace reports current open table sessions. Ordering,
PaymentPending, PaymentInProgress and PartiallyPaid are open. The last three
states are awaiting payment. Completed, Cancelled and Closed are excluded.
The count is scoped to the authenticated Branch, including sessions awaiting
handover from a previous shift.

Closing captures these counts with the cash summary in the lifecycle
transaction. Later table changes cannot alter a recorded closing. Historical
snapshots without table counts remain unavailable rather than showing zero.

A partially paid table blocks both CloseShift and CloseStore. The Branch
returns the affected table session and table IDs in a structured conflict
response. A rejected close creates neither a lifecycle result nor a closing
snapshot. After the operator resolves the payment state, the same uncommitted
request can be attempted again.

The PC POS workspace distinguishes current Branch counts from saved closing
counts. When a close is blocked, its dialog identifies the tables and directs
the operator to the table workspace. PIN handling and uncertain-request recovery
follow the existing lifecycle command rules.

This adds D18.9 waiting-payment counts and the D04.11.7 open-session count.
Non-cash payment execution, cancellation report totals and Central lifecycle
report delivery remain integration work.
