using Pos.Domain.Billing;

namespace Pos.Domain.Tests.Billing;

public sealed class BillCalculatorTests
{
    [Fact]
    public void Calculates_inclusive_vat_discounts_service_charge_and_global_floor()
    {
        var policy = new BillPolicySnapshot(
            "MASTER-7",
            "CALC-3",
            vatRate: 7m,
            serviceChargeRate: 10m,
            currencyScale: 2,
            MonetaryRoundingMode.AwayFromZero,
            new DiscountSnapshot(DiscountKind.FixedAmount, 20m));
        var lines = new[]
        {
            new OrderLineSnapshot(
                "LINE-1",
                "ITEM-1",
                "ข้าวผัดกุ้ง",
                2,
                100m,
                "DineIn",
                "ไม่ใส่ต้นหอม",
                [new OrderLineOptionSnapshot("OPT-1", "ไข่ดาว", 1, 15m)],
                new DiscountSnapshot(DiscountKind.Percentage, 10m))
        };

        var result = BillCalculator.Calculate(policy, lines);

        Assert.Equal(230m, result.ItemGrossAmount);
        Assert.Equal(23m, result.LineDiscountAmount);
        Assert.Equal(207m, result.ItemNetAmount);
        Assert.Equal(new DiscountCalculation(20m, 20m), result.BillDiscount);
        Assert.Equal(187m, result.ServiceChargeBase);
        Assert.Equal(18.70m, result.ServiceChargeAmount);
        Assert.Equal(205.70m, result.TotalBeforeRounding);
        Assert.Equal(-0.70m, result.RoundingAdjustment);
        Assert.Equal(205m, result.AmountDue);
        Assert.Equal(13.46m, result.VatInclusiveAmount);
        Assert.Equal(192.24m, result.AmountBeforeVat);
    }

    [Fact]
    public void Fixed_discounts_are_capped_without_losing_requested_amount()
    {
        var result = BillCalculator.Calculate(
            Policy(billDiscount: new DiscountSnapshot(DiscountKind.FixedAmount, 500m)),
            [Line(discount: new DiscountSnapshot(DiscountKind.FixedAmount, 150m))]);

        Assert.Equal(new DiscountCalculation(150m, 100m), result.Lines[0].Discount);
        Assert.Equal(new DiscountCalculation(500m, 0m), result.BillDiscount);
        Assert.Equal(0m, result.AmountDue);
    }

    [Fact]
    public void Percentage_discount_outside_zero_to_one_hundred_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DiscountSnapshot(DiscountKind.Percentage, 100.01m));
    }

    [Fact]
    public void Empty_bill_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BillCalculator.Calculate(Policy(), []));
    }

    private static BillPolicySnapshot Policy(DiscountSnapshot? billDiscount = null) =>
        new("MASTER-1", "CALC-1", 7m, 0m, 2, MonetaryRoundingMode.AwayFromZero, billDiscount);

    private static OrderLineSnapshot Line(DiscountSnapshot? discount = null) =>
        new("LINE-1", "ITEM-1", "Item", 1, 100m, "DineIn", null, [], discount);
}
