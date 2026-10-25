using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudOutboxDeliveryServiceCollectionExtensions
{
    public static IServiceCollection AddBranchCloudOutboxDelivery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<
            IBranchCloudOutboxReadinessProbe,
            SqliteBranchCloudOutboxReadinessProbe>();
        services
            .AddOptions<BranchCloudOutboxDeliveryOptions>()
            .Bind(configuration.GetSection(BranchCloudOutboxDeliveryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchCloudOutboxDeliveryOptions>,
            BranchCloudOutboxDeliveryOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchCloudOutboxDeliveryOptions.SectionName)
            .GetValue<bool>(nameof(BranchCloudOutboxDeliveryOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        services.TryAddSingleton<IBranchCloudOutboxClaimStore>(provider =>
            new SqliteBranchCloudOutboxClaimStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxLeaseRenewalStore>(provider =>
            new SqliteBranchCloudOutboxLeaseRenewalStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxDeliveryFailureStore>(provider =>
            new SqliteBranchCloudOutboxDeliveryFailureStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudDeliveryConfirmationStore>(provider =>
            new SqliteBranchCloudDeliveryConfirmationStore(DatabasePath(provider)));
        return services;
    }

    private static string DatabasePath(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptions<BranchCloudOutboxDeliveryOptions>>()
            .Value
            .DatabasePath
            .Trim();
}
