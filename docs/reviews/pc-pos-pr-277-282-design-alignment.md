# PC POS design alignment review: PRs 277-282

## Result and reviewed baseline

Reviewed the merged changes and GitHub metadata for PRs 277-282 against the
user-supplied generated mockups and the confirmed requirements reading copy.
Baseline: `6fa7b6ff50d50ee619c41d77df1166857ef338a9`, the merge of PR 282.

The six PRs provide reusable ordering infrastructure and a partial read-only UI.
They do **not** complete the pictured ordering workspace. Keep the persistence,
identity, submission and recovery behavior; compose its UI to the established
design rather than treating standalone development previews as a new design.
This review does not change runtime code or claim that the visual gaps are fixed.

The separate `feature/pc-pos-persistent-basket-panel` handoff was awaiting its
result when this review started. It is not part of the reviewed six merged PRs.
Do not assume that component has merged or overwrite its continuation pointer.

## Sources and precedence

- D18.1: [navigation and table workspace](../requirements/POS-Requirements-v0.2/029-d18-1-navigation-pc-pos.md).
- D18.6: [basket, sent orders and menu selection](../requirements/POS-Requirements-v0.2/034-d18-6.md).
- D18.9: [store/shift layout and sync labels](../requirements/POS-Requirements-v0.2/037-d18-9.md).
- [Reference manifest](pc-pos-reference-images.json): exact filenames and SHA-256
  fingerprints for four inspected ordering examples from the supplied collection.

The image collection contains revisions, not 25 simultaneously authoritative
screen specifications. Current confirmed requirements take precedence over old
image text or sample values. In particular, D18.9 uses `Data Sent`, not the older
`Data Sended`. Mockup prices, times, employee IDs, statuses and totals are examples,
not production data, calculations or permission grants. Do not reproduce image
generation artifacts or obsolete labels just to match pixels.

## PR disposition

