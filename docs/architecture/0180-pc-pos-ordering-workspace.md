# PC POS integrated ordering workflow

## Scope and prerequisites

After verified MERGED PR #286, implement a larger coherent workflow covering
selection, persistent edits, Basket/Sent tabs, submission, authoritative accepted
reads and explicit recovery. Follow D18.6, D08.4, D09, D12 and D20 without changing
the existing Branch API, financial rules, schemas or database migrations.

App remains read-only by default. The host must opt in with a full authenticated
ordering client and an explicit IndexedDB factory. This uses the existing client
identity snapshot, owner-partitioned basket/journal, compare-and-swap writes and
immutable command snapshots. Client replacement must follow actual authenticated
identity changes; no credential provider or local authorization bypass is added.

## Selection and presentation

Basket/Sent use accessible tabs on the left; both panes remain mounted while
switching tabs. Accepted rows stay read-only. The right-hand selector uses the
session-frozen menu and the existing advisory availability predicate. SubItems
are a separate column, followed by a Topping-only non-modal dialog. Menus without
SubItems open Topping selection directly. Optional/multiple SubItem groups can
advance explicitly; a single-choice group advances after its selection.

Selections preserve root item and option-group/item IDs, enforce min/max counts
including repeated quantities, and persist through createBasketEditor.addLine.
No option is inferred/default-selected. No item photos, search, fake categories
or derived menu codes are introduced. Current v1 contracts lack category and
display-code metadata, so only All and opaque item IDs are available. Root and
option prices are shown separately; no final quote or price override is computed.
The shared DineIn/Takeaway selection controls future additions; existing lines
retain independent fulfillment and editable quantity/comment/fulfillment controls.
Beginning menu selection from Sent reveals Basket before confirmation, so the
active next-line fulfillment choice is visible rather than hidden in another tab.

## Outcomes, navigation and recovery

BasketPanel retains ownership of editor refresh/cancel. Tab changes never remount
it. Leaving the workspace cancels presentation, not persisted data; re-entry or
reload uses the same scoped IndexedDB records. A pending basket remains frozen.
Only explicit GET recovery or same-ID retry can resolve/resend it.
While pending, the footer shows those two recovery controls instead of the
disabled new-submit button and unrelated amount hint. The generic refresh-to-edit
hint is withheld because it is misleading for a frozen pending request. Other
issues and durable local-write warnings remain visible. Bounded pane scrolling
keeps recovery controls reachable on the 720-pixel desktop viewport.

On correlated durable BranchAccepted, the workspace first captures the receipt
in an owner-scoped host message, preserving localApplied warnings. It then
refreshes the independent read controller. It does not refresh the basket editor
automatically, invent accepted batches, or replace uncertain commands. Sent reads
hide previous amounts while refreshing. Rejection and uncertainty do not trigger
an acceptance refresh. Existing refreshRequired rules require the operator to
read current data before another edit. Screen lock unmounts all private views;
replacement clients cannot inherit selection, draft scope or receipt messages.

## Evidence and remaining work

Synthetic integration tests cover both option paths, cardinality, Escape/focus,
mixed fulfillment, durable navigation/reload, accepted/rejected/uncertain results,
same-body retry, frozen-menu mismatch, locked preflight, employee isolation and
acceptance with local-write failure. Existing store/editor/recovery tests remain
the integrity foundation. DEV-only orderingPreview emulates a Branch using fake
IndexedDB and injected transport; its simplified monetary response values are
fixtures, not production pricing logic. No hardware tests are performed.

Real authentication/session bootstrap, Branch end-to-end deployment, complete
floor plan, categories/display codes, an authoritative unsent basket quote,
operational status host data, and production
device trials remain outside this slice. Missing metadata must be added through
explicit contracts rather than guessed from names or IDs. Pending requests must
not expire as drafts or acquire replacement IDs.
Draft option editing is addressed by architecture note 0181; it does not alter
the accepted-order, financial, metadata or deployment limits above.

## Merge gate

This slice exposes authenticated order mutations through an explicit host opt-in.
Obtain explicit D21.3 user approval before Antigravity merges it, even if checks
pass. No physical printer/drawer action is authorized by this implementation.
