using Pos.Printing.Abstractions;
using Pos.Printing.EscPosTcp;

namespace Pos.Printing.Tests;

public sealed class RawTcpPrintTransportAdapterTests
{
    [Fact]
    public async Task Send_connects_then_journals_first_byte_then_writes_exact_payload()
    {
        var events = new List<string>();
        var connection = new FakeConnection(events);
        var factory = new FakeConnectionFactory(connection, events);
        var adapter = new RawTcpPrintTransportAdapter(factory);
        var payload = new RenderedPrintPayload("DOC-01", [0x1B, 0x40, 0x0A]);

        await adapter.SendAsync(
            Profile(),
            payload,
            new RecordingObserver(events),
            CancellationToken.None);

        Assert.Equal(["connect", "first-byte", "write"], events);
        Assert.Equal("printer-hot.branch.test", factory.Host);
        Assert.Equal(9100, factory.Port);
        Assert.Equal(TimeSpan.FromSeconds(2), factory.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), connection.Timeout);
        Assert.Equal(payload.Bytes.ToArray(), connection.Payload);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Connection_failure_does_not_cross_first_byte_boundary()
    {
        var events = new List<string>();
        var factory = new FakeConnectionFactory(
            new IOException("connect failed"),
            events);
        var adapter = new RawTcpPrintTransportAdapter(factory);

        await Assert.ThrowsAsync<IOException>(() => adapter.SendAsync(
            Profile(),
            Payload(),
            new RecordingObserver(events),
            CancellationToken.None));

        Assert.Equal(["connect"], events);
    }

    [Fact]
    public async Task First_byte_journal_failure_prevents_payload_write_and_disposes_connection()
    {
        var events = new List<string>();
        var connection = new FakeConnection(events);
        var adapter = new RawTcpPrintTransportAdapter(
            new FakeConnectionFactory(connection, events));

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.SendAsync(
            Profile(),
            Payload(),
            new ThrowingObserver(events),
            CancellationToken.None));

        Assert.Equal(["connect", "first-byte"], events);
        Assert.Empty(connection.Payload);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Send_failure_occurs_after_first_byte_boundary_and_disposes_connection()
    {
        var events = new List<string>();
        var connection = new FakeConnection(events)
        {
            Failure = new IOException("send failed")
        };
        var adapter = new RawTcpPrintTransportAdapter(
            new FakeConnectionFactory(connection, events));

        await Assert.ThrowsAsync<IOException>(() => adapter.SendAsync(
            Profile(),
            Payload(),
            new RecordingObserver(events),
            CancellationToken.None));

        Assert.Equal(["connect", "first-byte", "write"], events);
        Assert.True(connection.Disposed);
    }

    [Fact]
    public async Task Adapter_rejects_a_non_raw_tcp_profile_before_connecting()
    {
        var events = new List<string>();
        var connection = new FakeConnection(events);
        var adapter = new RawTcpPrintTransportAdapter(
            new FakeConnectionFactory(connection, events));

        await Assert.ThrowsAsync<ArgumentException>(() => adapter.SendAsync(
            Profile(PrinterProtocol.Ipp),
            Payload(),
            new RecordingObserver(events),
            CancellationToken.None));

        Assert.Empty(events);
        Assert.False(connection.Disposed);
    }

    [Fact]
    public async Task Timeout_helper_maps_its_own_cancellation_to_timeout_exception()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            SystemRawTcpConnectionFactory.RunWithTimeoutAsync(
                token => Task.Delay(Timeout.InfiniteTimeSpan, token),
                TimeSpan.FromMilliseconds(10),
                CancellationToken.None,
                "timed out"));
    }

    [Fact]
    public async Task Timeout_helper_preserves_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SystemRawTcpConnectionFactory.RunWithTimeoutAsync(
                token => Task.Delay(Timeout.InfiniteTimeSpan, token),
                TimeSpan.FromMinutes(1),
                cancellation.Token,
                "timed out"));
    }

    private static RenderedPrintPayload Payload() =>
        new("DOC-01", [0x1B, 0x40, 0x0A]);

    private static PrinterProfile Profile(
        PrinterProtocol protocol = PrinterProtocol.RawTcpEscPos) =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            "BRANCH-01",
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9100,
            protocol,
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
            PrinterStatusMode.Unsupported,
            false,
            "1D5601",
            null,
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeConnectionFactory : IRawTcpConnectionFactory
    {
        private readonly IRawTcpConnection? _connection;
        private readonly Exception? _failure;
        private readonly List<string> _events;

        public FakeConnectionFactory(IRawTcpConnection connection, List<string> events)
        {
            _connection = connection;
            _events = events;
        }

        public FakeConnectionFactory(Exception failure, List<string> events)
        {
            _failure = failure;
            _events = events;
        }

        public string? Host { get; private set; }

        public int Port { get; private set; }

        public TimeSpan Timeout { get; private set; }

        public Task<IRawTcpConnection> ConnectAsync(
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            _events.Add("connect");
            Host = host;
            Port = port;
            Timeout = timeout;
            return _failure is null
                ? Task.FromResult(_connection!)
                : Task.FromException<IRawTcpConnection>(_failure);
        }
    }

    private sealed class FakeConnection(List<string> events) : IRawTcpConnection
    {
        public Exception? Failure { get; init; }

        public byte[] Payload { get; private set; } = [];

        public TimeSpan Timeout { get; private set; }

        public bool Disposed { get; private set; }

        public Task WriteAsync(
            ReadOnlyMemory<byte> payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            events.Add("write");
            Payload = payload.ToArray();
            Timeout = timeout;
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingObserver(List<string> events) : IPrintSendObserver
    {
        public Task OnFirstByteSendingAsync(CancellationToken cancellationToken)
        {
            events.Add("first-byte");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver(List<string> events) : IPrintSendObserver
    {
        public Task OnFirstByteSendingAsync(CancellationToken cancellationToken)
        {
            events.Add("first-byte");
            return Task.FromException(new InvalidOperationException("journal failed"));
        }
    }
}
