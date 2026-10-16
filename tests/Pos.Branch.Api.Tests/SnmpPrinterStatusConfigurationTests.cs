using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Branch.Api.Printing;
using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Branch.Api.Tests;

public sealed class SnmpPrinterStatusConfigurationTests
{
    [Fact]
    public void Disabled_defaults_validate_without_secret_mapping_or_runtime_registration()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["SnmpPrinterStatus:Enabled"] = "false"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<SnmpPrinterStatusOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Equal(161, options.Port);
        Assert.Empty(options.CommunityEnvironmentVariablesByPrinterId);
        Assert.Null(provider.GetService<ISnmpV1CommunityProvider>());
        Assert.Null(provider.GetService<ISnmpV1DatagramTransport>());
        Assert.Null(provider.GetService<IPrinterConditionProbe>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    [Fact]
    public void Enabled_configuration_binds_validated_endpoint_and_environment_names()
    {
        using var provider = Provider(EnabledValues());

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<SnmpPrinterStatusOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal(1161, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), options.ResponseTimeout);
        Assert.Equal(2_048, options.MaximumRequestBytes);
        Assert.Equal(8_192, options.MaximumResponseBytes);
        Assert.Equal(
            "POS_SNMP_COMMUNITY_PRINTER_01",
            options.CommunityEnvironmentVariablesByPrinterId["PRINTER-01"]);
    }

    [Fact]
    public void Enabled_configuration_composes_singletons_without_starting_background_work()
    {
        using var provider = Provider(EnabledValues());
        provider.GetRequiredService<IStartupValidator>().Validate();

        var community = provider.GetRequiredService<ISnmpV1CommunityProvider>();
        var transport = provider.GetRequiredService<ISnmpV1DatagramTransport>();
        var requestIds = provider.GetRequiredService<ISnmpV1RequestIdSource>();
        var client = provider.GetRequiredService<ISnmpV1PrinterClient>();
        var reader = provider.GetRequiredService<IPrinterMibSnapshotReader>();
        var evaluator = Assert.Single(provider.GetServices<IPrinterMibConditionEvaluator>());
        var probe = provider.GetRequiredService<IPrinterConditionProbe>();

        Assert.IsType<EnvironmentVariableSnmpV1CommunityProvider>(community);
        Assert.IsType<SnmpV1UdpDatagramTransport>(transport);
        Assert.IsType<CryptographicSnmpV1RequestIdSource>(requestIds);
        Assert.IsType<BoundedSnmpV1PrinterClient>(client);
        Assert.IsType<EpsonTmT82xM352ASnapshotReader>(reader);
        Assert.IsType<EpsonTmT82xM352AConditionEvaluator>(evaluator);
        Assert.IsType<SnmpPrinterConditionProbe>(probe);
        Assert.Same(community, provider.GetRequiredService<ISnmpV1CommunityProvider>());
        Assert.Same(probe, provider.GetRequiredService<IPrinterConditionProbe>());
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Null(provider.GetService<IPrintSpoolCycleProcessor>());
    }

    [Fact]
    public void Enabled_configuration_requires_at_least_one_secret_mapping()
    {
        var values = EnabledValues();
        values.Remove("SnmpPrinterStatus:CommunityEnvironmentVariablesByPrinterId:PRINTER-01");
        using var provider = Provider(values);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("At least one", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_invalid_endpoint_bounds_and_mapping_names()
    {
        var validator = new SnmpPrinterStatusOptionsValidator();
        var result = validator.Validate(null, new SnmpPrinterStatusOptions
        {
            Enabled = true,
            Port = 0,
            ConnectTimeout = TimeSpan.Zero,
            ResponseTimeout = TimeSpan.FromSeconds(-1),
            MaximumRequestBytes = 0,
            MaximumResponseBytes = 65_508,
            CommunityEnvironmentVariablesByPrinterId = new Dictionary<string, string>
            {
                [" PRINTER-01"] = "PUBLIC",
                ["PRINTER-02"] = "lowercase_name"
            }
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Port", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("ConnectTimeout", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("ResponseTimeout", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("MaximumRequestBytes", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("MaximumResponseBytes", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("printer identifiers", StringComparison.Ordinal));
        Assert.Equal(
            2,
            result.Failures.Count(failure =>
                failure.Contains("environment-variable names", StringComparison.Ordinal)));
    }

    [Fact]
    public void Disabled_configuration_does_not_activate_or_validate_dormant_runtime_values()
    {
        var validator = new SnmpPrinterStatusOptionsValidator();

        var result = validator.Validate(null, new SnmpPrinterStatusOptions
        {
            Enabled = false,
            Port = 0,
            ConnectTimeout = TimeSpan.Zero,
            ResponseTimeout = TimeSpan.Zero,
            MaximumRequestBytes = 0,
            MaximumResponseBytes = 0,
            CommunityEnvironmentVariablesByPrinterId = []
        });

        Assert.True(result.Succeeded);
    }

    private static Dictionary<string, string?> EnabledValues() =>
        new(StringComparer.Ordinal)
        {
            ["SnmpPrinterStatus:Enabled"] = "true",
            ["SnmpPrinterStatus:Port"] = "1161",
            ["SnmpPrinterStatus:ConnectTimeout"] = "00:00:03",
            ["SnmpPrinterStatus:ResponseTimeout"] = "00:00:04",
            ["SnmpPrinterStatus:MaximumRequestBytes"] = "2048",
            ["SnmpPrinterStatus:MaximumResponseBytes"] = "8192",
            ["SnmpPrinterStatus:CommunityEnvironmentVariablesByPrinterId:PRINTER-01"] =
                "POS_SNMP_COMMUNITY_PRINTER_01"
        };

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSnmpPrinterStatus(configuration);
        return services.BuildServiceProvider();
    }
}
