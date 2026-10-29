# Branch Cloud outbox retry-delay policy

Cloud delivery failures need a deterministic delay derived from the retry count
already persisted with each outbox event. The application layer now exposes a
retry-delay policy boundary and an exponential implementation.

The first failure uses the configured initial delay. Each previously persisted
failure doubles the next delay until the configured maximum is reached. The
calculation validates its inputs, caps before multiplication can overflow, and
remains deterministic so persistence and tests can reproduce the same result.

This slice defines and tests the policy only. Runtime composition, operational
defaults, and replacing the current fixed delay remain separate follow-up work.
