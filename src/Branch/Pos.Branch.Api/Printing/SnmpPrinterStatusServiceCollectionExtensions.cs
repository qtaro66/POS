using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Branch.Api.Printing;

public static class SnmpPrinterStatusServiceCollectionExtensions
{
    public static IServiceCollection AddSnmpPrinterStatus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSnmpPrinterStatusConfiguration(configuration);
        var section = configuration.GetSection(SnmpPrinterStatusOptions.SectionName);
        if (!section.GetValue<bool>(nameof(SnmpPrinterStatusOptions.Enabled)))
        {
            return services;
        }

        services.TryAddSingleton<ISnmpV1CommunityProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SnmpPrinterStatusOptions>>().Value;
            return new EnvironmentVariableSnmpV1CommunityProvider(
                options.CommunityEnvironmentVariablesByPrinterId);
        });
        services.TryAddSingleton<ISnmpV1DatagramTransport>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<SnmpPrinterStatusOptions>>().Value;
            return new SnmpV1UdpDatagramTransport(new SnmpV1UdpTransportOptions(
                options.Port,
                options.ConnectTimeout,
                options.ResponseTimeout,
                options.MaximumRequestBytes,
                options.MaximumResponseBytes));
        });
        services.TryAddSingleton<ISnmpV1RequestIdSource, CryptographicSnmpV1RequestIdSource>();
        services.TryAddSingleton<ISnmpV1PrinterClient, BoundedSnmpV1PrinterClient>();
        services.TryAddSingleton<IPrinterMibSnapshotReader, EpsonTmT82xM352ASnapshotReader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IPrinterMibConditionEvaluator,
            EpsonTmT82xM352AConditionEvaluator>());
        services.TryAddSingleton<IPrinterConditionProbe, SnmpPrinterConditionProbe>();
        return services;
    }

    public static IServiceCollection AddSnmpPrinterStatusConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<SnmpPrinterStatusOptions>()
            .Bind(configuration.GetSection(SnmpPrinterStatusOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<SnmpPrinterStatusOptions>,
            SnmpPrinterStatusOptionsValidator>();
        return services;
    }
}
