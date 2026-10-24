namespace Pos.Application.Synchronization;

public sealed class BranchCloudDeliveryConfirmation
{
    public BranchCloudDeliveryConfirmation(
        string branchId,
        string outboxId,
        DateTimeOffset acknowledgedAt)
    {
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        OutboxId = RequireIdentifier(outboxId, nameof(outboxId));
        AcknowledgedAt = acknowledgedAt.Offset == TimeSpan.Zero
            ? acknowledgedAt
            : throw new ArgumentException(
                "The acknowledgment timestamp must be UTC.",
                nameof(acknowledgedAt));
    }

    public string BranchId { get; }

    public string OutboxId { get; }

    public DateTimeOffset AcknowledgedAt { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

public enum BranchCloudDeliveryConfirmationResult
{
    Confirmed,
    AlreadyConfirmed,
    NotFound
}

public interface IBranchCloudDeliveryConfirmationStore
{
    /// <summary>
    /// Atomically records the first durable Cloud acknowledgment for the
    /// matching Branch and outbox item. Repeated confirmation returns
    /// <see cref="BranchCloudDeliveryConfirmationResult.AlreadyConfirmed"/>
    /// and must preserve the original acknowledgment timestamp. A missing or
    /// cross-Branch item returns
    /// <see cref="BranchCloudDeliveryConfirmationResult.NotFound"/>.
    /// </summary>
    Task<BranchCloudDeliveryConfirmationResult> ConfirmAsync(
        BranchCloudDeliveryConfirmation confirmation,
        CancellationToken cancellationToken = default);
}
