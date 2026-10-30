# Branch Cloud replay lease heartbeat

The replay heartbeat renews exact persisted ownership every third of the lease
duration using the dedicated renewal store and a UTC time provider. It exposes
a linked delivery cancellation token and returns the latest lease token only
after the renewal loop has stopped.

Renewal failure cancels delivery and is surfaced on stop/disposal, preventing
completion or failure persistence with uncertain ownership. Caller cancellation
stops the loop; normal stop leaves the delivery token uncancelled. Consumers
must propagate caller cancellation and use the stopped loop's latest token for
subsequent persistence operations.

This component is not registered or started by the hosted worker yet. Replay
cycle implementation and runtime activation remain follow-up work. Tests use
controlled delay, clock, and renewal fakes without network or hardware access.
