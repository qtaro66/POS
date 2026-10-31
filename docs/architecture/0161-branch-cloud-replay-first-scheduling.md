# Branch Cloud replay-first scheduling

Default enabled delivery now supplies the existing serial polling service with
a replay-first scheduler. Each cycle attempts one eligible replay; a processed
replay ends the cycle. Only `NoWork` permits one normal outbox cycle. Both idle
results preserve the existing idle delay. Replay errors propagate to the
poller's retry delay without normal fallthrough.

`NoWork` includes replay work waiting for a lease or retry time; normal delivery
may proceed during that wait. Central still enforces sequence order and rejects
gaps, so no later event is treated as accepted without durable acknowledgment.
Eligible recovery work takes priority and can temporarily defer normal work.

The default scheduler shares Branch, owner, lease duration, retry delay, and
cancellation across processors. Custom normal-cycle registrations remain
authoritative and opt out of this default scheduling. No separate hosted worker
or parallel delivery loop is created. Tests use fakes and no hardware/network.
