# Consolidated browser acceptance for captured cash history

This joins the independently tested cash-history views in a real Edge browser
against the existing loopback Branch fixture. It covers the implemented D18.8
and D01.4 read-only cash slice; it does not declare all transaction sources,
authorized reprint or physical receipt requirements complete.

## Isolated fixture boundary

POS_BROWSER_HISTORY_FIXTURE=1 selects an explicit mode in the non-publishable
Pos.PcPos.BrowserHost test assembly. Seed only when its disposable database is
first created. The fixture process uses existing application/store commands to
create captured cash, cancellation, durable lifecycle and simulated receipt
records. Dispatch-store calls create pre-send failures and a final Unknown
attempt using synthetic outcomes and a .invalid printer profile. No dispatcher
connects to a device and no physical printer or drawer is exercised.

The fixture adds no HTTP mutation or inspection endpoint. The browser uses the
same pairing, login and Branch history routes as the existing UI. A restart uses
the same database and fresh fixture login PIN without reseeding business data.
Other existing fixture modes retain their behavior. Central delivery is disabled
in this mode, allowing the local history reads to be tested without a Central
service or background outbox mutations.

The setup deliberately includes a fully cancelled original line that is absent
from the chargeable bill, a remaining takeaway line, two captured cash sales,
different historical shifts, an unreconciled close, a counted store close and a
new current store. Original item names remain captured after current menu data
changes. A successful simulated receipt and a failed result remain distinct from
original kitchen print evidence. More than twenty attempts exercise actual
continuation instead of fabricating a future multi-job source.

## Browser case matrix

| Case | Boundary exercised |
| --- | --- |
| Flow | Pair/login, split cash detail, original bill, cancellation-to-original-line focus, lifecycle, receipt and original print evidence |
| Filters | Captured receipt/session/table/order identities and subject selection through visible controls |
| PrintPaging | Original job uncertainty and recorded attempt continuation without duplicate membership |
| Reload | Pairing/login and captured history survive a tab reload |
| BranchRestart | The same captured database survives process restart without reseeding |
| SourceFailure | A failed or malformed evidence response remains local and preserves cash/bill/other evidence |
| StaleRead | Pending old responses cannot restore evidence after a subject, refresh or lock boundary |

Failure injection is at the browser transport boundary. No browser or Node code
writes SQLite. Existing API/persistence tests, rather than transport simulation,
prove stored snapshot-hash validation and server-side corruption handling.

## Read-only and evidence rules

An external Node observer opens SQLite read-only and fingerprints selected
business tables before and after browsing. Pairing/session authentication tables
are excluded because pairing, login and explicit screen-lock session closure
are expected authentication transitions. The runner separately requires exactly
one session-end request when the lock case clicks Lock and none in other cases. Cash,
order, cancellation, audit, lifecycle, receipt, print, payload and business outbox
data must remain unchanged. No browser script gets a database connection.

Run cases sequentially with disposable Edge contexts, an ephemeral loopback TLS
certificate and the fixture's owned temporary directory. Cleanup waits for owned
child processes before removing a verified GUID-named directory under the OS
temporary root. Existing user browser processes are not terminated.

Reports record the exact commit, clean-tree flag, complete selected case plan,
case completion/failure, observed Edge version and read-only result. A native
exit code of zero without valid matching browser evidence is a failure. Stop on
failure by default; continuing does not turn failures or unattempted cases into
passes. Reports are written atomically without overwriting an existing report.

Keep generated reports under ignored .handoff paths. Do not include fixture PINs,
pairing links, credentials, tokens, device identifiers, browser storage or raw
request headers. Versioned documentation records the commands and scope;
machine-generated reports remain local.

## Acceptance limits

These checks exercise local Edge, the actual built PC POS UI, real Branch HTTPS
routes and synthetic SQLite records. They do not prove deployed TLS, production
latency, Central/PostgreSQL delivery, Android/iOS behavior or physical paper/drawer
results. Transfers, other payment methods, uncaptured historical fields, refunds,
authorized reprint and Back Office/Stock remain outside this cash-history slice.
No production authorization policy, database schema or business rule changes are
introduced by the acceptance harness.
