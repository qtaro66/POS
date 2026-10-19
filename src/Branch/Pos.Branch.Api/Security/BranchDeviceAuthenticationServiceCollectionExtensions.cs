using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Security;

public static class BranchDeviceAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddBranchDeviceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<
            IBranchDeviceCredentialReadinessProbe,
            SqliteBranchDeviceCredentialReadinessProbe>();
        services
            .AddOptions<BranchDeviceAuthenticationOptions>()
            .Bind(configuration.GetSection(BranchDeviceAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchDeviceAuthenticationOptions>,
            BranchDeviceAuthenticationOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchDeviceAuthenticationOptions.SectionName)
            .GetValue<bool>(nameof(BranchDeviceAuthenticationOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        services.TryAddSingleton<IBranchDeviceCredentialValidator>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchDeviceAuthenticationOptions>>()
                .Value;
            return new SqliteBranchDeviceCredentialValidator(options.DatabasePath.Trim());
        });
        services.TryAddSingleton<BranchDeviceTokenAuthenticationService>();
        services
            .AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, BranchDeviceAuthenticationHandler>(
                BranchDeviceAuthenticationDefaults.SchemeName,
                _ => { });
        return services;
    }
}
