# Repository instructions

- Base decisions on facts, tests, and the versioned requirements in `docs/requirements`.
- Do not change confirmed business rules silently. Record conflicts and obtain a decision before changing behavior.
- Keep domain and application code independent of Windows, Linux, database, browser, and printer implementations.
- The Branch API is the only process allowed to write the branch SQLite database.
- Browser and mobile clients must not connect directly to SQLite or physical printers.
- Safe automated tests must not contact hardware. Any physical printer or cash-drawer action requires explicit operator confirmation at execution time.
- After sending begins, an uncertain print result is `Unknown` and must not be retried automatically.
- Never commit secrets, private keys, real device identifiers, customer data, production databases, or local hardware evidence.

