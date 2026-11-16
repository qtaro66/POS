# PC POS browser cash-cycle acceptance

Run in an isolated test Branch with synthetic data, a disposable browser profile,
and the PC POS web app served over trusted HTTPS. Provision two PC POS devices
and an employee authorized for ordering. Seed a sellable item with a known price
and two available tables. Enable the test cash cycle and use simulated receipts.
Use a disposable Central endpoint for the reconnect scenarios.

Record browser version, Branch and app commits, fixture totals, and pass/fail
for each row. Keep PINs, pairing links, tokens, and device IDs out of evidence.

| Scenario | Steps and expected result |
|---|---|
| Pair and log in | Pair the first browser with its PC POS link. Try an invalid PIN, then the authorized PIN. Confirm that reloading retains pairing and requires signing in again. |
| Start shift | Open with 500.00 and confirm the shift. Try a second open and confirm only one shift is active. |
| Sell | Open a table, add a priced item, change its quantity, and submit. Reload and sign back in. Verify the sent quantity, amount, and table state. |
| Settle | Request payment and try tender below the amount due. Verify no settlement. Complete the first sale with exact tender and the second with overpayment. Verify the applied amounts and change. |
| Concurrent settlement | Sign in on both devices, open the same payment on both, and submit from both. Verify one settlement, receipt job, and cloud completion event. Refresh the stale view and verify the table is available. |
| Receipt | Compare the simulated receipt with the settled sale. Reopen the receipt and check that the simulation result is stable and there is no second payment or receipt job. |
| Outage and restart | Stop only the test Central endpoint. Complete a test sale and verify the Branch cloud status shows pending work. Restart the Branch process using the same database, sign back in and verify the closed table and persisted pending work. |
| Reconnect | Restore Central. Verify the Branch backlog drains and Central records one completion per sale. |
| Reconcile | Close a test shift with opening float plus applied cash amounts. In another shift enter a deliberate discrepancy. Verify zero and the signed difference respectively. |

## Local Branch fixture

From the repository root, build and start the loopback-only host.

```pwsh
npm run build --workspace apps/pos-web -- --mode browser-fixture
dotnet run --no-launch-profile --project tests/Pos.PcPos.BrowserHost -- (Resolve-Path apps/pos-web/dist).Path
```

Open https://127.0.0.1:17443/ in a disposable browser profile. The host
creates an ephemeral self-signed certificate for loopback. Accept the
certificate for this fixture only. Use the one-time link and synthetic PIN
printed by the host. Stop with Ctrl+C to remove the temporary database.

This fixture serves the built UI and real Branch routes from the same
origin. It issues two one-time pairing challenges and does not start Central.

Run the loopback HTTPS smoke test from the repository root after building the
browser fixture. Stop any existing fixture host first.

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-CashSale.ps1 -Dist apps/pos-web/dist
```

The smoke test sends HTTP requests and restarts the Branch fixture with its original database. It checks that paired devices, the settlement and receipt outcome, the released table, and the shift balance survive the restart. Use the browser checklist above for UI
acceptance.

The smoke test starts a separate loopback Central fixture after Branch
restarts. It uses the Central ingestion handler with an in-memory store,
and replaces the first successful cash acknowledgment with HTTP 503.
The test checks ordered ingestion, a single stored cash event, a
duplicate delivery, and an empty Branch outbox. It uses ports 17443 and
17444 and does not require PostgreSQL.

Run the smoke test sequentially with `-Scenario Balanced`, `-Scenario Short`,
and `-Scenario Over` to exercise counted cash of 700, 690, and 710 against
an expected 700. Short uses exact tender; Balanced and Over use overpayment.
Every run rejects insufficient tender and verifies restart and reconnect.

## Automated Edge acceptance

With Microsoft Edge and Playwright 1.65.0 available to Node, run from the
repository root after stopping other fixtures on port 17443.

```pwsh
npm run build --workspace apps/pos-web -- --mode browser-fixture
dotnet build tests/Pos.PcPos.BrowserHost
node tests/Pos.PcPos.BrowserHost/browser-cash.cjs apps/pos-web/dist
```

The Edge runner opens a registered table from the available table list in PC POS.
It then uses browser controls for ordering, cash, receipt and shift close.
Concurrent opens the same table from two Edge contexts. EmployeeIsolation
submits from a stale available-table list. Both verify that Branch returns the
same session to both devices and that a refresh removes the available-table
action.

LostResponse drops a committed open response. ReloadUnsent blocks the open
request before it reaches Branch. Both reload the tab and sign in before
recovering the original request and continuing the sale.
The observer explicitly refreshes the table list after settlement.

Run all thirty combinations of recovery mode and shift outcome with the same build.
Each case restarts Branch, recovers the original cash request, checks the
shift difference, and verifies delivery to the in-memory Central fixture.
Keep both fixture ports (17443 and 17444) free.

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashRecovery.ps1
```

