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
        services.TryAddSingleton<IBranchCloudReplayReadinessProbe, SqliteBranchCloudReplayReadinessProbe>();
        services
            .AddOptions<BranchCloudOutboxDeliveryOptions>()
            .Bind(configuration.GetSection(BranchCloudOutboxDeliveryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchCloudOutboxDeliveryOptions>,
            BranchCloudOutboxDeliveryOptionsValidator>();
        services.AddSingleton<IValidateOptions<BranchCloudOutboxDeliveryOptions>,
            BranchCloudReplayReadinessOptionsValidator>();
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

        services.TryAddSingleton<
            IBranchCloudOutboxRetryDelayPolicy,
            ExponentialBranchCloudOutboxRetryDelayPolicy>();
        services.TryAddSingleton<IBranchCloudOutboxClaimStore>(provider =>
            new SqliteBranchCloudOutboxClaimStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxLeaseRenewalStore>(provider =>
            new SqliteBranchCloudOutboxLeaseRenewalStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxDeliveryFailureStore>(provider =>
            new SqliteBranchCloudOutboxDeliveryFailureStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxSequenceReplayStore>(provider =>
            new SqliteBranchCloudOutboxSequenceReplayStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxSequenceReplayClaimStore>(provider =>
            new SqliteBranchCloudOutboxSequenceReplayClaimStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxSequenceReplayLeaseRenewalStore>(provider =>
            new SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxSequenceReplayCompletionStore>(provider =>
            new SqliteBranchCloudOutboxSequenceReplayCompletionStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudOutboxSequenceReplayFailureStore>(provider =>
            new SqliteBranchCloudOutboxSequenceReplayFailureStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudDeliveryConfirmationStore>(provider =>
            new SqliteBranchCloudDeliveryConfirmationStore(DatabasePath(provider)));
        services.TryAddSingleton<IBranchCloudReplayCycleProcessor>(provider =>
            new BranchCloudReplayCycleProcessor(
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayClaimStore>(),
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayLeaseRenewalStore>(),
                provider.GetRequiredService<IBranchCloudOutboxDeliveryClient>(),
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayCompletionStore>(),
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayFailureStore>(),
                provider.GetRequiredService<IBranchCloudOutboxSequenceReplayStore>(),
                provider.GetRequiredService<IBranchCloudOutboxRetryDelayPolicy>(),
                provider.GetRequiredService<IOptions<BranchCloudOutboxDeliveryOptions>>().Value.MaximumRetryDelay,
                provider.GetRequiredService<TimeProvider>()));
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
                    provider.GetRequiredService<IBranchCloudOutboxSequenceReplayStore>(),
                    provider.GetRequiredService<IBranchCloudOutboxRetryDelayPolicy>(),
                    provider
                        .GetRequiredService<IOptions<BranchCloudOutboxDeliveryOptions>>()
                        .Value
                        .MaximumRetryDelay,
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
