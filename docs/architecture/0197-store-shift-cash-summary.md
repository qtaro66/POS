# Store and shift cash summaries

The store workspace reads cash settlement totals from committed cash_sales
records scoped to the authenticated Branch and store session. The current
shift summary includes its own receipts and applied cash. Store totals include
all shifts within that store session. Cash tendered and change are excluded
from the applied-sales total.

Closing captures the available cash summary in the same transaction as the
lifecycle command and its closing result. A saved shift summary covers the
closing shift. A store-close summary covers every shift in the store session.
Expected cash retains the D04.11 cumulative opening-float rule.

Later settlements and refreshed live totals must not change a captured summary.
Replaying a completed command returns the original outcome. A closing accepted
before summary capture was introduced has an unavailable summary; history
reads do not recalculate its totals.

These summaries describe the existing cash settlement data source. Full
D04.11.7 reports additionally require committed non-cash payment methods,
discounts, charges, rounding, VAT and cancellation details. Production payment
integration and Central lifecycle report delivery remain separate dependencies.

Regression evidence includes applied-versus-tendered amounts, receipt counts,
scope isolation, multiple shifts, atomic close/replay, later sales, restart
and upgrade of a database containing an older closing.
