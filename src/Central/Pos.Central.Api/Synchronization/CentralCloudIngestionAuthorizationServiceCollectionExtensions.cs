using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pos.Central.Api.Synchronization;

public static class CentralCloudIngestionAuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCentralCloudIngestionAuthorization(
        this IServiceCollection services) =>
        AddCentralCloudIngestionAuthorizationCore(
            services,
            authenticationScheme: null);

    public static IServiceCollection AddCentralCloudIngestionAuthorization(
        this IServiceCollection services,
        string authenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!CentralCloudIngestionOptionsValidator
                .IsCanonicalAuthenticationScheme(authenticationScheme))
        {
            throw new ArgumentException(
                "The authentication scheme must be a canonical ASCII identifier.",
                nameof(authenticationScheme));
        }

        return AddCentralCloudIngestionAuthorizationCore(
            services,
            authenticationScheme);
    }

    private static IServiceCollection AddCentralCloudIngestionAuthorizationCore(
        IServiceCollection services,
        string? authenticationScheme)
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
                    if (authenticationScheme is not null)
                    {
                        policy.AddAuthenticationSchemes(authenticationScheme);
                    }

                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(
                        new CentralCloudIngestionAuthorizationRequirement());
                });
        return services;
    }
}
