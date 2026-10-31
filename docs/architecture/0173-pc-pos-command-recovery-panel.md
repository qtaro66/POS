# PC POS command recovery inspection panel

Consume the durable command journal from PR #278 through an optional authenticated
PC host integration. App constructs the recovery adapter from the exact same
client used for the sent-order workspace; typed props require the full ordering
client when supplying a journal. Existing read-only callers and the blocked
default production entrypoint keep their current behavior.

The drawer lists original command IDs, session IDs, employee IDs, UTC creation
times, item/option codes, quantities, fulfillment and comments. Stored payloads
are structurally/canonically validated before rendering. Snapshots do not carry
authoritative menu names or prices; do not invent them from a current catalog.
Local resolved markers are historical evidence requiring a fresh Branch lookup.

The presentation controller has only list/recover capabilities. An operator can
explicitly query a listed request; neither mounting nor listing queries Branch
status automatically. Distinguish durable accepted/rejected results from
NoDurableResult and unavailable lookup, with no fabricated business outcome.
Display the Branch receipt timestamp and local check timestamp separately.
Failure to save a resolved marker cannot change the durable Branch result.

There is deliberately no resend, replacement-ID, deletion or finalize control
until persistent basket coordination is implemented. GET recovery can persist
a local resolved marker through the existing adapter; this is not a new order
or a write to Branch business data. An empty journal is not evidence of no
Branch transactions. Acceptance does not confirm any physical print operation.

Refresh clears previous entries/results before awaiting reads. Per-request abort
and epoch checks ignore late responses, including on close, screen lock and
identity changes. StrictMode remounts can refresh safely. The nonmodal scrollable
drawer has a sticky header/close action, heading focus, Escape handling and
trigger focus restoration. Other workspace controls remain available.

Controller and React tests cover state transitions, malformed/colliding snapshots,
lost/late results, storage failures, original details, read-only capabilities,
identity replacement, screen lock and focus/cancellation. A DEV-only preview
uses synthetic in-memory ports with no external fetch, persistent writes or
mutations. Production assets must exclude the preview fixture module.

Production session provisioning, persistent basket reconciliation, submission
controls, live updates, real Branch integration and hardware tests remain pending.
No backend API/schema, permission or business rule is changed. D21.3 explicit
approval is required before merge for the identity-scoped recovery UI integration.
