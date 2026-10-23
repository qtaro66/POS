using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public static class BranchPairingServiceCollectionExtensions
{
    public static IServiceCollection AddBranchPairing(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IBranchPairingReadinessProbe, SqliteBranchPairingReadinessProbe>();
        services
            .AddOptions<BranchPairingOptions>()
            .Bind(configuration.GetSection(BranchPairingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchPairingOptions>,
            BranchPairingOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchPairingOptions.SectionName)
            .GetValue<bool>(nameof(BranchPairingOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        var deviceAuthenticationEnabled = configuration
            .GetSection(BranchDeviceAuthenticationOptions.SectionName)
            .GetValue<bool>(nameof(BranchDeviceAuthenticationOptions.Enabled));
        var employeeSessionsEnabled = configuration
            .GetSection(BranchEmployeeSessionOptions.SectionName)
            .GetValue<bool>(nameof(BranchEmployeeSessionOptions.Enabled));
        if (!deviceAuthenticationEnabled || !employeeSessionsEnabled)
        {
            throw new InvalidOperationException(
                "Branch pairing requires BranchDeviceAuthentication and BranchEmployeeSessions to be enabled.");
        }

        services.TryAddSingleton<IBranchPairingTokenIssuanceStore>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchPairingOptions>>()
                .Value;
            return new SqliteBranchPairingTokenIssuanceStore(options.DatabasePath.Trim());
        });
        services.TryAddSingleton<IBranchPairingRedemptionStore>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchPairingOptions>>()
                .Value;
            return new SqliteBranchPairingRedemptionStore(options.DatabasePath.Trim());
        });
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<BranchPairingTokenIssuanceService>();
        services.TryAddSingleton<BranchPairingRedemptionService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                BranchPairingRedemptionRateLimitPolicy.PolicyName,
                new BranchPairingRedemptionRateLimitPolicy());
        });
        return services;
    }
}
