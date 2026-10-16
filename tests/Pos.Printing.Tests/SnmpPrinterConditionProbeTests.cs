using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Printing.Tests;

public sealed class SnmpPrinterConditionProbeTests
{
    [Fact]
    public async Task Unsupported_status_mode_does_not_read_network_snapshot()
    {
        var reader = new FakeReader(ReadySnapshot());
        var probe = Probe(reader);

        var condition = await probe.GetConditionAsync(
            Profile(PrinterStatusMode.Unsupported, statusCommandProfile: null),
            CancellationToken.None);

        Assert.Equal(PrinterCondition.StatusUnsupported, condition);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Unknown_compatibility_profile_fails_closed_without_reading()
    {
        var reader = new FakeReader(ReadySnapshot());
        var probe = Probe(reader);

        var condition = await probe.GetConditionAsync(
            Profile(compatibilityProfileId: "unverified-printer-v1"),
            CancellationToken.None);

        Assert.Equal(PrinterCondition.Unknown, condition);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task Verified_closed_cover_present_paper_and_empty_alert_table_is_ready()
    {
        var reader = new FakeReader(ReadySnapshot());

        var condition = await Probe(reader).GetConditionAsync(Profile(), CancellationToken.None);

        Assert.Equal(PrinterCondition.Ready, condition);
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public async Task Verified_cover_open_value_blocks_as_cover_open()
    {
        var snapshot = new PrinterMibSnapshot(3, -3, 0, true, []);

        var condition = await Probe(new FakeReader(snapshot))
            .GetConditionAsync(Profile(), CancellationToken.None);

        Assert.Equal(PrinterCondition.CoverOpen, condition);
    }

    [Theory]
    [InlineData(0, 19)]
    [InlineData(0, 19, 3)]
    public async Task Verified_paper_values_block_as_paper_out(
        int currentLevel,
        int status,
        int coverStatus = 4)
    {
        var snapshot = new PrinterMibSnapshot(coverStatus, currentLevel, status, true, []);

        var condition = await Probe(new FakeReader(snapshot))
            .GetConditionAsync(Profile(), CancellationToken.None);

        var expected = coverStatus == 3
            ? PrinterCondition.CoverOpen
            : PrinterCondition.PaperOut;
        Assert.Equal(expected, condition);
    }

    [Fact]
    public async Task Exact_active_alert_808_blocks_as_paper_out()
    {
        var snapshot = new PrinterMibSnapshot(
            4,
            -3,
            0,
            true,
            [new PrinterMibAlert(808, "Roll Paper End")]);

        var condition = await Probe(new FakeReader(snapshot))
            .GetConditionAsync(Profile(), CancellationToken.None);

        Assert.Equal(PrinterCondition.PaperOut, condition);
    }

    [Theory]
    [MemberData(nameof(UnverifiedSnapshots))]
    public async Task Unverified_or_conflicting_values_fail_closed_as_unknown(
        PrinterMibSnapshot snapshot)
    {
        var condition = await Probe(new FakeReader(snapshot))
            .GetConditionAsync(Profile(), CancellationToken.None);

        Assert.Equal(PrinterCondition.Unknown, condition);
    }

    [Fact]
    public async Task Reader_failure_is_not_converted_to_a_supported_condition()
    {
        var reader = new FakeReader(new IOException("SNMP unavailable"));

        await Assert.ThrowsAsync<IOException>(() =>
            Probe(reader).GetConditionAsync(Profile(), CancellationToken.None));
    }

    [Fact]
    public void Snapshot_owns_its_alert_collection()
    {
        var alerts = new[] { new PrinterMibAlert(808, "Roll Paper End") };
        var snapshot = new PrinterMibSnapshot(4, -3, 0, true, alerts);

        alerts[0] = new PrinterMibAlert(999, "changed");
        var exposed = Assert.IsType<PrinterMibAlert[]>(snapshot.ActiveAlerts);
        exposed[0] = new PrinterMibAlert(999, "changed again");

        Assert.Equal(808, snapshot.ActiveAlerts[0].Code);
    }

    public static TheoryData<PrinterMibSnapshot> UnverifiedSnapshots => new()
    {
        new PrinterMibSnapshot(4, -3, 0, false, []),
        new PrinterMibSnapshot(2, -3, 0, true, []),
        new PrinterMibSnapshot(4, 25, 0, true, []),
        new PrinterMibSnapshot(4, -3, 19, true, []),
        new PrinterMibSnapshot(4, -3, 0, true, [new PrinterMibAlert(808, "different")]),
        new PrinterMibSnapshot(4, -3, 0, true, [new PrinterMibAlert(999, "unmapped")])
    };

    private static SnmpPrinterConditionProbe Probe(FakeReader reader) =>
        new(reader, [new EpsonTmT82xM352AConditionEvaluator()]);

    private static PrinterMibSnapshot ReadySnapshot() =>
        new(4, -3, 0, true, []);

    private static PrinterProfile Profile(
        PrinterStatusMode statusMode = PrinterStatusMode.SnmpPrinterMib,
        string compatibilityProfileId = EpsonTmT82xM352AConditionEvaluator.ProfileId,
        string? statusCommandProfile = "snmp-printer-mib-v1") =>
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
            statusMode,
            false,
            "1D5601",
            null,
            statusCommandProfile,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeReader : IPrinterMibSnapshotReader
    {
        private readonly PrinterMibSnapshot? _snapshot;
        private readonly Exception? _failure;

        public FakeReader(PrinterMibSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public FakeReader(Exception failure)
        {
            _failure = failure;
        }

        public int ReadCount { get; private set; }

        public Task<PrinterMibSnapshot> ReadAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return _failure is null
                ? Task.FromResult(_snapshot!)
                : Task.FromException<PrinterMibSnapshot>(_failure);
        }
    }
}
