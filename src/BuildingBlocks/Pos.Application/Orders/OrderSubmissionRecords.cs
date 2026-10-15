using Pos.Application.MasterData;
using Pos.Domain.TableSessions;

namespace Pos.Application.Orders;

public enum FulfillmentType
{
    DineIn,
    Takeaway
}

public sealed record SubmitOrderLine
{
    public SubmitOrderLine(
        string sellableItemId,
        int quantity,
        FulfillmentType fulfillmentType,
        string? comment = null,
        IReadOnlyList<CatalogOptionSelection>? options = null)
    {
        SellableItemId = Require(sellableItemId, nameof(sellableItemId));
        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (!Enum.IsDefined(fulfillmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(fulfillmentType));
        }

        Quantity = quantity;
        FulfillmentType = fulfillmentType;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Options = options?.ToArray() ?? [];
        if (Options.Any(option => option is null))
        {
            throw new ArgumentException("Option selections cannot contain null values.", nameof(options));
        }
    }

    public string SellableItemId { get; }

    public int Quantity { get; }

    public FulfillmentType FulfillmentType { get; }

    public string? Comment { get; }

    public IReadOnlyList<CatalogOptionSelection> Options { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}

public sealed record SubmitOrderCommand
{
    public SubmitOrderCommand(
        TableSessions.BranchCommandMetadata metadata,
        string orderBatchId,
        IReadOnlyList<SubmitOrderLine> lines)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        OrderBatchId = string.IsNullOrWhiteSpace(orderBatchId)
            ? throw new ArgumentException("The value cannot be empty.", nameof(orderBatchId))
            : orderBatchId.Trim();
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(line => line is null))
        {
            throw new ArgumentException(
                "SubmitOrder requires at least one non-null line.",
                nameof(lines));
        }

        Lines = lines.ToArray();
    }

    public TableSessions.BranchCommandMetadata Metadata { get; }

    public string OrderBatchId { get; }

    public IReadOnlyList<SubmitOrderLine> Lines { get; }
}

public sealed record SubmitOrderValidationError(
    int LineSequence,
    CatalogValidationErrorCode Code,
    string? SellableItemId,
    string? OptionGroupId);

public sealed record OrderBatchSnapshot(
    string BranchId,
    string OrderBatchId,
    string RequestId,
    TableSessionId TableSessionId,
    string DeviceId,
    string EmployeeId,
    long BranchSequence,
    DateTimeOffset ClientCreatedAt,
    DateTimeOffset ServerReceivedAt,
    IReadOnlyList<OrderLineSnapshot> Lines);

public sealed record OrderLineSnapshot(
    string OrderLineId,
    int LineSequence,
    string SellableItemId,
    string DisplayName,
    int Quantity,
    decimal UnitPrice,
    FulfillmentType FulfillmentType,
    string? Comment,
    IReadOnlyList<OrderLineOptionSnapshot> Options);

public sealed record OrderLineOptionSnapshot(
    int OptionSequence,
    string OptionGroupId,
    string SellableItemId,
    string DisplayName,
    int QuantityPerItem,
    decimal UnitPrice);
