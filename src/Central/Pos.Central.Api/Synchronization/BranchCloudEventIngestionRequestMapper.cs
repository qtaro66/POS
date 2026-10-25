using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionRequestMapper
{
    public const int CurrentSchemaVersion = 1;

    public static BranchCloudOutboxEventEnvelope Map(
        BranchCloudEventIngestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"SchemaVersion must be {CurrentSchemaVersion}.",
                nameof(request));
        }

        if (!Enum.TryParse<TableSessionAction>(
                request.Action,
                ignoreCase: false,
                out var action) ||
            !Enum.IsDefined(action) ||
            !string.Equals(request.Action, action.ToString(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Action must be a defined case-sensitive table-session action name.",
                nameof(request));
        }

        return BranchCloudOutboxEventEnvelope.Create(
            request.EventId,
            request.BranchId,
            request.BranchSequence,
            request.RequestId,
            TableSessionId.From(request.TableSessionId),
            action,
            request.TableVersion,
            request.OccurredAt);
    }
}
