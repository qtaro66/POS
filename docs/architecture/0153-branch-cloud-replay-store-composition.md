# Branch Cloud replay store composition

Enabled Branch Cloud delivery now registers dedicated replay claim, renewal,
completion, and failure stores as replaceable singletons, alongside the existing
replay-request store. Every factory uses the same validated Branch database
path as normal outbox persistence. Disabled delivery registers none of them.

Existing custom registrations remain authoritative through `TryAddSingleton`.
Composition tests verify concrete singleton types, disabled behavior, and custom
override preservation without network or hardware access.

This slice makes persistence available to the runtime but does not execute
replay delivery cycles. Replay database readiness and cycle/heartbeat integration
remain follow-up work; no new worker is started by these registrations.
