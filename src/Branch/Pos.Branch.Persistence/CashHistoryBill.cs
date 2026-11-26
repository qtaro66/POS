namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record CashHistoryBill(int SchemaVersion, string BranchId, string TableSessionId, string TableId,
    string PaymentRequestId, DateTimeOffset CapturedAt, string MasterDataVersion, string CalculationPolicyVersion,
    decimal VatRate, decimal ServiceChargeRate, IReadOnlyList<CashHistoryBillLine> Items, CashHistoryDiscount? BillDiscount);
public sealed record CashHistoryBillLine(string OrderLineId, string OrderBatchId, string SellableItemId, string DisplayName,
    int Quantity, decimal UnitPrice, string FulfillmentType, string? Comment, IReadOnlyList<CashHistoryBillOption> Options,
    decimal GrossAmount, CashHistoryDiscount? Discount, decimal NetAmount);
public sealed record CashHistoryBillOption(string OptionId, string DisplayName, int QuantityPerItem, decimal UnitPrice);
public sealed record CashHistoryDiscount(string Kind, decimal RequestedValue, decimal RequestedAmount, decimal AppliedAmount);
