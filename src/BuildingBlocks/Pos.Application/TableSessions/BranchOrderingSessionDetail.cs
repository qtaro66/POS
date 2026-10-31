namespace Pos.Application.TableSessions;

public sealed record BranchOrderingSessionDetail(
    BranchActiveTableSession Session, DateTimeOffset AsOf, bool HasOpenCancellationRequest,
    BranchOrderingBillTotals? BillPreviewTotals, IReadOnlyList<BranchOrderingBatch> Batches);
public sealed record BranchOrderingBillTotals(
    string MasterDataVersion, string CalculationPolicyVersion, decimal VatRate, decimal ServiceChargeRate,
    decimal ItemGrossAmount, decimal LineDiscountAmount, decimal ItemNetAmount, decimal BillDiscountAmount,
    decimal ServiceChargeBase, decimal ServiceChargeAmount, decimal TotalBeforeRounding,
    decimal RoundingAdjustment, decimal NetTotal, decimal VatInclusiveAmount, decimal AmountBeforeVat);
public sealed record BranchOrderingBatch(
    string OrderBatchId, string RequestId, long? BranchSequence, DateTimeOffset ServerReceivedAt,
    IReadOnlyList<BranchOrderingLine> Lines, decimal ActiveGrossAmount, decimal ActiveLineDiscountAmount, decimal ActiveItemNetAmount);
public sealed record BranchOrderingLine(
    string OrderLineId, int LineSequence, string SellableItemId, string DisplayName,
    int OriginalQuantity, int ActiveQuantity, string Status, decimal UnitPrice, string FulfillmentType, string? Comment,
    IReadOnlyList<BranchOrderingOption> Options, decimal ActiveGrossAmount, decimal ActiveDiscountAmount, decimal ActiveNetAmount);
public sealed record BranchOrderingOption(string SellableItemId, string? OptionGroupId, string DisplayName, int QuantityPerItem, decimal UnitPrice);
