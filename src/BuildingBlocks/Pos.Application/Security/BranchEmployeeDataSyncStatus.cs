namespace Pos.Application.Security;

public sealed class BranchEmployeeDataSyncStatus
{
    public BranchEmployeeDataSyncStatus(
        string branchId,
        DateTimeOffset lastSynchronizedAt)
    {
        BranchId = string.IsNullOrWhiteSpace(branchId)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(branchId))
            : branchId.Trim();
        LastSynchronizedAt = lastSynchronizedAt.ToUniversalTime();
    }

    public string BranchId { get; }

    public DateTimeOffset LastSynchronizedAt { get; }
}

public interface IBranchEmployeeDataSyncStatusQuery
{
    Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
        string branchId,
        CancellationToken cancellationToken = default);
}
