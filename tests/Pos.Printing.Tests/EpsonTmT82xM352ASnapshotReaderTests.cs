using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Printing.Tests;

public sealed class EpsonTmT82xM352ASnapshotReaderTests
{
    private const string CoverStatusOid = "1.3.6.1.2.1.43.6.1.1.3.1.1";
    private const string PaperCurrentLevelOid = "1.3.6.1.2.1.43.8.2.1.10.1.1";
    private const string PaperStatusOid = "1.3.6.1.2.1.43.8.2.1.11.1.1";
    private const string AlertTableOid = "1.3.6.1.2.1.43.18.1.1";

    [Fact]
    public async Task Reads_verified_scalars_and_pairs_alert_columns_by_table_index()
    {
        var client = ReadyClient(
            new SnmpV1Variable(
                $"{AlertTableOid}.8.1.42",
                new SnmpV1OctetStringValue(" Roll Paper End ")),
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.42",
                new SnmpV1IntegerValue(808)),
            new SnmpV1Variable(
                $"{AlertTableOid}.8.1.43",
                new SnmpV1OctetStringValue(string.Empty)),
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.43",
                new SnmpV1IntegerValue(999)));

        var snapshot = await new EpsonTmT82xM352ASnapshotReader(client)
            .ReadAsync(Profile(), CancellationToken.None);

        Assert.Equal(4, snapshot.CoverStatus);
        Assert.Equal(-3, snapshot.PaperCurrentLevel);
        Assert.Equal(0, snapshot.PaperStatus);
        Assert.True(snapshot.AlertTableComplete);
        Assert.Collection(
            snapshot.ActiveAlerts,
            alert =>
            {
                Assert.Equal(808, alert.Code);
                Assert.Equal("Roll Paper End", alert.Description);
            },
            alert =>
            {
                Assert.Equal(999, alert.Code);
                Assert.Equal(string.Empty, alert.Description);
            });
        Assert.Equal(
            [CoverStatusOid, PaperCurrentLevelOid, PaperStatusOid],
            client.GetOids);
        Assert.Equal(AlertTableOid, client.WalkSubtreeOid);
        Assert.Equal(256, client.WalkMaximumVariables);
    }

    [Fact]
    public async Task Complete_empty_alert_table_preserves_ready_evidence()
    {
        var snapshot = await new EpsonTmT82xM352ASnapshotReader(ReadyClient())
            .ReadAsync(Profile(), CancellationToken.None);

        Assert.True(snapshot.AlertTableComplete);
        Assert.Empty(snapshot.ActiveAlerts);
    }

    [Fact]
    public async Task Incomplete_walk_fails_closed_even_when_known_rows_are_paired()
    {
        var client = ReadyClient(
            false,
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.42",
                new SnmpV1IntegerValue(808)),
            new SnmpV1Variable(
                $"{AlertTableOid}.8.1.42",
                new SnmpV1OctetStringValue("Roll Paper End")));

        var snapshot = await new EpsonTmT82xM352ASnapshotReader(client)
            .ReadAsync(Profile(), CancellationToken.None);

        Assert.False(snapshot.AlertTableComplete);
        Assert.Single(snapshot.ActiveAlerts);
    }

    [Theory]
    [MemberData(nameof(IncompleteAlertRows))]
    public async Task Malformed_duplicate_or_unpaired_alert_rows_fail_closed(
        SnmpV1Variable[] variables)
    {
        var snapshot = await new EpsonTmT82xM352ASnapshotReader(ReadyClient(variables))
            .ReadAsync(Profile(), CancellationToken.None);

        Assert.False(snapshot.AlertTableComplete);
    }

    [Fact]
    public async Task Unexpected_scalar_oid_or_type_is_rejected()
    {
        var wrongOid = ReadyClient();
        wrongOid.GetResponses[CoverStatusOid] = new SnmpV1Variable(
            "1.3.6.1.2.1.43.6.1.1.3.1.2",
            new SnmpV1IntegerValue(4));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EpsonTmT82xM352ASnapshotReader(wrongOid)
                .ReadAsync(Profile(), CancellationToken.None));

        var wrongType = ReadyClient();
        wrongType.GetResponses[CoverStatusOid] = new SnmpV1Variable(
            CoverStatusOid,
            new SnmpV1OctetStringValue("4"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EpsonTmT82xM352ASnapshotReader(wrongType)
                .ReadAsync(Profile(), CancellationToken.None));
    }

    [Fact]
    public async Task Unsupported_profile_is_rejected_before_client_access()
    {
        var client = ReadyClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new EpsonTmT82xM352ASnapshotReader(client)
                .ReadAsync(Profile("another-profile"), CancellationToken.None));

        Assert.Empty(client.GetOids);
        Assert.Null(client.WalkSubtreeOid);
    }

    [Fact]
    public async Task Unexpected_status_command_profile_is_rejected_before_client_access()
    {
        var client = ReadyClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new EpsonTmT82xM352ASnapshotReader(client)
                .ReadAsync(Profile(statusCommandProfile: "unverified-snmp-profile"), CancellationToken.None));

        Assert.Empty(client.GetOids);
        Assert.Null(client.WalkSubtreeOid);
    }

    [Fact]
    public void Snmp_contracts_own_collections_and_require_valid_oids()
    {
        var variables = new[]
        {
            new SnmpV1Variable(CoverStatusOid, new SnmpV1IntegerValue(4))
        };
        var walk = new SnmpV1WalkResult(true, variables);

        variables[0] = new SnmpV1Variable(PaperStatusOid, new SnmpV1IntegerValue(0));
        var exposed = Assert.IsType<SnmpV1Variable[]>(walk.Variables);
        exposed[0] = new SnmpV1Variable(PaperStatusOid, new SnmpV1IntegerValue(0));

        Assert.Equal(CoverStatusOid, walk.Variables[0].Oid);
        Assert.Throws<ArgumentException>(() =>
            new SnmpV1Variable("1.3.invalid", new SnmpV1IntegerValue(0)));
    }

    public static TheoryData<SnmpV1Variable[]> IncompleteAlertRows => new()
    {
        new[]
        {
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.42",
                new SnmpV1IntegerValue(808))
        },
        new[]
        {
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.42",
                new SnmpV1IntegerValue(808)),
            new SnmpV1Variable(
                $"{AlertTableOid}.7.1.42",
                new SnmpV1IntegerValue(808)),
            new SnmpV1Variable(
                $"{AlertTableOid}.8.1.42",
                new SnmpV1OctetStringValue("Roll Paper End"))
        },
        new[]
        {
            new SnmpV1Variable(
                $"{AlertTableOid}.7.42",
                new SnmpV1IntegerValue(808))
        },
        new[]
        {
            new SnmpV1Variable(
                $"{AlertTableOid}.8.1.42",
                new SnmpV1IntegerValue(808))
        },
        new[]
        {
            new SnmpV1Variable(
                $"{AlertTableOid}.2.1.42",
                new SnmpV1IntegerValue(3))
        }
    };

    private static FakeSnmpV1PrinterClient ReadyClient(params SnmpV1Variable[] alerts) =>
        ReadyClient(true, alerts);

    private static FakeSnmpV1PrinterClient ReadyClient(
        bool completedSubtree,
        params SnmpV1Variable[] alerts) =>
        new(
            new Dictionary<string, SnmpV1Variable>(StringComparer.Ordinal)
            {
                [CoverStatusOid] = new(CoverStatusOid, new SnmpV1IntegerValue(4)),
                [PaperCurrentLevelOid] = new(
                    PaperCurrentLevelOid,
                    new SnmpV1IntegerValue(-3)),
                [PaperStatusOid] = new(PaperStatusOid, new SnmpV1IntegerValue(0))
            },
            new SnmpV1WalkResult(completedSubtree, alerts));

    private static PrinterProfile Profile(
        string compatibilityProfileId = EpsonTmT82xM352AConditionEvaluator.ProfileId,
        string statusCommandProfile = "snmp-printer-mib-v1") =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            "BRANCH-01",
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9100,
            PrinterProtocol.RawTcpEscPos,
            compatibilityProfileId,
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
            statusCommandProfile,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeSnmpV1PrinterClient(
        Dictionary<string, SnmpV1Variable> getResponses,
        SnmpV1WalkResult walkResult) : ISnmpV1PrinterClient
    {
        public Dictionary<string, SnmpV1Variable> GetResponses { get; } = getResponses;

        public List<string> GetOids { get; } = [];

        public string? WalkSubtreeOid { get; private set; }

        public int WalkMaximumVariables { get; private set; }

        public Task<SnmpV1Variable> GetAsync(
            PrinterProfile profile,
            string oid,
            CancellationToken cancellationToken)
        {
            GetOids.Add(oid);
            return Task.FromResult(GetResponses[oid]);
        }

        public Task<SnmpV1WalkResult> WalkAsync(
            PrinterProfile profile,
            string subtreeOid,
            int maximumVariables,
            CancellationToken cancellationToken)
        {
            WalkSubtreeOid = subtreeOid;
            WalkMaximumVariables = maximumVariables;
            return Task.FromResult(walkResult);
        }
    }
}
