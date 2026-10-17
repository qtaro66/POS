using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Printing;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Branch.Api.Tests;

public sealed class EscPosRasterRenderingCompositionTests
{
    [Fact]
    public void Disabled_polling_allows_startup_validation_without_font()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["PrintSpoolPolling:Enabled"] = "false",
            ["EscPosRasterRendering:FontFilePath"] = string.Empty,
            ["EscPosRasterRendering:BoldFontFilePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Enabled_polling_fails_startup_validation_when_font_is_missing()
    {
        var missingFont = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.ttf");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["PrintSpoolPolling:Enabled"] = "true",
            ["PrintSpoolPolling:PrinterIds:0"] = "PRINTER-01",
            ["EscPosRasterRendering:FontFilePath"] = missingFont,
            ["EscPosRasterRendering:BoldFontFilePath"] = missingFont
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("existing file", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_polling_requires_absolute_supported_font_and_safe_threshold()
    {
        var validator = new EscPosRasterRenderingOptionsValidator(
            Options.Create(new PrintSpoolPollingOptions { Enabled = true }));

        var result = validator.Validate(null, new EscPosRasterRenderingOptions
        {
            FontFilePath = "relative-font.bin",
            BoldFontFilePath = "relative-font.bin",
            MonochromeThreshold = 255
        });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("absolute path", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("between 1 and 254", StringComparison.Ordinal));

        var unsupportedResult = validator.Validate(null, new EscPosRasterRenderingOptions
        {
            FontFilePath = Path.GetFullPath("font.bin"),
            BoldFontFilePath = Path.GetFullPath("font.bin"),
            MonochromeThreshold = 180
        });
        Assert.True(unsupportedResult.Failed);
        Assert.Contains(
            unsupportedResult.Failures,
            failure => failure.Contains(".ttf or .otf", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_polling_requires_the_bold_font_independently()
    {
        var regularPath = Path.Combine(Path.GetTempPath(), $"font-regular-{Guid.NewGuid():N}.ttf");
        var missingBoldPath = Path.Combine(Path.GetTempPath(), $"font-bold-missing-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(regularPath, [0x00, 0x01, 0x00, 0x00]);
            var validator = new EscPosRasterRenderingOptionsValidator(
                Options.Create(new PrintSpoolPollingOptions { Enabled = true }));

            var result = validator.Validate(null, new EscPosRasterRenderingOptions
            {
                FontFilePath = regularPath,
                BoldFontFilePath = missingBoldPath,
                MonochromeThreshold = 180
            });

            Assert.True(result.Failed);
            Assert.Contains(
                result.Failures,
                failure => failure.Contains("BoldFontFilePath", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(regularPath);
        }
    }

    [Fact]
    public void Enabled_polling_accepts_existing_readable_font_candidate()
    {
        var regularPath = Path.Combine(Path.GetTempPath(), $"font-regular-{Guid.NewGuid():N}.ttf");
        var boldPath = Path.Combine(Path.GetTempPath(), $"font-bold-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(regularPath, [0x00, 0x01, 0x00, 0x00]);
            File.WriteAllBytes(boldPath, [0x00, 0x01, 0x00, 0x00]);
            var validator = new EscPosRasterRenderingOptionsValidator(
                Options.Create(new PrintSpoolPollingOptions { Enabled = true }));

            var result = validator.Validate(null, new EscPosRasterRenderingOptions
            {
                FontFilePath = regularPath,
                BoldFontFilePath = boldPath,
                MonochromeThreshold = 180
            });

            Assert.True(result.Succeeded);
        }
        finally
        {
            File.Delete(regularPath);
            File.Delete(boldPath);
        }
    }

    [Fact]
    public void Registration_is_singleton_and_does_not_add_transport_or_cycle_processor()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrintSpoolPolling(configuration);
        services.AddPrintSpoolRetention(configuration);

        services.AddEscPosRasterRendering(configuration);

        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IEscPosDocumentRasterizer) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(IPrintPayloadRenderer) &&
                descriptor.ImplementationType == typeof(EscPosRasterPayloadRenderer) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IPrintSpoolCycleProcessor));
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Name.Contains("Transport", StringComparison.Ordinal));
    }

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrintSpoolPolling(configuration);
        services.AddPrintSpoolRetention(configuration);
        services.AddEscPosRasterRendering(configuration);
        return services.BuildServiceProvider();
    }
}
