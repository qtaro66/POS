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

## Windows installation, upgrade and recovery

Run the Windows checks and the cash/day-close scenario as one acceptance
package. Use disposable Windows environments and a test Central. Use the
release installation, migration and backup procedures for the build under
test. Record a blocked row if a required artifact or procedure is unavailable.

1. **Clean install before the cash scenario.** Record the artifact checksum and
   application, API and schema versions. Verify the Branch Windows Service starts
   after reboot without opening Edge, readiness is reachable over the configured
   HTTPS origin, and the paired POS can sign in and load the test table registry
   and menu. Capture a baseline before creating transactions.
2. **Crash and restart during the cash scenario.** After the second-shift sale
   is acknowledged and Central is disconnected, record the active shift, sale,
   receipt and pending event IDs. Terminate the test Branch process and verify
   recovery with RPO 0 and RTO at most 5 minutes. Repeat with a Windows restart
   and verify RPO 0 and RTO at most 15 minutes. Measure from disruption until the
   Branch can accept another business command. Replay the acknowledged request
   using the original identity and request ID, and verify a single settlement
   and one corresponding outbox record. Continue the close-store and reconnect
   steps after the restart checks.
3. **Upgrade and rollback.** Preserve a pre-upgrade test backup. Upgrade a copy
   of the populated Branch using a supported previous build as the source.
   Compare Business Date, store/shift state, accepted orders, applied cash,
   receipts, audits and outbox IDs before and after. Verify the release rollback
   procedure separately on a disposable copy, including a compatible database
   restore if required. Record versions, elapsed time, and the evidence for
   preserving the authorized recovery point.
4. **Restore on another Windows instance.** Restore a supported recovery bundle
   to an isolated test Branch with cloud delivery disabled until the ledger
   comparison is complete. Compare orders and totals, payments and cancellations,
   table sessions, deduplication records, audit history, print and cloud outboxes,
   and any stock effects enabled in the test configuration. Reconnect only to
   test Central and verify replay neither loses events nor duplicates them.
   Measure RPO from the last recoverable accepted transaction and RTO from the
   outage to restored service. Whole-host loss targets are RPO at most 5 minutes
   online or 15 minutes offline and RTO at most 4 hours, subject to the backup
   media and replacement-host prerequisites in D12.8.

Use a single acceptance record with the build and environment versions, test
step, PASS/FAIL/BLOCKED, expected and observed results, elapsed recovery time,
and sanitized evidence references. Preserve any release or recovery blockers
alongside the cash-cycle results. A virtual Windows restore exercises software
recovery. Verification of physical backup media, replacement hardware, and
printer or drawer devices requires the corresponding equipment.

See [D12.8 backup and recovery](requirements/POS-Requirements-v0.2/017-d12-8-backup-restore-rpo-rto.md)
and the Windows deployment requirements in [POS Requirements v0.2](requirements/POS-Requirements-v0.2.md).

## Single-Branch operational pilot

### Staff rehearsal

Use synthetic sales at one selected Branch. Assign an on-site lead, cashier,
shift lead, and Central reconciliation owner. Give the staff the Windows
acceptance sequence above and have them open the store, submit and settle orders,
hand off a shift, and close the business day themselves. Include one Central
disconnection and reconnect. Rehearse the fallback with synthetic transactions.
Record which steps required assistance and resolve issues affecting the ability
to sell, close, recover, or reconcile before moving to live sales.

### Go/no-go and one live shift

The on-site lead and reconciliation owner review the deployed acceptance
results, staff rehearsal, recovery evidence, and the readiness of hardware
required for the live shift. Until the required printer and drawer checks pass,
limit the pilot to rehearsal. Agree on the cutover time and the legacy fallback
process before opening the pilot shift.

Record the Branch, Business Date, build versions, opening cash, and first
receipt reference. Use PC POS as the register of record for the pilot shift.
Monitor settlement, receipt issuance and Central delivery. At shift end,
reconcile physical cash, effective cash payments, receipt references, and the
Central ledger. Record any unresolved differences and pending deliveries.

### Fallback and decision

If the team cannot safely sell, issue a valid receipt, recover the service, or
reconcile settlement, stop new PC POS sales and capture the last confirmed
receipt and unresolved requests. Reconcile uncertain payments against their
recorded status before re-entering anything in the legacy register. Resume new
sales in the legacy register under the agreed fallback process. Preserve the
pilot ledger and outbox for reconciliation, and record the cutover time and
first legacy receipt. Confirm the combined shift totals and resolve exceptions
before another live pilot.

Keep one pilot record with the rehearsal, go/no-go decision, live shift result,
fallback if used, reconciliation, issues, fixes and retest evidence. Expand
beyond one Branch only after the pilot is reconciled and the owners approve the
next rollout. Use PASS, FAIL, or BLOCKED for each stage and link sanitized
evidence.

### Pilot readiness evidence

At main commit f05271b (PR #403), automated Edge fixture checks passed 3/3:
ShiftHandoff (Balanced to Short), ShiftTransitions, and BranchIsolation.
These cases exercised shift reconciliation, recovery after a Branch process
restart, receipt separation, and fixture Central reconnect.

From the repository root in PowerShell, reproduce the check with a new report:

```powershell
$report = Join-Path ([IO.Path]::GetTempPath()) ("pc-pos-pilot-" + [guid]::NewGuid() + ".json")
& ./tests/Pos.PcPos.BrowserHost/Test-EdgeCashRecovery.ps1 -Modes ShiftHandoff,ShiftTransitions,BranchIsolation -Scenarios Balanced -NextShiftScenarios Short -ReportPath $report
```

Deployed pilot status is BLOCKED. The checkout exposes shift and simulated
receipt operations through the enabled test cash-cycle routes. The production
OpenStore and CloseStore entry points and an installable Windows release were
not found in this checkout. No deployed staff rehearsal, physical device
validation, or live shift was run.

The next implementation dependency is the production store and shift
vertical slice from D04.11. Start with backed, auditable operations and the
PC POS store-and-shift screen, then verify a single explicit store close
across two shifts before running the deployed acceptance plan.
