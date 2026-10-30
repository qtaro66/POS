namespace Pos.Branch.Api.Synchronization;

public interface IBranchCloudReplayCycleProcessor
{
    /// <summary>
    /// Processes at most one eligible replay request for the specified Branch.
    /// Returns NoWork when no lease can be claimed, including when the earliest
    /// replay is leased or awaiting retry. Delivers the original immutable event
    /// envelope through the authenticated Cloud client and completes recovery
    /// only after an acknowledgment matching event ID and Branch sequence.
    /// Known delivery failures record replay-specific retry evidence and release
    /// ownership. Processing must maintain the exact replay lease throughout
    /// delivery; loss of ownership or uncertain persistence must fail closed.
    /// Original event acknowledgment and normal outbox retry state remain intact.
    /// </summary>
    Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
        string branchId,
        string leaseOwner,
        TimeSpan leaseDuration,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);
}
