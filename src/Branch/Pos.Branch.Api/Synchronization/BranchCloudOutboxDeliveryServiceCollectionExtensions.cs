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
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<
            IBranchCloudOutboxWorkerIdentity,
            RuntimeBranchCloudOutboxWorkerIdentity>();
        services.AddHostedService<BranchCloudOutboxPollingService>();

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
        if (!services.Any(descriptor =>
                descriptor.ServiceType ==
                    typeof(IBranchCloudOutboxCycleProcessor)))
        {
            services.AddBranchCloudOutboxHttpDeliveryClient();
            services.TryAddSingleton<IBranchCloudOutboxCycleProcessor>(provider =>
                new BranchCloudOutboxCycleProcessor(
                    provider.GetRequiredService<IBranchCloudOutboxClaimStore>(),
                    provider.GetRequiredService<IBranchCloudOutboxLeaseRenewalStore>(),
                    provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>(),
                    provider.GetRequiredService<IBranchCloudDeliveryConfirmationStore>(),
                    provider.GetRequiredService<IBranchCloudOutboxDeliveryFailureStore>(),
                    provider.GetRequiredService<TimeProvider>()));
        }
        return services;
    }

    private static string DatabasePath(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptions<BranchCloudOutboxDeliveryOptions>>()
            .Value
            .DatabasePath
            .Trim();
}
