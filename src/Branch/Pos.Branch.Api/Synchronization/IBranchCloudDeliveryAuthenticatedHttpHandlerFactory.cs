namespace Pos.Branch.Api.Synchronization;

/// <summary>
/// Deployment boundary for the authenticated Branch-to-Central HTTP pipeline.
/// Implementations must attach the configured Branch identity and retain normal
/// server-certificate validation. Secrets and private keys must remain outside
/// source control and application configuration committed to the repository.
/// </summary>
public interface IBranchCloudDeliveryAuthenticatedHttpHandlerFactory
{
    /// <summary>
    /// Creates a new handler instance whose lifetime is owned by the named
    /// <see cref="IHttpClientFactory"/> pipeline.
    /// </summary>
    HttpMessageHandler CreateHandler();
}
