using Pos.Printing.Abstractions;

namespace Pos.Printing.Snmp;

public abstract record SnmpV1Value;

public sealed record SnmpV1IntegerValue(int Value) : SnmpV1Value;

public sealed record SnmpV1OctetStringValue : SnmpV1Value
{
    public SnmpV1OctetStringValue(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Value { get; }
}

public sealed record SnmpV1RawValue : SnmpV1Value
{
    private readonly byte[] _bytes;

    public SnmpV1RawValue(byte tag, ReadOnlySpan<byte> bytes)
    {
        if ((tag & 0x20) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tag), "A raw value must be primitive.");
        }

        Tag = tag;
        _bytes = bytes.ToArray();
    }

    public byte Tag { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
}

public sealed record SnmpV1Variable
{
    public SnmpV1Variable(string oid, SnmpV1Value value)
    {
        Oid = SnmpObjectIdentifier.Normalize(oid);
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Oid { get; }

    public SnmpV1Value Value { get; }

}

public sealed record SnmpV1WalkResult
{
    private readonly SnmpV1Variable[] _variables;

    public SnmpV1WalkResult(bool completedSubtree, IEnumerable<SnmpV1Variable> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        _variables = variables.ToArray();
        if (_variables.Any(variable => variable is null))
        {
            throw new ArgumentException("The variable collection cannot contain null.", nameof(variables));
        }

        CompletedSubtree = completedSubtree;
    }

    public bool CompletedSubtree { get; }

    public IReadOnlyList<SnmpV1Variable> Variables => _variables.ToArray();
}

public interface ISnmpV1PrinterClient
{
    Task<SnmpV1Variable> GetAsync(
        PrinterProfile profile,
        string oid,
        CancellationToken cancellationToken);

    Task<SnmpV1WalkResult> WalkAsync(
        PrinterProfile profile,
        string subtreeOid,
        int maximumVariables,
        CancellationToken cancellationToken);
}