| PR | Responsibility | Alignment disposition |
| --- | --- | --- |
| [277](https://github.com/qtaro66/POS/pull/277) | Read-only active-table/sent-order workspace, protected App entry, layout and preview | Reuse data rendering and stale-response protection. Compose the missing ordering view and shared navigation instead of declaring this initial table/detail view a finished D18.6 screen. |
| [278](https://github.com/qtaro66/POS/pull/278) | Immutable command snapshots, journal and recovery | Keep. This infrastructure imposes no conflicting visual design. Preserve original request IDs/payloads and distinguish unknown results from rejection. |
| [279](https://github.com/qtaro66/POS/pull/279) | Optional command-inspection drawer | Keep as an auxiliary recovery surface. Fit it into the shared shell without replacing the primary basket/menu workflow. It intentionally performs no resend from its inspection controller. |
| [280](https://github.com/qtaro66/POS/pull/280) | Transactional basket persistence and journal binding | Keep. Generation/revision checks, pending immutability and atomic cleanup remain necessary behind the pictured basket controls. |
| [281](https://github.com/qtaro66/POS/pull/281) | Submission, preflight and durable recovery coordination | Keep. A visual change must not introduce new IDs for uncertainty, automatic resend, local authorization or client-owned final prices. |
| [282](https://github.com/qtaro66/POS/pull/282) | Persistent basket editing controller | Keep and connect to the pictured view. It already supports per-line fulfillment, persistent editing, menu-version checks and recovery, but does not render the full screen. |

## Findings and corrective outcomes

### F1 - Two related workspaces must not be conflated (high)

`SentOrdersWorkspace.tsx` currently renders active tables on the left and accepted
order details on the right. That is a partial implementation of **D18.1's table
workspace**, not inherently the wrong layout for that workflow. The missing part
is **D18.6's order-entry workspace**: a back-to-tables header, basket/sent tabs on
the left, and horizontal categories/menu on the right, under the same selected
table session. Preserve the table overview; do not merely reverse its columns
and call the ordering screen complete.

Evidence: `apps/pos-web/src/ordering/SentOrdersWorkspace.tsx`,
`apps/pos-web/src/App.css` and `apps/pos-web/src/ordering/readController.ts`.
`App.tsx` mounts only the read-only workspace; it does not compose the basket
editor/controller into the pictured tabs and menu picker.

### F2 - Menu interaction is still missing, although option data exists (high)

The existing MenuItem/SessionMenu DTOs carry item kinds, option groups, SubItem
and Topping options, availability, prices and selection constraints. Reuse those
fields for two distinct paths:

1. With Sub Item: select the menu, show the separate right-hand sub-item column,
   select the sub-item, then show a Topping-only popover and add-to-basket action.
2. Without Sub Item: selecting the menu opens the Topping-only popover directly.

Do not put the base ingredient/Sub Item selector back inside the Topping popover.
Respect group cardinality/repetition and disable unavailable selections. The
confirmed label is the Thai add-to-basket action in D18.6; no separate draft-save
button or second list duplicating the Sent tab is required.

### F3 - Some mockup fields cannot be populated from these DTOs (high)

`orderingContracts.ts` has no category identity/order/label metadata. Do not
invent category membership from item names or identifier prefixes. A stable
category source/mapping must precede working horizontal category tabs.
`sellableItemId` also must not silently become a business display code if the
catalog uses a separate code.

The active-table response explicitly says `includesAvailableTables: false` and
does not provide a complete zone/available-table registry. It cannot produce the
full floor plan alone. The reviewed session view also does not supply an opening
timestamp for elapsed time or a complete host store/shift/sync status model.
Connect appropriate real data sources; never manufacture Available tables,
opening durations, store-open indicators, employee/shift details or green online
states from the mockup examples. This is a gap in this reviewed UI composition,
not a claim that every corresponding backend capability is absent elsewhere.

### F4 - The common shell/navigation is incomplete (medium)

The current sidebar shows only Tables. The shared shell should follow D18.1:
Home, Tables, Transaction History, Store and Shift, and System Status. Top-bar
state and bottom-sidebar sync labels need real scoped data. Do not enable
unimplemented pages or protected commands as decorative working buttons.

Screen lock and payment-related table lock are different controls. Preserve
screen locking from App. Table lock/unlock must use RequestPayment and
CancelPaymentRequest with real Branch state, never a new browser-only isLocked
flag. These actions are not implemented by the reviewed ordering client.

### F5 - Basket estimates and accepted totals need distinct provenance (medium)

The references place an estimate above the basket's fixed submit footer and
accepted batch amounts in the Sent tab. The current bill-preview totals describe
accepted Branch data; they are not a quote for unsent basket rows. Do not reuse
that total for an unsent basket or copy sample image amounts.

Retain a place for the confirmed estimate display. Its implementation requires
an appropriate Branch quote/calculation source or an explicitly agreed,
clearly labeled indicative-display policy. Do not silently introduce new tax,
discount, charge or rounding rules to make the mockup show a number. Accepted
batch/bill amounts remain authoritative Branch values. Dine-in/takeaway groups
stay inside the same session/batch/bill and do not become TOGO sales channels.

### F6 - Development previews and passing tests do not prove visual completion (medium)

The existing tests establish behavioral safety, not correspondence to the whole
mockup. A standalone panel preview is a component exercise, not a replacement
design. Add viewport-based visual checks after integration, preserving the
existing command/storage/authentication checks. No review result here asserts
production session provisioning, real Branch integration or hardware validation.

## Integration sequence

1. Resolve the outstanding basket-panel handoff through its own exact branch
   protocol before integrating that component. Do not rewrite merged PR history.
2. Compose a shared PC shell and retain separate table overview/order-entry modes
   under the selected table session. Navigation back must preserve local drafts.
3. Bind Basket/Sent tabs to their existing controllers. Keep Sent read-only and
   refresh authoritative accepted data after a proven BranchAccepted outcome.
   Capture/present command outcomes before refresh clears transient controller
   state; local-application failure and uncertainty retain original recovery IDs.
4. Add the menu/Sub Item/Topping flow using current frozen menu/availability data.
   Supply category/display-code/other missing metadata through explicit contracts
   and mapping; do not guess it in UI code.
5. Provide correctly sourced estimate/status/floor-plan data and the confirmed
   payment-state lock actions in their corresponding scoped work. Any contract,
   financial, security or migration change retains D21.3 approval requirements.
6. Compare integrated views at desktop sizes including 1280x720 and 1920x1080,
   covering both option paths, mixed fulfillment, Sent rounds, Sold Out, pending
   recovery, locks, missing data and failed reads. Keep action footers reachable
   and independently scroll long row lists. Compare layout/workflow, not generated
   typography glitches or illustrative sample numbers.

## Visual acceptance checklist

- [ ] Shared navy/teal shell and the confirmed navigation are used consistently.
- [ ] Table overview and order-entry modes are distinct without losing session scope.
- [ ] Entry has Basket/Sent on the left and category/menu selection on the right.
- [ ] Categories are horizontal; there are no menu photos, search field or vertical category rail.
- [ ] Sub Item uses its own column; the following popover contains only Topping.
- [ ] No-Sub-Item selection opens the Topping popover directly.
- [ ] Sold Out items/options cannot be added and no stock-balance guess overrides that rule.
- [ ] DineIn is the initial new-line default; switching it affects future additions only.
- [ ] Both tabs group per-line DineIn/Takeaway while preserving options/comments.
- [ ] Basket controls affect only unsubmitted rows; there is no separate draft-save button.
- [ ] The fixed submit footer counts basket lines; any estimate has defined provenance.
- [ ] Sent displays real batches, acceptance and Branch amounts with no duplicate-production edits.
- [ ] Unknown/pending requests preserve identity and cannot produce fresh replacement orders.
- [ ] Screen lock, payment-state table lock and protected operational actions remain distinct.
- [ ] No sample online/shift/timing/status or monetary values ship as real state.

These unchecked items are the future integrated-screen acceptance criteria, not
a claim that they already pass. This documentation-only review does not change
requirements, schemas, commands, runtime UI or the outstanding task's handoff.
