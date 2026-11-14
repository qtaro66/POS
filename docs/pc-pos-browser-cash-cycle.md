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
origin. It issues a single pairing challenge and does not start Central.