`ReloadLost` drops the response after Branch accepts payment. `ReloadUnsent`
drops the request before Branch receives it. The expected shift differences
are 0, -10, and +10 respectively for Balanced, Short, and Over.

EmployeeIsolation and EmployeeIsolationUnsent verify that switching employees hides the pending cash recovery action and that the original employee can recover the original cash request after a dropped response or a request that never reached Branch.

### Cash-cycle acceptance coverage

The runner covers seven single-shift modes across three closing cash outcomes and all nine pairs of ShiftHandoff outcomes.
Central ran with an in-memory store and receipt printing was simulated.

| Mode | Balanced | Short | Over |
| --- | --- | --- | --- |
| Concurrent | Yes | Yes | Yes |
| LostResponse | Yes | Yes | Yes |
| ReloadLost | Yes | Yes | Yes |
| ReloadUnsent | Yes | Yes | Yes |
| EmployeeIsolation | Yes | Yes | Yes |
| EmployeeIsolationUnsent | Yes | Yes | Yes |
| MultiSale | Yes | Yes | Yes |
| ShiftHandoff | Yes | Yes | Yes |

To rerun a specific mode and shift outcome, pass both filters. Omitting them
runs the full thirty-case suite.

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashRecovery.ps1 -Modes EmployeeIsolationUnsent -Scenarios Short
```

Use `-ListCases` to preview the selected mode and shift combinations without
building or launching the fixtures. The same filters apply to the preview
and the actual run.

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashRecovery.ps1 -ListCases -Modes ReloadLost -Scenarios Short
```

The LostResponse and ReloadUnsent modes also interrupt order submission.
After restarting Branch with the same database, reloading, and signing in,
LostResponse checks the durable result without
another POST. ReloadUnsent explicitly resends the original request body.
Both modes continue through cash settlement and shift close. Every mode verifies
that Central receives one SubmitOrder with the original request ID and one CompletePayment.

MultiSale settles A01, restarts Branch with A02 still open, then settles A02
and a new A01 session. It checks three distinct receipts and sessions, three
order and payment events in Central, and expected shift cash of 900.

ShiftHandoff closes the three-sale shift, switches employees on the same
terminal, then opens the next shift with 300. After a Branch restart, it
replays the original receipt, sells another bill and expects 400 in the new shift.
Central must receive four orders and four settlements in sequence.

ShiftHandoff verifies that the first three sales share the original shift and
employee, while the fourth belongs to the new shift and employee. Compare
the shifts across all nine pairs of balanced, short, and over, then replay
the fourth settlement after closing to confirm the original result.

Filter the second shift independently with "-NextShiftScenarios". This filter
applies to ShiftHandoff cases only. For example:

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashRecovery.ps1 -Modes ShiftHandoff -Scenarios Short -NextShiftScenarios Over
```

ShiftHandoff also verifies that an invalid PIN cannot close the first shift,
a repeated close with a changed count returns a conflict, and the next employee
can open a shift after Branch restarts with the same database.

The cross-shift cases read the disposable Branch database in read-only mode
to compare both persisted close records (cash, difference, employee and time)
with the API responses and check that no open shift remains.

The cross-shift audit also joins each cash sale to its simulated receipt job,
recalculates each shift from its opening cash and applied sales, and matches
payment request IDs through the Branch outbox to the events acknowledged by Central.

After both shifts close, the cross-shift cases restart Branch once more,
compare all four rendered receipts with the persisted sale amounts, and
verify that asking for the opposite simulated outcome keeps the original
success or failure result for each receipt.

While the second shift is open, the cross-shift test retries the first shift's
close and attempts to close the new shift with another employee's PIN. It
then verifies the current shift still accepts sales and closes on its own ID.
