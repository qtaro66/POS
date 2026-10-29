using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchCloudOutboxHttpDeliveryAuthenticationValidator :
    IValidateOptions<BranchCloudOutboxDeliveryOptions>
{
    private readonly IServiceProviderIsService _services;

    public BranchCloudOutboxHttpDeliveryAuthenticationValidator(
        IServiceProviderIsService services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ValidateOptionsResult Validate(
        string? name,
        BranchCloudOutboxDeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled ||
            _services.IsService(
                typeof(IBranchCloudDeliveryAuthenticatedHttpHandlerFactory)))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{nameof(IBranchCloudDeliveryAuthenticatedHttpHandlerFactory)} " +
            "must be registered when the default Branch Cloud HTTP delivery client is enabled.");
    }
}
