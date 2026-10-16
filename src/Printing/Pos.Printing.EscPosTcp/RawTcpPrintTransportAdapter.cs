using System.Net.Sockets;
using Pos.Printing.Abstractions;

namespace Pos.Printing.EscPosTcp;

public sealed class RawTcpPrintTransportAdapter : IPrintTransportAdapter
{
    private readonly IRawTcpConnectionFactory _connectionFactory;

    public RawTcpPrintTransportAdapter()
        : this(new SystemRawTcpConnectionFactory())
    {
    }

    internal RawTcpPrintTransportAdapter(IRawTcpConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public PrinterProtocol Protocol => PrinterProtocol.RawTcpEscPos;

    public async Task SendAsync(
        PrinterProfile profile,
        RenderedPrintPayload payload,
        IPrintSendObserver observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(observer);
        if (profile.Protocol is not PrinterProtocol.RawTcpEscPos)
        {
            throw new ArgumentException(
                "The printer profile is not configured for RAW TCP ESC/POS.",
                nameof(profile));
        }

        await using var connection = await _connectionFactory.ConnectAsync(
            profile.Host,
            profile.Port,
            profile.ConnectTimeout,
            cancellationToken);

        // This is the durable safety boundary. The observer must finish before
        // the connection is authorized to write any payload byte.
        await observer.OnFirstByteSendingAsync(cancellationToken);
        await connection.WriteAsync(
            payload.Bytes,
            profile.SendTimeout,
            cancellationToken);
    }
}

internal interface IRawTcpConnectionFactory
{
    Task<IRawTcpConnection> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface IRawTcpConnection : IAsyncDisposable
{
    Task WriteAsync(
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class SystemRawTcpConnectionFactory : IRawTcpConnectionFactory
{
    public async Task<IRawTcpConnection> ConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await RunWithTimeoutAsync(
                token => client.ConnectAsync(host, port, token).AsTask(),
                timeout,
                cancellationToken,
                "RAW TCP connection timed out.");
            return new SystemRawTcpConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    internal static async Task RunWithTimeoutAsync(
        Func<CancellationToken, Task> operation,
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
            await operation(linkedSource.Token);
        }
        catch (OperationCanceledException exception)
            when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage, exception);
        }
    }
}

internal sealed class SystemRawTcpConnection(TcpClient client) : IRawTcpConnection
{
    private readonly TcpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task WriteAsync(
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        SystemRawTcpConnectionFactory.RunWithTimeoutAsync(
            async token =>
            {
                var stream = _client.GetStream();
                await stream.WriteAsync(payload, token);
                await stream.FlushAsync(token);
            },
            timeout,
            cancellationToken,
            "RAW TCP send timed out.");

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
