using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Branch.Api.Printing;

public static class PrintRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddPrintRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<PrintRuntimeOptions>()
            .Bind(configuration.GetSection(PrintRuntimeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<PrintRuntimeOptions>,
            PrintRuntimeOptionsValidator>();

        var pollingEnabled = configuration
            .GetSection(PrintSpoolPollingOptions.SectionName)
            .GetValue<bool>(nameof(PrintSpoolPollingOptions.Enabled));
        if (!pollingEnabled)
        {
            return services;
        }

        services.TryAddSingleton(provider =>
            new SqlitePrintDispatchStore(DatabasePath(provider)));
        services.TryAddSingleton(provider =>
            new SqliteRenderedPrintPayloadStore(DatabasePath(provider)));
        services.TryAddSingleton<IPrinterConfigurationProvider>(provider =>
            new SqlitePrinterConfigurationProvider(DatabasePath(provider)));
        services.TryAddSingleton<IPrinterConditionProbe, UnsupportedPrinterConditionProbe>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPrintTransportAdapter, RawTcpPrintTransportAdapter>());
        services.TryAddSingleton<IPrintDispatcher, SafePrintDispatcher>();
        services.TryAddSingleton(provider =>
            new PersistedPrintSpoolWorker(
                provider.GetRequiredService<SqlitePrintDispatchStore>(),
                provider.GetRequiredService<SqliteRenderedPrintPayloadStore>(),
                provider.GetRequiredService<IPrinterConfigurationProvider>(),
                provider.GetRequiredService<IPrintPayloadRenderer>(),
                provider.GetRequiredService<IPrintDispatcher>(),
                provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IPrintSpoolCycleProcessor, PersistedPrintSpoolCycleProcessor>();
        return services;
    }

    private static string DatabasePath(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<PrintRuntimeOptions>>().Value.DatabasePath.Trim();
}
