using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;

namespace Pos.Branch.Api.Synchronization;

public static class BranchCloudOutboxHttpDeliveryServiceCollectionExtensions
{
    public const string HttpClientName = "BranchCloudOutboxDelivery";

    public static IServiceCollection AddBranchCloudOutboxHttpDeliveryClient(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider
                    .GetRequiredService<
                        IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>()
                    .CreateHandler() ??
                throw new InvalidOperationException(
                    "The authenticated Branch Cloud HTTP handler factory returned no handler."));
        services.TryAddSingleton<IBranchCloudOutboxDeliveryClient>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchCloudOutboxDeliveryOptions>>()
                .Value;
            var endpoint = BranchCloudDeliveryEndpoint.Require(
                options.ServiceOrigin,
                options.DeliveryPath,
                nameof(BranchCloudOutboxDeliveryOptions.ServiceOrigin),
                nameof(BranchCloudOutboxDeliveryOptions.DeliveryPath));
            var httpClient = provider
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient(HttpClientName);

            return new HttpBranchCloudOutboxDeliveryClient(
                httpClient,
                endpoint);
        });

        return services;
    }
}
