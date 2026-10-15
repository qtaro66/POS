using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public static class PrintSpoolPollingServiceCollectionExtensions
{
    public static IServiceCollection AddPrintSpoolPolling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services
            .AddOptions<PrintSpoolPollingOptions>()
            .Bind(configuration.GetSection(PrintSpoolPollingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PrintSpoolPollingOptions>, PrintSpoolPollingOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPrintWorkerIdentity, RuntimePrintWorkerIdentity>();
        services.AddHostedService<PrintSpoolPollingService>();
        return services;
    }
}
