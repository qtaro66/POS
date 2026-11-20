# Closing financial details from bill snapshots

Cash summary totals now optionally include item gross amount, line and bill
discounts, service charges, the total before rounding, rounding adjustment,
amount due, VAT and the amount before VAT. Values come from the bill snapshot
stored with each completed cash sale, using the existing billing calculation.

Each scope is complete only when every included sale has compatible captured
bill fields. Incomplete or inconsistent older bills retain their applied cash
and receipt count but have unavailable financial detail. Missing billDiscount
means no bill-level discount under the existing snapshot serializer.

Closing captures these financial totals with the existing immutable cash
snapshot in the same transaction. Older closing snapshots are read as saved,
with unavailable financial detail where that field was absent. Live totals can
change while previously captured closing totals stay fixed.

The workspace offers expandable bill detail for current totals and saved
closings. Rounding is a separate value from discounts. The source covers cash
sales only. Non-cash methods, cancellation statistics and Central lifecycle
report delivery still need integration.

Regression coverage includes a real 100.90 bill settled for 100.00, preserved
VAT after policy changes, optional historical fields, incomplete data, replay,
later shifts and the Edge store lifecycle sequence.
