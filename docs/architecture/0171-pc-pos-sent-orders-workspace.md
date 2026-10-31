# PC POS read-only sent-order workspace

Connect the confirmed sent-order view to the read adapter from PR #276. Render
active sessions, accepted batches and Branch-calculated amounts without
reconstructing acceptance from a local basket or computing financial values.
Group lines by DineIn/Takeaway, retain options/comments and cancelled quantities,
and clearly distinguish current estimates from receipts or received payments.

The controller invalidates financial content before refresh, ignores superseded
responses, rereads unchanged table versions after refresh, and cancels obsolete
reads. A new client creates a new controller synchronously so old branch data
cannot flash during employee/device identity changes. Unknown/closed sessions,
read failures and empty bills have explicit states with no fabricated totals.

App requires a caller-supplied authenticated read client. Its default entrypoint
is blocked at sign-in because production credential provisioning/PIN UI is not
implemented. UI locking immediately hides data and cancels reads; a fresh
authenticated client must be supplied by the future PIN flow for re-entry.
This is not server token revocation, automatic locking, or a complete D18.4 flow.

A development-only synthetic preview supports visual review without credentials
or network calls to a Branch. Vite production builds omit its data module. Tests
use injected ports/fetch, Node, Vitest/jsdom and the testing package versions
already used by Mobile. No production dependency versions are introduced.

Visible order-entry, master floor plan, session provisioning, push/live updates,
payment, mutation controls and hardware validation remain outside this slice.
The root gate gains the PC React tests without removing the existing adapter
tests or weakening CI. Privacy-related UI guards and test dependency changes
require D21.3 review/approval before merge.
