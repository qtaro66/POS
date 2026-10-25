namespace Pos.Application.Synchronization;

public enum BranchCloudEventPersistenceDisposition
{
    Stored,
    AlreadyStored
}

public sealed class BranchCloudEventPersistenceResult
{
    public BranchCloudEventPersistenceResult(
        BranchCloudEventPersistenceDisposition disposition,
        string eventId,
        string branchId,
        long branchSequence)
    {
        Disposition = Enum.IsDefined(disposition)
            ? disposition
            : throw new ArgumentOutOfRangeException(
                nameof(disposition),
                "The Cloud event persistence disposition is not defined.");
        EventId = RequireIdentifier(eventId, nameof(eventId));
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        BranchSequence = branchSequence >= 1
            ? branchSequence
            : throw new ArgumentOutOfRangeException(
                nameof(branchSequence),
                "The persisted Branch sequence must be positive.");
    }

    public BranchCloudEventPersistenceDisposition Disposition { get; }

    public string EventId { get; }

    public string BranchId { get; }

    public long BranchSequence { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();
}

public sealed class BranchCloudEventUniquenessConflictException : Exception
{
    public BranchCloudEventUniquenessConflictException(
        string eventId,
        string branchId,
        long branchSequence)
        : base("The Cloud event identity conflicts with an event already persisted.")
    {
        EventId = string.IsNullOrWhiteSpace(eventId)
            ? throw new ArgumentException(
                "The event identifier cannot be empty.",
                nameof(eventId))
            : eventId.Trim();
        BranchId = string.IsNullOrWhiteSpace(branchId)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(branchId))
            : branchId.Trim();
        BranchSequence = branchSequence >= 1
            ? branchSequence
            : throw new ArgumentOutOfRangeException(
                nameof(branchSequence),
                "The Branch sequence must be positive.");
    }

    public string EventId { get; }

    public string BranchId { get; }

    public long BranchSequence { get; }
}

public interface IBranchCloudEventIngestionStore
{
    /// <summary>
    /// Atomically persists one Branch event before returning Stored. An exact
    /// retry of the same event returns AlreadyStored. A reused event ID or
    /// reused Branch sequence with different event content must throw
    /// BranchCloudEventUniquenessConflictException and must not alter the
    /// previously persisted event.
    /// </summary>
    Task<BranchCloudEventPersistenceResult> PersistAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
