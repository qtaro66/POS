namespace Pos.Domain.Payments;

public sealed record CashSettlement
{
    public decimal TenderedAmount { get; }
    public decimal AppliedAmount { get; }
    public decimal ChangeAmount { get; }

    public CashSettlement(decimal amountDue, decimal tenderedAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amountDue);
        ArgumentOutOfRangeException.ThrowIfLessThan(tenderedAmount, amountDue);

        TenderedAmount = tenderedAmount;
        AppliedAmount = amountDue;
        ChangeAmount = tenderedAmount - amountDue;
    }
}
