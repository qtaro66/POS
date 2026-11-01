# PC POS basket submission coordinator

Compose PR #280's transactional baskets with the existing authenticated ordering
client and recovery adapter before activating visible mutation controls. Create
both stores from the same IndexedDB factory and derive owner scope from captured
client identity, with no separately supplied actor to drift from authentication.
Expose only nonsecret employee metadata from the ordering client; credentials
remain private, memory-only closures.

Fresh submission loads the expected basket generation/revision, checks current
Ordering state and frozen session menu version, and performs advisory availability
and option-group validation. Mirror CatalogSelectionValidator's membership,
duplicate selection, repeated quantity and summed group cardinality rules.
Accept zero prices, do not reprice, and do not consult stock quantities. Enforce
the existing v1 signed 32-bit quantity wire fields. Branch still decides against
its own current state and catalog when it commits the whole batch.

Only after successful checks, prepare one immutable command and atomically stage
it with the basket. Query Branch before POST. A bound basket returns the original
request ID on a fresh submit action; it cannot create a replacement command.
Same-instance busy guards suppress double-clicks; transactional compare-and-swap
protects staging across instances/tabs and edits during preflight.

GET-only recovery queries before session/menu gating so an accepted command can
be applied even if the table has since entered PaymentPending. Explicit retry
queries first, then checks current session/menu if still unresolved, then performs
the recovery adapter's final lookup before any same-ID/body POST. Previously
resolved commands never resend after a later NoDurableResult. This is explicit
operator-driven retry, with no background resubmission or new-identity replay.

Durable accepted/rejected outcomes are applied through the atomic basket store.
Preserve the real Branch result when marker persistence or local cleanup fails,
returning localApplied=false and retaining recovery evidence. HTTP authentication
or request-validation failures remain pending transport outcomes, not durable
business rejections. Cancellation after staging cannot discard the original ID.
Local preflight blocks are not Branch rejections and leave records intact.

Tests use isolated fake-indexeddb and injected fetch, covering staged-before-POST,
acceptance, rejection, lost response/reload, locked sessions, catalog changes,
option constraints, concurrent coordinators, cancellation and transaction faults.
No real service, branch database, printer or cash drawer is contacted.

No UI mutation control or runtime activation is added. Session provisioning,
visible basket editing/recovery presentation, sent-order refresh wiring and real
Branch/TLS integration remain future work. No backend API/schema, confirmed
business policy or dependency version changes. This client preflight is neither
authorization nor a guarantee against state changes after its reads. D21.3
explicit approval is required before merge for identity-scoped command execution.
