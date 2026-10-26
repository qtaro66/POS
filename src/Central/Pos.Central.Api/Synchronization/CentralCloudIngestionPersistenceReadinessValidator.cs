using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralCloudIngestionPersistenceReadinessValidator
    : IValidateOptions<CentralCloudIngestionOptions>
{
    private readonly IServiceProvider _services;

    public CentralCloudIngestionPersistenceReadinessValidator(
        IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public ValidateOptionsResult Validate(
        string? name,
        CentralCloudIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var registry = _services.GetService<IServiceProviderIsService>();
        var storeRegistered = registry?.IsService(
            typeof(IBranchCloudEventIngestionStore)) == true;
        var serviceRegistered = registry?.IsService(
            typeof(BranchCloudEventIngestionService)) == true;
        if (storeRegistered && serviceRegistered)
        {
            return ValidateOptionsResult.Success;
        }

        var missing = new List<string>();
        if (!storeRegistered)
        {
            missing.Add(nameof(IBranchCloudEventIngestionStore));
        }

        if (!serviceRegistered)
        {
            missing.Add(nameof(BranchCloudEventIngestionService));
        }

        return ValidateOptionsResult.Fail(
            "Central Cloud ingestion requires registered persistence services: " +
            string.Join(", ", missing) + ".");
    }
}
