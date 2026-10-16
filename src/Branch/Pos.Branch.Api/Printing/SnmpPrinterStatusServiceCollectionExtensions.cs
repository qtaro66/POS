using Microsoft.Extensions.Options;

namespace Pos.Branch.Api.Printing;

public static class SnmpPrinterStatusServiceCollectionExtensions
{
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
