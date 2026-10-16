using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public interface ISnmpV1CommunityProvider
{
    ValueTask<ReadOnlyMemory<byte>> GetCommunityAsync(
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
