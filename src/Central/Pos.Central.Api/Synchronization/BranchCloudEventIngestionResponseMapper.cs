using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionResponseMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchCloudEventIngestionResponse Map(
        BranchCloudEventPersistenceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new BranchCloudEventIngestionResponse(
            CurrentSchemaVersion,
            result.EventId,
            result.BranchId,
            result.BranchSequence);
    }
}
