namespace Pos.Domain.Billing;

public enum DiscountKind
{
    Percentage,
    FixedAmount
}

public enum MonetaryRoundingMode
{
    AwayFromZero,
    ToEven,
    ToZero,
    ToNegativeInfinity,
    ToPositiveInfinity
}

public sealed record DiscountSnapshot
{
    public DiscountSnapshot(DiscountKind kind, decimal requestedValue)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (requestedValue < 0 || (kind == DiscountKind.Percentage && requestedValue > 100))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedValue));
        }

        Kind = kind;
        RequestedValue = requestedValue;
    }

    public DiscountKind Kind { get; }

    public decimal RequestedValue { get; }
}

public sealed record BillPolicySnapshot
{
    public BillPolicySnapshot(
        string masterDataVersion,
        string calculationPolicyVersion,
        decimal vatRate,
        decimal serviceChargeRate,
        int currencyScale,
        MonetaryRoundingMode roundingMode,
        DiscountSnapshot? billDiscount = null)
    {
        MasterDataVersion = Require(masterDataVersion, nameof(masterDataVersion));
        CalculationPolicyVersion = Require(calculationPolicyVersion, nameof(calculationPolicyVersion));
        VatRate = NonNegative(vatRate, nameof(vatRate));
        ServiceChargeRate = NonNegative(serviceChargeRate, nameof(serviceChargeRate));
        if (currencyScale is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(nameof(currencyScale));
        }

        if (!Enum.IsDefined(roundingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(roundingMode));
        }

        CurrencyScale = currencyScale;
        RoundingMode = roundingMode;
        BillDiscount = billDiscount;
    }

    public string MasterDataVersion { get; }

    public string CalculationPolicyVersion { get; }

    public decimal VatRate { get; }

    public decimal ServiceChargeRate { get; }

    public int CurrencyScale { get; }

    public MonetaryRoundingMode RoundingMode { get; }

    public DiscountSnapshot? BillDiscount { get; }

    private static decimal NonNegative(decimal value, string name) =>
        value < 0 ? throw new ArgumentOutOfRangeException(name) : value;

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("The value cannot be empty.", name) : value.Trim();
}

public sealed record OrderLineOptionSnapshot
{
    public OrderLineOptionSnapshot(string optionId, string displayName, int quantityPerItem, decimal unitPrice)
    {
        OptionId = Require(optionId, nameof(optionId));
        DisplayName = Require(displayName, nameof(displayName));
        if (quantityPerItem < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityPerItem));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        QuantityPerItem = quantityPerItem;
        UnitPrice = unitPrice;
    }

    public string OptionId { get; }

    public string DisplayName { get; }

    public int QuantityPerItem { get; }

    public decimal UnitPrice { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("The value cannot be empty.", name) : value.Trim();
}

public sealed record OrderLineSnapshot
{
    public OrderLineSnapshot(
        string orderLineId,
        string sellableItemId,
        string displayName,
        int quantity,
        decimal unitPrice,
        string fulfillmentType,
        string? comment,
        IReadOnlyList<OrderLineOptionSnapshot> options,
        DiscountSnapshot? discount = null)
    {
        OrderLineId = Require(orderLineId, nameof(orderLineId));
        SellableItemId = Require(sellableItemId, nameof(sellableItemId));
        DisplayName = Require(displayName, nameof(displayName));
        FulfillmentType = Require(fulfillmentType, nameof(fulfillmentType));
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        }

        Quantity = quantity;
        UnitPrice = unitPrice;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Options = options?.ToArray() ?? throw new ArgumentNullException(nameof(options));
        Discount = discount;
    }

    public string OrderLineId { get; }

    public string SellableItemId { get; }

    public string DisplayName { get; }

    public int Quantity { get; }

    public decimal UnitPrice { get; }

    public string FulfillmentType { get; }

    public string? Comment { get; }

    public IReadOnlyList<OrderLineOptionSnapshot> Options { get; }

    public DiscountSnapshot? Discount { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("The value cannot be empty.", name) : value.Trim();
}

public sealed record DiscountCalculation(decimal RequestedAmount, decimal AppliedAmount);

