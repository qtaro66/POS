# Branch Cloud sequence-replay boundary

Branch now has a transport- and database-independent request contract for
replaying a retained event that Central reports as missing. A request contains
an idempotent recovery ID, normalized Branch ID, expected sequence, greater
received sequence, and UTC request time.

The replay store boundary must mark the retained expected event for another
delivery attempt without deleting or replacing its original durable
acknowledgment evidence. An exact recovery retry returns `AlreadyRequested`, a
missing retained event returns `ExpectedSequenceNotFound`, and reuse of the same
recovery ID for different data must fail closed.

This slice defines the application boundary only. SQLite schema and store
implementation, claim eligibility, replay confirmation, runtime composition,
and cycle activation remain separate follow-up slices. Automated tests contact
no database, network, or Branch hardware.
