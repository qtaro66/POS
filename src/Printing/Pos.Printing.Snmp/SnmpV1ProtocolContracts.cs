using System.Security.Cryptography;
using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public sealed class SnmpV1CommunitySecret : IDisposable
{
    private byte[]? _bytes;

    public SnmpV1CommunitySecret(ReadOnlySpan<byte> bytes)
        : this(bytes.ToArray(), takeOwnership: true)
    {
    }

    internal SnmpV1CommunitySecret(byte[] bytes, bool takeOwnership)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                "An SNMP v1 community must contain between 1 and 255 bytes.");
        }

        _bytes = takeOwnership ? bytes : bytes.ToArray();
    }

    public int Length => GetBytes().Length;

    internal ReadOnlyMemory<byte> Bytes => GetBytes();

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private byte[] GetBytes() =>
        Volatile.Read(ref _bytes)
        ?? throw new ObjectDisposedException(nameof(SnmpV1CommunitySecret));
}

public interface ISnmpV1CommunityProvider
{
    ValueTask<SnmpV1CommunitySecret> GetCommunityAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken);
}

public interface ISnmpV1DatagramTransport
{
    Task<ReadOnlyMemory<byte>> ExchangeAsync(
        PrinterProfile profile,
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken);
}

public interface ISnmpV1RequestIdSource
{
    int Next();
}
