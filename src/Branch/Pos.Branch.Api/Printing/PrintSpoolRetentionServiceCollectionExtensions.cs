using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public static class PrintSpoolRetentionServiceCollectionExtensions
{
    public static IServiceCollection AddPrintSpoolRetention(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services
            .AddOptions<PrintSpoolRetentionOptions>()
            .Bind(configuration.GetSection(PrintSpoolRetentionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<PrintSpoolRetentionOptions>,
            PrintSpoolRetentionOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<PrintSpoolRetentionService>();
        return services;
    }
}
