# PC POS cash sale and business-day acceptance

Use this runbook as one acceptance work package on a deployed **test** Branch
and test Central, with synthetic register data, two paired POS terminals, a
known Branch timezone, two active employees, and two registered tables. Record
the app, Branch and Central build SHAs and the Business Date. Use simulated
receipts and omit PINs, tokens and pairing links from evidence.

## Establish the baseline

Run the [Edge cash-cycle suite](pc-pos-browser-cash-cycle.md) and retain its JSON
report. PR #400 recorded 32/32 fixture cases. On the deployed Branch, check the
PC POS readiness view and record the Branch response and database accessibility.
A passing readiness probe does not certify writes or cash totals.

## Run the work in this order

| Step | Action | Pass evidence |
| --- | --- | --- |
| Start the business day | Open the store with a 500.00 float and open its first shift using the required PIN. | One store session, one active shift, correct Business Date, and audit attribution. |
| Complete a cash sale | Open a registered table, submit one order for 100.00, request payment, and tender 150.00. | Applied cash 100.00, change 50.00, one completion, one receipt, and table available. Check the non-modal change overlay and table or receipt reference per D04. |
| Verify rounding and split tender | For a synthetic bill of 100.90, verify amount due 100.00. Apply 40.00 by a non-cash method, then tender 100.00 cash for the remaining 60.00. | Effective cash applied 60.00 and change 40.00. Verify the final cash line alone can over-tender, and there is one completion. |
| Close and reopen a shift | Close the first shift without entering a drawer count, then open the next shift before the local date changes. | Expected cash 660.00, actual null, NotReconciled, no cash difference or discrepancy notification. The second shift has its own ID and retains the same store session and Business Date. |
| Sell while Central is unavailable | In the second shift, interrupt only Central connectivity, then submit and settle a 200.00 exact-cash order. Keep the Branch running. | The Branch commits once and shows pending delivery. Expected cash for the store is now 860.00. |
| Check unresolved payment handling | Attempt to close the store while an additional test bill is partially paid. Follow the documented resolution workflow. Keep the bill outside the reported totals unless its settlement commits. | Capture the warning and available resolution steps. Check that the store close snapshot is created only after satisfying its preconditions. |
| Close the store explicitly | With the test ledger unchanged, close the store while Central is still disconnected. Enter 850.00 and use an active employee PIN. Separately verify that a missing count fails and a count of zero is accepted in a disposable test session. | The store and last shift close once. Snapshot records expected 860.00, actual 850.00 and difference -10.00, with cashier, timestamp, session and shift attribution. |
| Restore Central | Reconnect the test Central and wait for delivery. | Branch backlog drains. Central has one event per committed sale and one store-closing snapshot, including the discrepancy. |
| Review prior-day activity | Inspect prior-date results by receipt and table, and verify the authorized cancellation or refund workflow where supported. | The order, receipt, shift, employee and audit activity can be traced. Any approved adjustment is visible separately, and the original CloseStore snapshot stays unchanged. |
| Open the next Business Date | Use an environment with a controlled Branch clock or repeat after the actual date rollover. Open a fresh store session with an independently recorded float. | New date and session, no carry-over of prior-day sales or discrepancies, and no duplication on replay of prior-day events. |

If resolving the partially paid bill adds an effective cash amount, record that
amount and adjust expected cash and counted cash by the same amount before
asserting the -10.00 difference. Use the cumulative **applied** cash, rather than
cash tendered, for reconciliation.

## Record acceptance and implementation gaps

For each row, record PASS, FAIL, or BLOCKED, with observed and expected values,
the affected requirement and references to sanitized evidence. If a production
control is unavailable, record that row as BLOCKED and capture the missing
normal entry point. The BrowserHost test cash endpoints and simulated receipts
provide regression coverage for the fixture only. D04 production payment,
store-lifecycle controls and close reports require their own deployed evidence.

Keep fixes, focused tests and the deployed acceptance record together in the
same implementation follow-up. Physical printer and drawer tests remain
deferred. References include D04.5, D04.11, D04.11.5, D12.9 and the D04 PC POS
screen requirements in [POS Requirements v0.2](requirements/POS-Requirements-v0.2.md).
