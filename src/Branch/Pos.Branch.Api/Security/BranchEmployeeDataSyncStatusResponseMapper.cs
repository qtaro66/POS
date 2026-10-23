using Pos.Application.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeDataSyncStatusResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchEmployeeDataSyncStatusResponse Map(
        string branchId,
        BranchEmployeeDataSyncStatus? status)
    {
        branchId = RequireIdentifier(branchId);
        if (status is null)
        {
            return new BranchEmployeeDataSyncStatusResponse(
                CurrentSchemaVersion,
                null);
        }

        if (!string.Equals(status.BranchId, branchId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The employee synchronization status belongs to a different Branch.");
        }

        if (status.LastSynchronizedAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The employee synchronization timestamp must be UTC.");
        }

        return new BranchEmployeeDataSyncStatusResponse(
            CurrentSchemaVersion,
            status.LastSynchronizedAt);
    }

    private static string RequireIdentifier(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(value))
            : value.Trim();
}
