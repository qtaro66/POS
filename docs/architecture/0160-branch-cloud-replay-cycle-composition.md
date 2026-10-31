# Branch Cloud replay cycle composition

Enabled Cloud delivery registers a replaceable singleton replay processor. It
resolves dedicated replay stores, the existing Cloud delivery client, retry
policy, validated maximum retry delay, and time provider. Disabled delivery
registers no replay processor. A custom processor registration is preserved.

The default normal-delivery composition supplies the authenticated client used
by both processors. Hosts replacing the normal processor must supply a delivery
client before explicitly resolving the default replay processor, or replace the
replay processor as well. Registration alone does not resolve or execute it.

The existing polling service still executes normal outbox cycles only. Replay
scheduling and priority remain follow-up work. Composition tests resolve default
and authenticated-client graphs and preserve overrides without Cloud/hardware
access.
