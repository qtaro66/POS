using Microsoft.Extensions.Options;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Branch.Api.Printing;

public static class EscPosRasterRenderingServiceCollectionExtensions
{
    public static IServiceCollection AddEscPosRasterRendering(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<EscPosRasterRenderingOptions>()
            .Bind(configuration.GetSection(EscPosRasterRenderingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<EscPosRasterRenderingOptions>,
            EscPosRasterRenderingOptionsValidator>();
        services.AddSingleton<IEscPosDocumentRasterizer>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<EscPosRasterRenderingOptions>>()
                .Value;
            return new SkiaThaiTextRasterizer(
                options.FontFilePath.Trim(),
                options.MonochromeThreshold);
        });
        services.AddSingleton<IPrintPayloadRenderer, EscPosRasterPayloadRenderer>();
        return services;
    }
}
