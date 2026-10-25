using Pos.Application.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Central.Api.Synchronization;

public sealed class AuthenticatedCentralBranchIdentity
{
    private AuthenticatedCentralBranchIdentity(string branchId)
    {
        BranchId = branchId;
    }

    public string BranchId { get; }

    public static AuthenticatedCentralBranchIdentity Create(string branchId) =>
        new(string.IsNullOrWhiteSpace(branchId)
            ? throw new ArgumentException(
                "The authenticated Branch identifier cannot be empty.",
                nameof(branchId))
            : branchId.Trim());
}

public sealed class BranchCloudEventBranchMismatchException : Exception
{
    public BranchCloudEventBranchMismatchException(
        string authenticatedBranchId,
        string requestedBranchId)
        : base("The Cloud ingestion request belongs to a different Branch.")
    {
        AuthenticatedBranchId = authenticatedBranchId;
        RequestedBranchId = requestedBranchId;
    }

    public string AuthenticatedBranchId { get; }

    public string RequestedBranchId { get; }
}

public static class BranchCloudEventIngestionIdentityHandler
{
    public static async Task<BranchCloudEventIngestionResponse> HandleAsync(
        BranchCloudEventIngestionRequest request,
        AuthenticatedCentralBranchIdentity identity,
        BranchCloudEventIngestionService service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(service);

        var envelope = BranchCloudEventIngestionRequestMapper.Map(request);
        if (!string.Equals(
                envelope.BranchId,
                identity.BranchId,
                StringComparison.Ordinal))
        {
            throw new BranchCloudEventBranchMismatchException(
                identity.BranchId,
                envelope.BranchId);
        }

        var result = await service.IngestAsync(envelope, cancellationToken);
        return BranchCloudEventIngestionResponseMapper.Map(result);
    }
}
