using System.Net.Sockets;
using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public sealed record SnmpV1UdpTransportOptions
{
    public const int DefaultSnmpPort = 161;
    public const int MaximumUdpPayloadBytes = 65_507;

    public SnmpV1UdpTransportOptions(
        int port,
        TimeSpan connectTimeout,
        TimeSpan responseTimeout,
        int maximumRequestBytes = MaximumUdpPayloadBytes,
        int maximumResponseBytes = MaximumUdpPayloadBytes)
    {
        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        if (connectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectTimeout));
        }

        if (responseTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(responseTimeout));
        }

        if (maximumRequestBytes is < 1 or > MaximumUdpPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes));
        }

        if (maximumResponseBytes is < 1 or > MaximumUdpPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        Port = port;
        ConnectTimeout = connectTimeout;
        ResponseTimeout = responseTimeout;
        MaximumRequestBytes = maximumRequestBytes;
        MaximumResponseBytes = maximumResponseBytes;
    }

    public int Port { get; }

    public TimeSpan ConnectTimeout { get; }

    public TimeSpan ResponseTimeout { get; }

    public int MaximumRequestBytes { get; }

    public int MaximumResponseBytes { get; }
}

public sealed class SnmpV1UdpDatagramTransport : ISnmpV1DatagramTransport
{
    private readonly SnmpV1UdpTransportOptions _options;
    private readonly ISnmpV1UdpSessionFactory _sessionFactory;

    public SnmpV1UdpDatagramTransport(SnmpV1UdpTransportOptions options)
        : this(options, new SystemSnmpV1UdpSessionFactory())
    {
    }

    internal SnmpV1UdpDatagramTransport(
        SnmpV1UdpTransportOptions options,
        ISnmpV1UdpSessionFactory sessionFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task<ReadOnlyMemory<byte>> ExchangeAsync(
        PrinterProfile profile,
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (request.IsEmpty || request.Length > _options.MaximumRequestBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"The SNMP request must contain between 1 and {_options.MaximumRequestBytes} bytes.");
        }

        await using var session = await RunWithTimeoutAsync(
            token => _sessionFactory.ConnectAsync(profile.Host, _options.Port, token),
            _options.ConnectTimeout,
            cancellationToken,
            "SNMP UDP connection timed out.");

        return await RunWithTimeoutAsync(
            async token =>
            {
                var sent = await session.SendAsync(request, token);
                if (sent != request.Length)
                {
                    throw new IOException(
                        $"SNMP UDP send was incomplete: {sent} of {request.Length} bytes were sent.");
                }

                var response = await session.ReceiveAsync(_options.MaximumResponseBytes, token);
                if (response.IsEmpty || response.Length > _options.MaximumResponseBytes)
                {
                    throw new InvalidDataException(
                        $"The SNMP response must contain between 1 and {_options.MaximumResponseBytes} bytes.");
                }

                return (ReadOnlyMemory<byte>)response.ToArray();
            },
            _options.ResponseTimeout,
            cancellationToken,
            "SNMP UDP response timed out.");
    }

    internal static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string timeoutMessage)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            return await operation(linkedSource.Token);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage, exception);
        }
    }
}

internal interface ISnmpV1UdpSessionFactory
{
    Task<ISnmpV1UdpSession> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken);
}

internal interface ISnmpV1UdpSession : IAsyncDisposable
{
    ValueTask<int> SendAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(
        int maximumBytes,
        CancellationToken cancellationToken);
}

internal sealed class SystemSnmpV1UdpSessionFactory : ISnmpV1UdpSessionFactory
{
    public async Task<ISnmpV1UdpSession> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var client = new UdpClient();
        try
        {
            await client.Client.ConnectAsync(host, port, cancellationToken);
            return new SystemSnmpV1UdpSession(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

internal sealed class SystemSnmpV1UdpSession(UdpClient client) : ISnmpV1UdpSession
{
    private readonly UdpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public ValueTask<int> SendAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken) =>
        _client.Client.SendAsync(request, SocketFlags.None, cancellationToken);

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[checked(maximumBytes + 1)];
        var received = await _client.Client.ReceiveAsync(
            buffer,
            SocketFlags.None,
            cancellationToken);
        return buffer.AsMemory(0, received).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
