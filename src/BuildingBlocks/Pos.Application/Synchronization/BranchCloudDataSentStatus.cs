namespace Pos.Application.Synchronization;

public sealed class BranchCloudDataSentStatus
{
    public BranchCloudDataSentStatus(
        string branchId,
        DateTimeOffset lastSuccessfullySentAt)
    {
        BranchId = string.IsNullOrWhiteSpace(branchId)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(branchId))
            : branchId.Trim();
        LastSuccessfullySentAt = lastSuccessfullySentAt.ToUniversalTime();
    }

    public string BranchId { get; }

    public DateTimeOffset LastSuccessfullySentAt { get; }
}

public interface IBranchCloudDataSentStatusQuery
{
    Task<BranchCloudDataSentStatus?> FindLatestAsync(
        string branchId,
        CancellationToken cancellationToken = default);
}
