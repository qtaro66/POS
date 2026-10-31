# PC POS ordering client foundation

Add a transport adapter for the opt-in Branch ordering API merged in PR #274.
Snapshot a whole basket and command identity before sending; retain the same
request ID and serialized body for explicit retries. Correlate durable replies
by schema, request ID, receipt state, and (for SubmitOrder) table session. Open
may return an existing session different from the proposed ID, as D03 requires.

Separate durable acceptance/rejection, transport denial and uncertain outcomes.
No mutation retries automatically. `NoDurableResult` does not reject or complete
an order. Menus retain server-frozen prices, current availability and option
rules, with no client total calculation. Active lists retain their incomplete
floor-plan flag.

The client uses memory-only credentials, HTTPS, no cookies/cache/redirects,
bounded JSON, cancellation and request timeout. Prepared commands cannot cross
client sessions. A workspace test script joins the existing root verification
gate using Node's built-in runner without adding dependencies or weakening CI.

This is an unwired adapter, not a cashier UI, login flow, durable browser outbox,
generated OpenAPI client, or real-network integration. D21.3 security approval
is required before merge because it introduces credential-bearing HTTP calls.
