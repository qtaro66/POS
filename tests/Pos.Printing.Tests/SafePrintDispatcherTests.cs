using Pos.Printing.Abstractions;

namespace Pos.Printing.Tests;

public sealed class SafePrintDispatcherTests
{
    [Theory]
    [InlineData(PrinterCondition.Offline)]
    [InlineData(PrinterCondition.CoverOpen)]
    [InlineData(PrinterCondition.PaperOut)]
    [InlineData(PrinterCondition.CutterError)]
    [InlineData(PrinterCondition.Unknown)]
    public async Task Unsafe_preflight_condition_keeps_job_blocked_without_attempt(
        PrinterCondition condition)
    {
        var adapter = new FakeTransportAdapter();
        var dispatcher = Dispatcher(Profile(), condition, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Blocked, result.Outcome);
        Assert.False(result.AttemptStarted);
        Assert.False(result.AutomaticRetryAllowed);
        Assert.Equal(condition.ToString(), result.BlockedReason);
        Assert.Equal(0, adapter.SendCount);
    }

    [Fact]
    public async Task Missing_active_station_binding_keeps_job_blocked()
    {
        var adapter = new FakeTransportAdapter();
        var dispatcher = Dispatcher(null, PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Blocked, result.Outcome);
        Assert.Equal("NoActivePrinter", result.BlockedReason);
        Assert.Null(result.PrinterId);
        Assert.Equal(0, adapter.SendCount);
    }

    [Fact]
    public async Task Mismatched_profile_is_blocked_before_status_or_transport()
    {
        var adapter = new FakeTransportAdapter { SignalFirstByte = true };
        var mismatched = Profile("BRANCH-02");
        var dispatcher = Dispatcher(mismatched, PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Blocked, result.Outcome);
        Assert.Equal("PrinterConfigurationMismatch", result.BlockedReason);
        Assert.Equal(0, adapter.SendCount);
    }

