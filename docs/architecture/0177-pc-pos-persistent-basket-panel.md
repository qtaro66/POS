# PC POS persistent basket panel

Build the visible basket pane over PR #282's editor controller. Render committed
local rows grouped by DineIn/Takeaway, preserving comments and options. Quantity,
fulfillment, comment and removal edits use the persistent controller; there is no
separate draft-save button. Changing the new-line fulfillment selector cannot
rewrite existing rows. The menu picker remains a separate follow-up component.

Display names and individual unit/option prices only from a matching frozen
session menu. If unavailable or version-mismatched, retain local item codes and
withhold prices rather than silently reinterpret the basket against a new master.
Do not calculate taxes, discounts, charges, net totals or receipts in this pane.
Reference the accepted-order view for Branch-confirmed amounts.

Disable draft mutations during I/O, session/menu blocks and pending commands.
Keep submitted uncertainty separate from durable acceptance/rejection. Pending
rows have explicit GET-only result checking and same-ID retry controls, with no
replacement request, automatic replay or pending deletion. A durable outcome with
incomplete local application remains a real Branch outcome plus a local warning.
Branch acceptance does not prove physical printing or payment completion.

The controller refreshes on mount and cancels obsolete work on unmount or
replacement. Result notices receive keyboard focus. Stable committed rows retain
quantity-button focus after IndexedDB writes; explicit comment application keeps
focus without inheriting another row's uncommitted form text after revision
changes. The row region scrolls independently of the persistent action footer.

A DEV-only basket preview uses fake-indexeddb in memory and injected synthetic
HTTP responses, never real browser storage or a Branch endpoint. The fixture and
its emulator must be absent from production bundles. UI tests cover committed
editing, mixed fulfillment, frozen-price withholding, locks, outcomes, recovery,
same-request retry, focus and identity/controller replacement. Browser inspection
uses only that marked preview, with no printer or cash drawer access.

This is the basket pane, not completion of D18.6. Menu/category selection, Sub
Item column, Topping popover, combined Basket/Sent workspace, production session
provisioning and real Branch integration remain future work. Production App still
defaults to sign-in; no runtime mutation control is enabled. No backend/schema,
dependency version, pricing, permission or confirmed business rule changes.
D21.3 explicit approval is required before merge for authenticated mutation UI
and recovery behavior. Any real hardware test remains separately operator-gated.
