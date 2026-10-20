using Microsoft.AspNetCore.RateLimiting;

namespace Pos.Branch.Api.Security;

public static class BranchPairingApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchPairingRateLimiting(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = configuration
            .GetSection(BranchPairingOptions.SectionName)
            .GetValue<bool>(nameof(BranchPairingOptions.Enabled));
        return enabled
            ? application.UseRateLimiter()
            : application;
    }
}
