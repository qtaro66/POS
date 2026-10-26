using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCentralCloudIngestionAuthorization(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAuthorizationHandler,
                CentralCloudIngestionAuthorizationHandler>());
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                CentralCloudIngestionAuthorizationDefaults.PolicyName,
                policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(
                        new CentralCloudIngestionAuthorizationRequirement());
                });
        return services;
    }
}
