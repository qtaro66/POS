namespace Pos.Application.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayRequest
{
    public BranchCloudOutboxSequenceReplayRequest(
        string recoveryId,
        string branchId,
        long expectedBranchSequence,
        long receivedBranchSequence,
        DateTimeOffset requestedAt)
    {
        RecoveryId = RequireIdentifier(recoveryId, nameof(recoveryId));
        BranchId = RequireIdentifier(branchId, nameof(branchId));
        ExpectedBranchSequence = expectedBranchSequence >= 1
            ? expectedBranchSequence
            : throw new ArgumentOutOfRangeException(
                nameof(expectedBranchSequence),
                "The expected Branch sequence must be positive.");
        ReceivedBranchSequence = receivedBranchSequence > expectedBranchSequence
            ? receivedBranchSequence
            : throw new ArgumentOutOfRangeException(
                nameof(receivedBranchSequence),
                "The received Branch sequence must be greater than the expected sequence.");
        RequestedAt = requestedAt.Offset == TimeSpan.Zero
            ? requestedAt
            : throw new ArgumentException(
                "The replay-request timestamp must be UTC.",
                nameof(requestedAt));
    }

    public string RecoveryId { get; }

    public string BranchId { get; }

    public long ExpectedBranchSequence { get; }

    public long ReceivedBranchSequence { get; }

    public DateTimeOffset RequestedAt { get; }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();
}

public enum BranchCloudOutboxSequenceReplayResult
{
    Requested,
    AlreadyRequested,
    ExpectedSequenceNotFound
}

public interface IBranchCloudOutboxSequenceReplayStore
{
    /// <summary>
    /// Atomically marks the retained event at the expected Branch sequence for
    /// replay without deleting or replacing its original durable-acknowledgment
    /// evidence. Repeating the same recovery identifier with the same request
    /// returns AlreadyRequested. Reusing it for different recovery data must
    /// fail closed. A missing retained sequence returns ExpectedSequenceNotFound.
    /// </summary>
    Task<BranchCloudOutboxSequenceReplayResult> RequestAsync(
        BranchCloudOutboxSequenceReplayRequest request,
        CancellationToken cancellationToken = default);
}
