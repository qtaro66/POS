using System.Text;
using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Printing.Tests;

public sealed class EnvironmentVariableSnmpV1CommunityProviderTests
{
    [Fact]
    public async Task Provider_resolves_an_exact_printer_mapping_without_caching_the_secret()
    {
        var environment = new FakeEnvironmentVariableSource("first", "second");
        var provider = Provider(environment);

        var first = await provider.GetCommunityAsync(Profile(), CancellationToken.None);
        Assert.Equal("first"u8.ToArray(), first.Bytes.ToArray());
        first.Dispose();
        using var second = await provider.GetCommunityAsync(Profile(), CancellationToken.None);

        Assert.Equal("second"u8.ToArray(), second.Bytes.ToArray());
        Assert.Equal(["POS_SNMP_COMMUNITY_PRINTER_01", "POS_SNMP_COMMUNITY_PRINTER_01"], environment.Names);
    }

    [Fact]
    public async Task Missing_mapping_fails_before_environment_access_without_disclosing_identifiers()
    {
        var environment = new FakeEnvironmentVariableSource("secret");
        var provider = new EnvironmentVariableSnmpV1CommunityProvider(
            new Dictionary<string, string>(StringComparer.Ordinal),
            environment);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetCommunityAsync(Profile(), CancellationToken.None));

        Assert.Empty(environment.Names);
        Assert.DoesNotContain("PRINTER-01", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Missing_or_empty_environment_value_is_rejected(string? value)
    {
        var provider = Provider(new FakeEnvironmentVariableSource(value));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GetCommunityAsync(Profile(), CancellationToken.None));

        Assert.DoesNotContain("PRINTER-01", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POS_SNMP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Utf8_byte_limit_is_enforced_without_returning_a_secret()
    {
        var provider = Provider(new FakeEnvironmentVariableSource(new string('ก', 86)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await provider.GetCommunityAsync(Profile(), CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_utf16_is_not_silently_replaced_when_encoding_the_secret()
    {
        var provider = Provider(new FakeEnvironmentVariableSource("\uD800"));

        await Assert.ThrowsAsync<EncoderFallbackException>(async () =>
            await provider.GetCommunityAsync(Profile(), CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_checked_before_environment_access()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var environment = new FakeEnvironmentVariableSource("secret");
        var provider = Provider(environment);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.GetCommunityAsync(Profile(), cancellation.Token));

        Assert.Empty(environment.Names);
    }

    [Fact]
    public async Task Constructor_copies_and_validates_the_mapping()
    {
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PRINTER-01"] = "POS_SNMP_COMMUNITY_PRINTER_01"
        };
        var environment = new FakeEnvironmentVariableSource("secret");
        var provider = new EnvironmentVariableSnmpV1CommunityProvider(mapping, environment);
        mapping["PRINTER-01"] = "CHANGED";

        using var secret = await provider.GetCommunityAsync(Profile(), CancellationToken.None);

        Assert.Equal(["POS_SNMP_COMMUNITY_PRINTER_01"], environment.Names);
        Assert.Throws<ArgumentException>(() => new EnvironmentVariableSnmpV1CommunityProvider(
            new Dictionary<string, string> { [" PRINTER-01"] = "VALID" },
            environment));
        Assert.Throws<ArgumentException>(() => new EnvironmentVariableSnmpV1CommunityProvider(
            new Dictionary<string, string> { ["PRINTER-01"] = " VARIABLE " },
            environment));
    }

    [Fact]
    public void Disposing_a_secret_zeroes_held_memory_and_prevents_reuse()
    {
        var secret = new SnmpV1CommunitySecret("private"u8);
        var heldMemory = secret.Bytes;

        secret.Dispose();

        Assert.All(heldMemory.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => secret.Length);
    }

    private static EnvironmentVariableSnmpV1CommunityProvider Provider(
        ISnmpV1EnvironmentVariableSource environment) =>
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PRINTER-01"] = "POS_SNMP_COMMUNITY_PRINTER_01"
            },
            environment);

    private static PrinterProfile Profile() =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            "BRANCH-01",
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9_100,
            PrinterProtocol.RawTcpEscPos,
            EpsonTmT82xM352AConditionEvaluator.ProfileId,
            "Epson",
            "TM-T82X",
            "M352A",
            "13.07 ESC/POS",
            80,
            203,
            576,
            PrinterRenderMode.BitmapRaster,
            PrinterThaiEncodingMode.UnicodeRaster,
            PrinterStatusMode.SnmpPrinterMib,
            false,
            "1D5601",
            null,
            "snmp-printer-mib-v1",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeEnvironmentVariableSource(params string?[] values)
        : ISnmpV1EnvironmentVariableSource
    {
        private readonly Queue<string?> _values = new(values);

        public List<string> Names { get; } = [];

        public string? Get(string name)
        {
            Names.Add(name);
            return _values.Dequeue();
        }
    }
}
