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

public sealed record SnmpV1Variable
{
    public SnmpV1Variable(string oid, SnmpV1Value value)
    {
        Oid = RequireOid(oid);
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Oid { get; }

    public SnmpV1Value Value { get; }

    private static string RequireOid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The OID cannot be empty.", nameof(value));
        }

        var normalized = value.Trim().Trim('.');
        var components = normalized.Split('.');
        if (components.Length < 2 ||
            components.Any(component =>
                component.Length == 0 ||
                component.Any(character => character is < '0' or > '9')))
        {
            throw new ArgumentException("The OID must contain decimal components.", nameof(value));
        }

        return normalized;
    }
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
