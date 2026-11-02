# PC POS persisted draft customization

## Scope

After verified MERGED PR #287, finish the existing draft edit workflow required
by D18.6 and D08.4. Integrated basket rows can reopen SubItem/Topping selection
with committed options prefilled. SubItems remain in their own column and the
following popover contains Topping only. Explicit confirmation replaces that
same row rather than appending another. The root item, quantity, comment and
DineIn/Takeaway value are retained. Choosing a different root requires cancelling
the edit and adding a new row through the existing flow.

Standalone BasketPanel keeps its prior behavior unless an edit callback is
supplied. The integrated workspace owns the edit request, keyed by editor identity
and a unique request sequence, so cancellation/reopening cannot resurrect prior
speculative choices. Cancel, Escape or leaving the workspace discards only the
unconfirmed option selection. It does not undo other committed basket edits.

## Revision and lifecycle boundaries

An edit captures basketId, revision, row index, sessionMasterDataVersion and the
original line. A changed revision, shifted index or refreshed menu invalidates
the editor. Confirmation is disabled with an explicit reopen message. There is
no automatic reset/rebase of unsaved options onto newer state.

createBasketEditor.replaceLine accepts an optional expected basket-version token.
It rejects stale/different tokens before persisting. The existing save operation
then uses IndexedDB compare-and-swap to protect against an external tab writing
between the in-memory check and durable save. A failed save retains the committed
basket and existing refresh-required error. No business event, schema or database
migration is added.

While customization is open, new SubmitOrder is disabled to avoid sending the
old options accidentally. Other draft edits may commit, but invalidate the open
option snapshot. Pending commands remain frozen; accepted rows stay read-only.
Changing clients cannot inherit the old edit request; screen lock and navigation
retain existing persistence and recovery rules.

## Availability and validation

All existing group minimum/maximum, repeated-count, per-option and advisory
availability checks apply. A currently unavailable selected option may be
explicitly deselected/reduced, never increased or newly selected. Required groups
must still have an available replacement before confirmation. No option is
silently dropped. If an option ID is entirely missing from the frozen menu,
validation fails closed; cancellation/removal and re-entry remain available
rather than guessing replacement IDs. Branch preflight remains authoritative.

## Verification and limits

Controller tests cover stale and different basket tokens. Integrated UI tests
cover prefill, in-place replacement and submitted bytes, field preservation,
cancel/focus, remount persistence, in-memory revision changes, row-index shifts,
external-tab CAS failure, menu refresh, unavailable required replacement,
pending freeze and employee isolation. Synthetic desktop checks exercise the
whole edit/confirm/cancel/submit path at 1280x720 and 1920x1080 without hardware.

This is the same explicit authenticated-host opt-in as note 0180. Production
authentication/session bootstrap, category/display-code metadata, unsent-basket
quotes, complete floor plan, payment/printing commands, physical devices and
real deployment remain outside scope. No financial rules, Branch API contracts,
dependencies, CI or production data are changed. Because it adds authenticated
order-editing behavior, explicit D21.3 approval is required before merge.
