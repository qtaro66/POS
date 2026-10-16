using Pos.Printing.Abstractions;
using Pos.Printing.Snmp;

namespace Pos.Printing.Tests;

public sealed class SnmpV1UdpDatagramTransportTests
{
    [Fact]
    public async Task Exchange_uses_the_SNMP_endpoint_and_returns_a_defensive_copy()
    {
        var response = new byte[] { 0x30, 0x01, 0x00 };
        var session = new FakeSession(response);
        var factory = new FakeSessionFactory(session);
        var transport = Transport(factory);
        var request = new byte[] { 0x30, 0x02, 0x01, 0x00 };

        var result = await transport.ExchangeAsync(Profile(), request, CancellationToken.None);
        response[0] = 0xFF;

        Assert.Equal("printer-hot.branch.test", factory.Host);
        Assert.Equal(161, factory.Port);
        Assert.Equal(request, session.Request);
        Assert.Equal(new byte[] { 0x30, 0x01, 0x00 }, result.ToArray());
        Assert.Equal(4_096, session.MaximumResponseBytes);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Exchange_rejects_an_incomplete_send_without_receiving_or_retrying()
    {
        var session = new FakeSession([0x30]) { SentByteCount = 1 };
        var factory = new FakeSessionFactory(session);

        await Assert.ThrowsAsync<IOException>(() => Transport(factory).ExchangeAsync(
            Profile(),
            new byte[] { 0x30, 0x01 },
            CancellationToken.None));

        Assert.Equal(1, factory.CallCount);
        Assert.Equal(0, session.ReceiveCallCount);
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_097)]
    public async Task Exchange_rejects_empty_or_oversized_responses(int responseLength)
    {
        var session = new FakeSession(new byte[responseLength]);

        await Assert.ThrowsAsync<InvalidDataException>(() => Transport(
            new FakeSessionFactory(session)).ExchangeAsync(
                Profile(),
                new byte[] { 0x30 },
                CancellationToken.None));

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Exchange_rejects_invalid_requests_before_connecting()
    {
        var factory = new FakeSessionFactory(new FakeSession([0x30]));
        var transport = Transport(factory);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ExchangeAsync(
            Profile(),
            ReadOnlyMemory<byte>.Empty,
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ExchangeAsync(
            Profile(),
            new byte[4_097],
            CancellationToken.None));

        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task Connect_timeout_is_reported_without_retrying()
    {
        var factory = new FakeSessionFactory(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Transport(
            factory,
            connectTimeout: TimeSpan.FromMilliseconds(10)).ExchangeAsync(
                Profile(),
                new byte[] { 0x30 },
                CancellationToken.None));

        Assert.Contains("connection", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Response_timeout_disposes_the_session_without_retrying()
    {
        var session = new FakeSession([0x30]) { BlockReceive = true };
        var factory = new FakeSessionFactory(session);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => Transport(
            factory,
            responseTimeout: TimeSpan.FromMilliseconds(10)).ExchangeAsync(
                Profile(),
                new byte[] { 0x30 },
                CancellationToken.None));

        Assert.Contains("response", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, factory.CallCount);
        Assert.Equal(1, session.SendCallCount);
        Assert.Equal(1, session.ReceiveCallCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_changed_into_a_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var factory = new FakeSessionFactory(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport(factory).ExchangeAsync(
            Profile(),
            new byte[] { 0x30 },
            cancellation.Token));

        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task Send_failure_is_not_retried_and_disposes_the_session()
    {
        var session = new FakeSession([0x30])
        {
            SendFailure = new IOException("simulated UDP failure")
        };
        var factory = new FakeSessionFactory(session);

        await Assert.ThrowsAsync<IOException>(() => Transport(factory).ExchangeAsync(
            Profile(),
            new byte[] { 0x30 },
            CancellationToken.None));

        Assert.Equal(1, factory.CallCount);
        Assert.Equal(1, session.SendCallCount);
        Assert.Equal(0, session.ReceiveCallCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void Options_validate_endpoint_timeouts_and_datagram_bounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(connectTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(responseTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(maximumRequestBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Options(maximumResponseBytes: 65_508));
    }

    private static SnmpV1UdpDatagramTransport Transport(
        ISnmpV1UdpSessionFactory factory,
        TimeSpan? connectTimeout = null,
        TimeSpan? responseTimeout = null) =>
        new(
            Options(
                connectTimeout: connectTimeout ?? TimeSpan.FromSeconds(2),
                responseTimeout: responseTimeout ?? TimeSpan.FromSeconds(2)),
            factory);

    private static SnmpV1UdpTransportOptions Options(
        int port = SnmpV1UdpTransportOptions.DefaultSnmpPort,
        TimeSpan? connectTimeout = null,
        TimeSpan? responseTimeout = null,
        int maximumRequestBytes = 4_096,
        int maximumResponseBytes = 4_096) =>
        new(
            port,
            connectTimeout ?? TimeSpan.FromSeconds(2),
            responseTimeout ?? TimeSpan.FromSeconds(2),
            maximumRequestBytes,
            maximumResponseBytes);

    private static PrinterProfile Profile() =>
        new(
            "PRINTER-01",
            "ครัวร้อน",
            "BRANCH-01",
            "KITCHEN-HOT",
            "printer-hot.branch.test",
            9_100,
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
            false,
            "1D5601",
            null,
            "epson-tm-t82x-m352a-v1",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            3,
            TimeSpan.FromSeconds(1));

    private sealed class FakeSessionFactory : ISnmpV1UdpSessionFactory
    {
        private readonly ISnmpV1UdpSession? _session;
        private readonly Func<CancellationToken, Task<ISnmpV1UdpSession>>? _connect;

        public FakeSessionFactory(ISnmpV1UdpSession session)
        {
            _session = session;
        }

        public FakeSessionFactory(Func<CancellationToken, Task<ISnmpV1UdpSession>> connect)
        {
            _connect = connect;
        }

        public int CallCount { get; private set; }

        public string? Host { get; private set; }

        public int Port { get; private set; }

        public Task<ISnmpV1UdpSession> ConnectAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Host = host;
            Port = port;
            return _connect is null
                ? Task.FromResult(_session!)
                : _connect(cancellationToken);
        }
    }

    private sealed class FakeSession(byte[] response) : ISnmpV1UdpSession
    {
        private readonly byte[] _response = response;

        public int? SentByteCount { get; init; }

        public bool BlockReceive { get; init; }

        public Exception? SendFailure { get; init; }

        public byte[] Request { get; private set; } = [];

        public int MaximumResponseBytes { get; private set; }

        public int SendCallCount { get; private set; }

        public int ReceiveCallCount { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<int> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            SendCallCount++;
            Request = request.ToArray();
            return SendFailure is null
                ? ValueTask.FromResult(SentByteCount ?? request.Length)
                : ValueTask.FromException<int>(SendFailure);
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            ReceiveCallCount++;
            MaximumResponseBytes = maximumBytes;
            if (BlockReceive)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return _response;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
