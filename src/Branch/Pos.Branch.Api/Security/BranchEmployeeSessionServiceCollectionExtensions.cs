using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public static class BranchEmployeeSessionServiceCollectionExtensions
{
    public static IServiceCollection AddBranchEmployeeSessions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<
            IBranchEmployeeSessionReadinessProbe,
            SqliteBranchEmployeeSessionReadinessProbe>();
        services
            .AddOptions<BranchEmployeeSessionOptions>()
            .Bind(configuration.GetSection(BranchEmployeeSessionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchEmployeeSessionOptions>,
            BranchEmployeeSessionOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchEmployeeSessionOptions.SectionName)
            .GetValue<bool>(nameof(BranchEmployeeSessionOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        if (!services.Any(
                descriptor => descriptor.ServiceType ==
                    typeof(IBranchEmployeeStatusReader)))
        {
            throw new InvalidOperationException(
                "IBranchEmployeeStatusReader must be registered before enabling Branch employee sessions.");
        }

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IBranchEmployeeSessionLifetimePolicy>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchEmployeeSessionOptions>>()
                .Value;
            return new FixedBranchEmployeeSessionLifetimePolicy(options.Lifetime);
        });
        services.TryAddSingleton<IBranchEmployeeSessionIssuanceStore>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchEmployeeSessionOptions>>()
                .Value;
            return new SqliteBranchEmployeeSessionIssuanceStore(
                options.DatabasePath.Trim());
        });
        services.TryAddSingleton<IBranchEmployeeSessionValidator>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchEmployeeSessionOptions>>()
                .Value;
            return new SqliteBranchEmployeeSessionValidator(
                options.DatabasePath.Trim(),
                provider.GetRequiredService<IBranchEmployeeStatusReader>(),
                provider.GetRequiredService<TimeProvider>());
        });
        services.TryAddSingleton<BranchEmployeeSessionIssuanceService>();
        services.TryAddSingleton<BranchEmployeeSessionAuthenticationService>();
        return services;
    }
}
