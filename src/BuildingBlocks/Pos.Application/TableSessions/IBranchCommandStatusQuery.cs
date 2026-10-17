namespace Pos.Application.TableSessions;

public interface IBranchCommandStatusQuery
{
    Task<BranchCommandReceipt?> FindAsync(
        string branchId,
        string requestId,
        CancellationToken cancellationToken = default);
}