public sealed record OrderLineCalculation(
    OrderLineSnapshot Snapshot,
    decimal GrossAmount,
    DiscountCalculation Discount,
    decimal NetAmount);

public sealed record BillCalculation(
    BillPolicySnapshot Policy,
    IReadOnlyList<OrderLineCalculation> Lines,
    decimal ItemGrossAmount,
    decimal LineDiscountAmount,
    decimal ItemNetAmount,
    DiscountCalculation BillDiscount,
    decimal ServiceChargeBase,
    decimal ServiceChargeAmount,
    decimal TotalBeforeRounding,
    decimal RoundingAdjustment,
    decimal AmountDue,
    decimal VatInclusiveAmount,
    decimal AmountBeforeVat);

public static class BillCalculator
{
    public static BillCalculation Calculate(
        BillPolicySnapshot policy,
        IReadOnlyList<OrderLineSnapshot> lines)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("A bill must contain at least one active order line.", nameof(lines));
        }

        var calculatedLines = lines.Select(line => CalculateLine(policy, line)).ToArray();
        var itemGross = Money(policy, calculatedLines.Sum(line => line.GrossAmount));
        var lineDiscount = Money(policy, calculatedLines.Sum(line => line.Discount.AppliedAmount));
        var itemNet = Money(policy, calculatedLines.Sum(line => line.NetAmount));
        var billDiscount = CalculateDiscount(policy, policy.BillDiscount, itemNet);
        var serviceBase = Money(policy, Math.Max(0, itemNet - billDiscount.AppliedAmount));
        var serviceCharge = Money(policy, serviceBase * policy.ServiceChargeRate / 100m);
        var totalBeforeRounding = Money(policy, serviceBase + serviceCharge);
        var amountDue = decimal.Floor(totalBeforeRounding);
        var roundingAdjustment = amountDue - totalBeforeRounding;
        var vat = policy.VatRate == 0
            ? 0
            : Money(policy, totalBeforeRounding * policy.VatRate / (100m + policy.VatRate));

        return new BillCalculation(
            policy,
            calculatedLines,
            itemGross,
            lineDiscount,
            itemNet,
            billDiscount,
            serviceBase,
            serviceCharge,
            totalBeforeRounding,
            roundingAdjustment,
            amountDue,
            vat,
            Money(policy, totalBeforeRounding - vat));
    }

    private static OrderLineCalculation CalculateLine(BillPolicySnapshot policy, OrderLineSnapshot line)
    {
        var optionUnitTotal = line.Options.Sum(option => option.UnitPrice * option.QuantityPerItem);
        var gross = Money(policy, (line.UnitPrice + optionUnitTotal) * line.Quantity);
        var discount = CalculateDiscount(policy, line.Discount, gross);
        return new OrderLineCalculation(line, gross, discount, Money(policy, gross - discount.AppliedAmount));
    }

    private static DiscountCalculation CalculateDiscount(
        BillPolicySnapshot policy,
        DiscountSnapshot? discount,
        decimal eligibleBase)
    {
        if (discount is null)
        {
            return new DiscountCalculation(0, 0);
        }

        var requested = discount.Kind == DiscountKind.Percentage
            ? Money(policy, eligibleBase * discount.RequestedValue / 100m)
            : Money(policy, discount.RequestedValue);
        return new DiscountCalculation(requested, Math.Min(requested, eligibleBase));
    }

    private static decimal Money(BillPolicySnapshot policy, decimal value) =>
        decimal.Round(
            value,
            policy.CurrencyScale,
            policy.RoundingMode switch
            {
                MonetaryRoundingMode.AwayFromZero => MidpointRounding.AwayFromZero,
                MonetaryRoundingMode.ToEven => MidpointRounding.ToEven,
                MonetaryRoundingMode.ToZero => MidpointRounding.ToZero,
                MonetaryRoundingMode.ToNegativeInfinity => MidpointRounding.ToNegativeInfinity,
                MonetaryRoundingMode.ToPositiveInfinity => MidpointRounding.ToPositiveInfinity,
                _ => throw new ArgumentOutOfRangeException(nameof(policy))
            });
}
