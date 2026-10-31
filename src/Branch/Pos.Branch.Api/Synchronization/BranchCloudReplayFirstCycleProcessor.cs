namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudReplayFirstCycleProcessor(
    IBranchCloudReplayCycleProcessor replay,
    IBranchCloudOutboxCycleProcessor normal) : IBranchCloudOutboxCycleProcessor
{
    private readonly IBranchCloudReplayCycleProcessor _replay =
        replay ?? throw new ArgumentNullException(nameof(replay));
    private readonly IBranchCloudOutboxCycleProcessor _normal =
        normal ?? throw new ArgumentNullException(nameof(normal));

    public async Task<BranchCloudOutboxCycleResult> ProcessNextAsync(
        string branchId, string leaseOwner, TimeSpan leaseDuration,
        TimeSpan retryDelay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _replay.ProcessNextAsync(
            branchId, leaseOwner, leaseDuration, retryDelay, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Disposition switch
        {
            BranchCloudOutboxCycleDisposition.Processed => result,
            BranchCloudOutboxCycleDisposition.NoWork => await _normal.ProcessNextAsync(
                branchId, leaseOwner, leaseDuration, retryDelay, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported replay scheduling disposition.")
        };
    }
}
