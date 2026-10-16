using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public sealed class BoundedSnmpV1PrinterClient : ISnmpV1PrinterClient
{
    private const int MaximumWalkVariables = 1_024;
    private readonly ISnmpV1CommunityProvider _communityProvider;
    private readonly ISnmpV1DatagramTransport _transport;
    private readonly ISnmpV1RequestIdSource _requestIdSource;

    public BoundedSnmpV1PrinterClient(
        ISnmpV1CommunityProvider communityProvider,
        ISnmpV1DatagramTransport transport,
        ISnmpV1RequestIdSource requestIdSource)
    {
        _communityProvider = communityProvider
            ?? throw new ArgumentNullException(nameof(communityProvider));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _requestIdSource = requestIdSource
            ?? throw new ArgumentNullException(nameof(requestIdSource));
    }

    public async Task<SnmpV1Variable> GetAsync(
        PrinterProfile profile,
        string oid,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalizedOid = SnmpObjectIdentifier.Normalize(oid);
        var community = await ResolveCommunityAsync(profile, cancellationToken);
        return await ExchangeAsync(
            profile,
            SnmpV1RequestType.Get,
            normalizedOid,
            community,
            cancellationToken);
    }

    public async Task<SnmpV1WalkResult> WalkAsync(
        PrinterProfile profile,
        string subtreeOid,
        int maximumVariables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (maximumVariables is < 1 or > MaximumWalkVariables)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumVariables));
        }

        var normalizedSubtree = SnmpObjectIdentifier.Normalize(subtreeOid);
        var community = await ResolveCommunityAsync(profile, cancellationToken);
        var currentOid = normalizedSubtree;
        var variables = new List<SnmpV1Variable>();
        for (var index = 0; index < maximumVariables; index++)
        {
            var variable = await ExchangeAsync(
                profile,
                SnmpV1RequestType.GetNext,
                currentOid,
                community,
                cancellationToken);
            if (SnmpObjectIdentifier.Compare(variable.Oid, currentOid) <= 0)
            {
                throw new InvalidDataException("SNMP GETNEXT did not advance the object identifier.");
            }

            if (!SnmpObjectIdentifier.IsDescendant(variable.Oid, normalizedSubtree))
            {
                return new SnmpV1WalkResult(true, variables);
            }

            variables.Add(variable);
            currentOid = variable.Oid;
        }

        return new SnmpV1WalkResult(false, variables);
    }

    private async Task<SnmpV1Variable> ExchangeAsync(
        PrinterProfile profile,
        SnmpV1RequestType requestType,
        string oid,
        ReadOnlyMemory<byte> community,
        CancellationToken cancellationToken)
    {
        var requestId = _requestIdSource.Next();
        if (requestId <= 0)
        {
            throw new InvalidOperationException("The SNMP request ID source returned an invalid value.");
        }

        var request = SnmpV1MessageCodec.EncodeRequest(
            requestType,
            requestId,
            community.Span,
            oid);
        var response = await _transport.ExchangeAsync(profile, request, cancellationToken);
        return SnmpV1MessageCodec.DecodeResponse(response, requestId, community.Span);
    }

    private async ValueTask<ReadOnlyMemory<byte>> ResolveCommunityAsync(
        PrinterProfile profile,
        CancellationToken cancellationToken)
    {
        var resolved = await _communityProvider.GetCommunityAsync(profile, cancellationToken);
        if (resolved.IsEmpty || resolved.Length > 255)
        {
            throw new InvalidOperationException(
                "The SNMP community provider returned an invalid byte length.");
        }

        return resolved.ToArray();
    }
}
