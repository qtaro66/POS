# PC POS store and shift workspace

The normal store-and-shift navigation connects to the authenticated lifecycle
API. GET /api/v1/ordering/store-lifecycle/current discovers the latest store
for the authenticated Branch, including a closed store. The first visit with
no store returns 404 and offers store opening.

The workspace presents the current Business Date and store/shift state.
Opening a store creates the first shift. Closing a mid-day shift leaves the
store open and allows the next shift. Store closing explicitly ends the session.
The dialog requests a PIN for each operation and a required cash value for
store opening and closing. A blank mid-day count remains null.

While a command result is uncertain, retain its business request identifier and
input so that a retry can recover the original result. PINs are entered for each
attempt and excluded from persistent recovery data. Refresh reads Branch
state. Concurrent operations in the workspace must be blocked until the
pending operation has a known result.

This workspace establishes lifecycle operations. Full D18.9 payment-channel
summaries, elapsed shift time, shift timeline and report navigation require
additional read models. Production settlement linkage and Central lifecycle
events remain integration tasks.
