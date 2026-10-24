using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudDataSentStatusServiceCollectionExtensions
{
    public static IServiceCollection AddBranchCloudDataSentStatus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<
            IBranchCloudDataSentStatusReadinessProbe,
            SqliteBranchCloudDataSentStatusReadinessProbe>();
        services
            .AddOptions<BranchCloudDataSentStatusOptions>()
            .Bind(configuration.GetSection(BranchCloudDataSentStatusOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchCloudDataSentStatusOptions>,
            BranchCloudDataSentStatusOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchCloudDataSentStatusOptions.SectionName)
            .GetValue<bool>(nameof(BranchCloudDataSentStatusOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        var employeeSessionsEnabled = configuration
            .GetSection(BranchEmployeeSessionOptions.SectionName)
            .GetValue<bool>(nameof(BranchEmployeeSessionOptions.Enabled));
        if (!employeeSessionsEnabled)
        {
            throw new InvalidOperationException(
                "BranchEmployeeSessions must be enabled before enabling Branch Cloud data-sent status.");
        }

        services.TryAddSingleton<IBranchCloudDataSentStatusQuery>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchCloudDataSentStatusOptions>>()
                .Value;
            return new SqliteBranchCloudDataSentStatusQuery(
                options.DatabasePath.Trim());
        });
        services.TryAddSingleton<IBranchCloudDeliveryConfirmationStore>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchCloudDataSentStatusOptions>>()
                .Value;
            return new SqliteBranchCloudDeliveryConfirmationStore(
                options.DatabasePath.Trim());
        });
        return services;
    }
}
