using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchCloudDataSentStatusResponse Map(
        string branchId,
        BranchCloudDataSentStatus? status)
    {
        branchId = RequireIdentifier(branchId);
        if (status is null)
        {
            return new BranchCloudDataSentStatusResponse(
                CurrentSchemaVersion,
                null);
        }

        if (!string.Equals(status.BranchId, branchId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Cloud data-sent status belongs to a different Branch.");
        }

        if (status.LastSuccessfullySentAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The Cloud data-sent timestamp must be UTC.");
        }

        return new BranchCloudDataSentStatusResponse(
            CurrentSchemaVersion,
            status.LastSuccessfullySentAt);
    }

    private static string RequireIdentifier(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The Branch identifier cannot be empty.",
                nameof(value))
            : value.Trim();
}
