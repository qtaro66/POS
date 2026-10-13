using Microsoft.Data.Sqlite;
using Pos.Application.Printing;

namespace Pos.Branch.Infrastructure.Sqlite;

public interface ISqliteBillPreviewContentSource
{
    /// <summary>
    /// Captures the authoritative Bill Preview payload using the supplied connection and
    /// transaction. Implementations must not open another connection for the read.
    /// </summary>
    Task<BillPreviewContent> CaptureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken);
}
