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

The script opens the table through the authenticated Branch API because the
PC POS table list exposes active sessions. It then uses browser controls for
item selection, quantity, order submission, cash, receipt and shift close.
The observer explicitly refreshes the table list after settlement.
