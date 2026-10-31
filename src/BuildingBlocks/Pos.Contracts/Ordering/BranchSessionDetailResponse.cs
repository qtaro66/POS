namespace Pos.Contracts.Ordering;

public sealed record BranchSessionDetailResponse(
    int SchemaVersion, BranchTableSessionView Session, DateTimeOffset AsOf, bool HasOpenCancellationRequest,
    BranchBillPreviewTotals? BillPreviewTotals, IReadOnlyList<BranchAcceptedBatchView> Batches);
public sealed record BranchBillPreviewTotals(
    string MasterDataVersion, string CalculationPolicyVersion, decimal VatRate, decimal ServiceChargeRate,
    decimal ItemGrossAmount, decimal LineDiscountAmount, decimal ItemNetAmount, decimal BillDiscountAmount,
    decimal ServiceChargeBase, decimal ServiceChargeAmount, decimal TotalBeforeRounding,
    decimal RoundingAdjustment, decimal NetTotal, decimal VatInclusiveAmount, decimal AmountBeforeVat);
public sealed record BranchAcceptedBatchView(
    string OrderBatchId, string RequestId, long? BranchSequence, DateTimeOffset ServerReceivedAt, string AcceptanceStatus,
    IReadOnlyList<BranchAcceptedLineView> Lines, decimal ActiveGrossAmount, decimal ActiveLineDiscountAmount, decimal ActiveItemNetAmount);
public sealed record BranchAcceptedLineView(
    string OrderLineId, int LineSequence, string SellableItemId, string DisplayName,
    int OriginalQuantity, int ActiveQuantity, string Status, decimal UnitPrice, string FulfillmentType, string? Comment,
    IReadOnlyList<BranchAcceptedOptionView> Options, decimal ActiveGrossAmount, decimal ActiveDiscountAmount, decimal ActiveNetAmount);
public sealed record BranchAcceptedOptionView(string SellableItemId, string? OptionGroupId, string DisplayName, int QuantityPerItem, decimal UnitPrice);
