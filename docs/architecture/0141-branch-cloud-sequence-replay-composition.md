# Branch Cloud sequence-replay composition

When Branch Cloud outbox delivery is enabled, the Branch API composes
`IBranchCloudOutboxSequenceReplayStore` as a singleton backed by
`SqliteBranchCloudOutboxSequenceReplayStore`. It uses the same validated Branch
database path as the existing claim, lease-renewal, delivery-failure, and
confirmation stores.

Disabled delivery does not register or open the replay store. An application
supplied replay-store registration is preserved, matching the replaceable
composition boundary used by the other Cloud outbox stores.

This slice makes the replay store available to the delivery runtime but does
not yet invoke it after a sequence-gap response. Runtime activation and replay
claiming remain separate follow-up slices. Composition tests use temporary
SQLite databases and do not contact a network endpoint or Branch hardware.
