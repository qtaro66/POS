using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Printing;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Branch.Api.Tests;

public sealed class PrintRuntimeCompositionTests
{
    [Fact]
    public void Disabled_polling_registers_no_runtime_services_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["PrintSpoolPolling:Enabled"] = "false",
            ["PrintRuntime:DatabasePath"] = string.Empty,
            ["SnmpPrinterStatus:Enabled"] = "false"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<SqlitePrintDispatchStore>());
        Assert.Null(provider.GetService<IPrinterConfigurationProvider>());
        Assert.Null(provider.GetService<IPrintDispatcher>());
        Assert.Null(provider.GetService<IPrintSpoolCycleProcessor>());
        Assert.Empty(provider.GetServices<IPrintTransportAdapter>());
    }

    [Fact]
    public void Enabled_polling_fails_closed_without_existing_database()
    {
        var missingDatabase = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.db");
        var validator = new PrintRuntimeOptionsValidator(
            Options.Create(new PrintSpoolPollingOptions
            {
                Enabled = true,
                PrinterIds = ["PRINTER-01"]
            }),
            new SqliteBranchDatabaseReadinessProbe());

        var result = validator.Validate(null, new PrintRuntimeOptions
        {
            DatabasePath = missingDatabase
        });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("existing Branch database file", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_polling_rejects_unmigrated_database()
    {
        var databasePath = CreateDatabaseCandidate();
        File.WriteAllBytes(databasePath, []);
        try
        {
            var validator = new PrintRuntimeOptionsValidator(
                Options.Create(new PrintSpoolPollingOptions { Enabled = true }),
                new SqliteBranchDatabaseReadinessProbe());

            var result = validator.Validate(null, new PrintRuntimeOptions
            {
                DatabasePath = databasePath
            });

            Assert.True(result.Failed);
            Assert.Contains(
                result.Failures,
                failure => failure.Contains("schema version must be", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Unsupported_fallback_probe_never_claims_snmp_condition()
    {
        var probe = new UnsupportedPrinterConditionProbe();

        var unsupported = await probe.GetConditionAsync(
            Profile(PrinterStatusMode.Unsupported),
            CancellationToken.None);
        var snmp = await probe.GetConditionAsync(
            Profile(PrinterStatusMode.SnmpPrinterMib),
            CancellationToken.None);

        Assert.Equal(PrinterCondition.StatusUnsupported, unsupported);
        Assert.Equal(PrinterCondition.Unknown, snmp);
    }

    [Fact]
    public void Enabled_polling_without_snmp_composes_only_the_safe_fallback_probe()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["PrintSpoolPolling:Enabled"] = "true",
            ["SnmpPrinterStatus:Enabled"] = "false"
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrintSpoolPolling(configuration);
        services.AddSnmpPrinterStatus(configuration);

        services.AddPrintRuntime(configuration);

        var probe = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IPrinterConditionProbe));
        Assert.Equal(typeof(UnsupportedPrinterConditionProbe), probe.ImplementationType);
    }

    [Fact]
    public async Task Enabled_polling_composes_one_inert_persisted_runtime()
    {
        var databasePath = CreateDatabaseCandidate();
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            var configuration = Configuration(new Dictionary<string, string?>
            {
                ["PrintSpoolPolling:Enabled"] = "true",
                ["PrintSpoolPolling:PrinterIds:0"] = "PRINTER-01",
                ["PrintRuntime:DatabasePath"] = databasePath,
                ["EscPosRasterRendering:FontFilePath"] = FontPath("NotoSansThaiLooped-Regular.ttf"),
                ["EscPosRasterRendering:BoldFontFilePath"] = FontPath("NotoSansThaiLooped-Bold.ttf"),
                ["SnmpPrinterStatus:Enabled"] = "true",
                ["SnmpPrinterStatus:CommunityEnvironmentVariablesByPrinterId:PRINTER-01"] =
                    "POS_SNMP_COMMUNITY_PRINTER_01"
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPrintSpoolPolling(configuration);
            services.AddEscPosRasterRendering(configuration);
            services.AddSnmpPrinterStatus(configuration);

            services.AddPrintRuntime(configuration);

            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(IPrintSpoolCycleProcessor));
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(IPrintDispatcher));
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(IPrinterConfigurationProvider));
            var adapter = Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(IPrintTransportAdapter));
            Assert.Equal(typeof(RawTcpPrintTransportAdapter), adapter.ImplementationType);
            Assert.DoesNotContain(
                services,
                descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType != typeof(PrintSpoolPollingService));

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
            var processor = provider.GetRequiredService<IPrintSpoolCycleProcessor>();
            Assert.IsType<PersistedPrintSpoolCycleProcessor>(processor);
            Assert.Same(processor, provider.GetRequiredService<IPrintSpoolCycleProcessor>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrintSpoolPolling(configuration);
        services.AddEscPosRasterRendering(configuration);
        services.AddSnmpPrinterStatus(configuration);
        services.AddPrintRuntime(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string CreateDatabaseCandidate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"branch-{Guid.NewGuid():N}.db");
        return path;
    }

    private static string FontPath(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Fonts",
            "NotoSansThaiLooped",
            "2.000",
            fileName);

    private static PrinterProfile Profile(PrinterStatusMode statusMode) =>
        new(
            "PRINTER-01",
            "Kitchen",
            "BRANCH-01",
            "STATION-01",
            "192.0.2.1",
            9_100,
            PrinterProtocol.RawTcpEscPos,
            "epson-tm-t82x-m352a-v1",
            "Epson",
            "TM-T82X",
            "M352A",
            null,
            80,
            203,
            576,
            PrinterRenderMode.BitmapRaster,
            PrinterThaiEncodingMode.UnicodeRaster,
            statusMode,
            false,
            null,
            null,
            statusMode is PrinterStatusMode.Unsupported ? null : "printer-mib-v1",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2),
            0,
            TimeSpan.FromSeconds(1));
}
