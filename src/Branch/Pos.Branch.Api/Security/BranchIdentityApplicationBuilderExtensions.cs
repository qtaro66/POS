using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Pos.Branch.Api.Security;

public static class BranchIdentityApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBranchIdentity(
        this IApplicationBuilder application,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(configuration);

        var deviceAuthenticationEnabled = configuration
            .GetSection(BranchDeviceAuthenticationOptions.SectionName)
            .GetValue<bool>(nameof(BranchDeviceAuthenticationOptions.Enabled));
        var employeeSessionsEnabled = configuration
            .GetSection(BranchEmployeeSessionOptions.SectionName)
            .GetValue<bool>(nameof(BranchEmployeeSessionOptions.Enabled));

        if (!deviceAuthenticationEnabled && !employeeSessionsEnabled)
        {
            return application;
        }

        application.UseAuthentication();
        application.UseAuthorization();
        return application;
    }
}
