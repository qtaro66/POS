using Microsoft.Data.Sqlite;
using Pos.Application.MasterData;

namespace Pos.Branch.Infrastructure.Sqlite;

public interface ISqliteCatalogSnapshotResolver
{
    /// <summary>
    /// Resolves frozen catalog content and current sellability using the supplied serialized
    /// Branch transaction. Implementations must not open another connection for the read.
    /// </summary>
    Task<CatalogResolutionResult> ResolveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogSelectionRequest request,
        CancellationToken cancellationToken);
}
