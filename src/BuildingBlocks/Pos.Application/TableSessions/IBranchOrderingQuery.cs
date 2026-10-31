using Pos.Application.MasterData;

namespace Pos.Application.TableSessions;

public sealed record BranchActiveTableSession(string TableSessionId, string TableId, string Status, long Version);
public sealed record BranchSessionMenu(string TableSessionId, string MasterDataVersion, IReadOnlyList<CatalogResolutionContext> Items);

public interface IBranchOrderingQuery
{
    Task<IReadOnlyList<BranchActiveTableSession>> ListActiveAsync(string branchId, CancellationToken cancellationToken = default);
    Task<BranchSessionMenu?> MenuAsync(string branchId, string tableSessionId, CancellationToken cancellationToken = default);
}
