using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Central.Api.Synchronization;

public static class BranchCloudEventIngestionHandler
{
    public static async Task<BranchCloudEventIngestionResponse> HandleAsync(
        BranchCloudEventIngestionRequest request,
        BranchCloudEventIngestionService service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(service);

        var envelope = BranchCloudEventIngestionRequestMapper.Map(request);
        var result = await service.IngestAsync(envelope, cancellationToken);
        return BranchCloudEventIngestionResponseMapper.Map(result);
    }
}
