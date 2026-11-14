using Pos.Domain.Payments;

namespace Pos.Domain.Tests;

public sealed class CashSettlementTests
{
    [Fact]
    public void Excess_cash_is_change_and_does_not_increase_applied_amount()
    {
        var settlement = new CashSettlement(150m, 200m);

        Assert.Equal(200m, settlement.TenderedAmount);
        Assert.Equal(150m, settlement.AppliedAmount);
        Assert.Equal(50m, settlement.ChangeAmount);
    }

    [Fact]
    public void Cash_below_amount_due_cannot_complete_a_cash_sale()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CashSettlement(150m, 149.99m));
    }
}