    [Fact]
    public async Task Condition_probe_failure_is_blocked_without_starting_attempt()
    {
        var adapter = new FakeTransportAdapter { SignalFirstByte = true };
        var dispatcher = new SafePrintDispatcher(
            new StaticConfigurationProvider(Profile()),
            new ThrowingConditionProbe(),
            [adapter]);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Blocked, result.Outcome);
        Assert.Equal("ConditionProbeFailed:IOException", result.BlockedReason);
        Assert.False(result.AttemptStarted);
        Assert.Equal(0, adapter.SendCount);
    }

    [Fact]
    public async Task Connection_failure_before_first_byte_is_failed_and_retryable()
    {
        var adapter = new FakeTransportAdapter { Failure = new IOException("connect failed") };
        var dispatcher = Dispatcher(Profile(), PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Failed, result.Outcome);
        Assert.False(result.AttemptStarted);
        Assert.True(result.AutomaticRetryAllowed);
        Assert.Equal(nameof(IOException), result.ErrorCode);
    }

    [Fact]
    public async Task Profile_can_disable_automatic_retry_before_first_byte()
    {
        var adapter = new FakeTransportAdapter { Failure = new IOException("connect failed") };
        var dispatcher = Dispatcher(
            Profile(maximumAutomaticRetriesBeforeSend: 0),
            PrinterCondition.Ready,
            adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Failed, result.Outcome);
        Assert.False(result.AttemptStarted);
        Assert.False(result.AutomaticRetryAllowed);
    }

    [Fact]
    public async Task Failure_after_first_byte_is_unknown_and_never_automatically_retried()
    {
        var adapter = new FakeTransportAdapter
        {
            SignalFirstByte = true,
            Failure = new IOException("connection lost")
        };
        var dispatcher = Dispatcher(Profile(), PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Unknown, result.Outcome);
        Assert.True(result.AttemptStarted);
        Assert.False(result.AutomaticRetryAllowed);
        Assert.Equal(nameof(IOException), result.ErrorCode);
    }

    [Fact]
    public async Task First_byte_journal_failure_is_conservatively_unknown()
    {
        var adapter = new FakeTransportAdapter { SignalFirstByte = true };
        var dispatcher = Dispatcher(Profile(), PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(
            Request(),
            new ThrowingDispatchLifecycle(throwOnFirstByte: true));

        Assert.Equal(PrintDispatchOutcome.Unknown, result.Outcome);
        Assert.True(result.AttemptStarted);
        Assert.False(result.AutomaticRetryAllowed);
        Assert.Equal(nameof(InvalidOperationException), result.ErrorCode);
    }

    [Fact]
    public async Task Attempt_journal_failure_stops_before_transport_and_propagates()
    {
        var adapter = new FakeTransportAdapter { SignalFirstByte = true };
        var dispatcher = Dispatcher(Profile(), PrinterCondition.Ready, adapter);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(
                Request(),
                new ThrowingDispatchLifecycle(throwOnAttemptStart: true)));

        Assert.Equal(0, adapter.SendCount);
    }

    [Theory]
    [InlineData(PrinterCondition.Ready)]
    [InlineData(PrinterCondition.StatusUnsupported)]
    public async Task Complete_transport_send_is_only_print_dispatched(
        PrinterCondition condition)
    {
        var adapter = new FakeTransportAdapter { SignalFirstByte = true };
        var dispatcher = Dispatcher(Profile(), condition, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.PrintDispatched, result.Outcome);
        Assert.True(result.AttemptStarted);
        Assert.False(result.AutomaticRetryAllowed);
        Assert.Equal("PRINTER-01", result.PrinterId);
    }

    [Fact]
    public async Task Transport_that_omits_first_byte_signal_is_conservatively_unknown()
    {
        var adapter = new FakeTransportAdapter();
        var dispatcher = Dispatcher(Profile(), PrinterCondition.Ready, adapter);

        var result = await dispatcher.DispatchAsync(Request());

        Assert.Equal(PrintDispatchOutcome.Unknown, result.Outcome);
        Assert.False(result.AutomaticRetryAllowed);
        Assert.Equal("TransportStartNotObserved", result.ErrorCode);
    }

    [Fact]
    public void Rendered_payload_owns_bytes_and_exposes_stable_hash()
    {
        var source = new byte[] { 0x1B, 0x40, 0x0A };
        var payload = new RenderedPrintPayload("DOC-1", source);

        source[0] = 0x00;
        var exposedCopy = payload.Bytes.ToArray();
        exposedCopy[0] = 0x00;

        Assert.Equal(0x1B, payload.Bytes.Span[0]);
        Assert.Equal(64, payload.Sha256.Length);
    }

    [Fact]
    public void Profile_rejects_incomplete_hex_command()
    {
        Assert.Throws<ArgumentException>(() => Profile(cutterCommandHex: "1D5"));
    }

    [Fact]
    public void Profile_requires_drawer_capability_and_pulse_command_together()
    {
        Assert.Throws<ArgumentException>(() =>
            Profile(supportsCashDrawer: true, drawerPulseCommandHex: null));
    }

    private static SafePrintDispatcher Dispatcher(
        PrinterProfile? profile,
        PrinterCondition condition,
        FakeTransportAdapter adapter) =>
        new(
            new StaticConfigurationProvider(profile),
            new StaticConditionProbe(condition),
            [adapter]);

    private static PrintDispatchRequest Request() =>
        new(
            "PRINT-01",
            "BRANCH-01",
            "KITCHEN-HOT",
            new RenderedPrintPayload("SNAPSHOT-01", [0x1B, 0x40, 0x0A]));

    internal static PrinterProfile Profile(
        string branchId = "BRANCH-01",
        int maximumAutomaticRetriesBeforeSend = 3,
        bool supportsCashDrawer = false,
        string? drawerPulseCommandHex = null,
        string? cutterCommandHex = "1D5601") =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            branchId,
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9100,
            PrinterProtocol.RawTcpEscPos,
            "epson-tm-t82x-m352a-v1",
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
            supportsCashDrawer,
            cutterCommandHex,
            drawerPulseCommandHex,
            "snmp-printer-mib-v1",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            maximumAutomaticRetriesBeforeSend,
            TimeSpan.FromSeconds(1));

    private sealed class StaticConfigurationProvider(PrinterProfile? profile) :
        IPrinterConfigurationProvider
    {
        public Task<PrinterProfile?> FindActivePrinterAsync(
            string branchId,
            string productionStationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(profile);
    }

    private sealed class StaticConditionProbe(PrinterCondition condition) : IPrinterConditionProbe
    {
        public Task<PrinterCondition> GetConditionAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            Task.FromResult(condition);
    }

    private sealed class ThrowingConditionProbe : IPrinterConditionProbe
    {
        public Task<PrinterCondition> GetConditionAsync(
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            Task.FromException<PrinterCondition>(new IOException("probe failed"));
    }

    private sealed class ThrowingDispatchLifecycle(
        bool throwOnFirstByte = false,
        bool throwOnAttemptStart = false) : IPrintDispatchLifecycle
    {
        public Task OnTransportAttemptStartingAsync(
            PrintDispatchRequest request,
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            throwOnAttemptStart
                ? Task.FromException(new InvalidOperationException("journal unavailable"))
                : Task.CompletedTask;

        public Task OnFirstByteSendingAsync(
            PrintDispatchRequest request,
            PrinterProfile profile,
            CancellationToken cancellationToken) =>
            throwOnFirstByte
                ? Task.FromException(new InvalidOperationException("journal unavailable"))
                : Task.CompletedTask;
    }

    private sealed class FakeTransportAdapter : IPrintTransportAdapter
    {
        public PrinterProtocol Protocol => PrinterProtocol.RawTcpEscPos;

        public bool SignalFirstByte { get; init; }

        public Exception? Failure { get; init; }

        public int SendCount { get; private set; }

        public async Task SendAsync(
            PrinterProfile profile,
            RenderedPrintPayload payload,
            IPrintSendObserver observer,
            CancellationToken cancellationToken)
        {
            SendCount++;
            if (SignalFirstByte)
            {
                await observer.OnFirstByteSendingAsync(cancellationToken);
            }

            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
