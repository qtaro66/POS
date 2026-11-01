# PC POS persistent basket editor controller

Connect basket persistence and PR #281's submission coordinator through a
subscribable controller for the upcoming table editor. Bind each instance to one
authenticated client and table session; derive captured owner scope internally.
No I/O occurs on construction. Explicit refresh restores local data and reads
Branch detail/menu; ignore stale responses using epochs and abort signals.

Persist edits before publishing a changed basket. Do not optimistically show
unsaved lines. Serialize actions within the controller, then rely on the
store's generation/revision compare-and-swap for other tabs. A write conflict
retains the last committed view and requires refresh before another edit.
Basic line normalization and the existing advisory selection checks protect
new/replaced lines, without inventing a pricing or stock calculation.

Start new-line fulfillment at DineIn; changing it to Takeaway affects subsequent
additions only. Explicit line replacement may change one unsubmitted line's
fulfillment, quantity, options or comment. Removal does not require other old
selections to remain sellable, so several unavailable old lines can be removed
one by one. Opening an empty editor does not persist an empty draft automatically.

Session locks, unavailable Branch reads, frozen catalog-version mismatches and
pending request bindings disable edits. Keep existing local lines for review;
do not silently reprice, drop, reassign or copy them to a new session. This
controller does not implement offline menu editing or employee takeover.

Submission/recovery/retry delegate to the established coordinator. Keep real
durable outcomes separate from transport uncertainty and local failures. After
processing a command, require fresh Branch reads before more edits. A restored
pending request remains frozen; GET recovery remains usable even when current
session/menu checks would prevent new submissions. The future UI must present
the outcome and refresh authoritative sent-order views after acceptance.

Cancellation invalidates obsolete UI updates and aborts network reads, without
deleting local drafts or staged commands. Refresh can safely restart, including
StrictMode effect remounts. Replacing client/session requires a fresh controller
so previous identity data cannot flash in a new workspace.

Tests use isolated IndexedDB and injected HTTP. Cover committed editing, reload,
mixed fulfillment, unavailable selections, locks/version checks, stale-tab
conflicts, superseded replies, identity replacement, submission, same-ID retry
and cancellation during an uncertain send. No hardware or real Branch access.
No editor component, production session provisioning or runtime activation is
added; backend contracts, schemas, pricing, stock and permission rules remain
unchanged. D21.3 explicit approval is required before merge for identity-scoped
persistent editor and command lifecycle integration.
