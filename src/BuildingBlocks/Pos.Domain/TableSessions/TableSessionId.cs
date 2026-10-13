namespace Pos.Domain.TableSessions;

public sealed record TableSessionId
{
    private TableSessionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static TableSessionId From(string value)
    {
        return new TableSessionId(RequireValue(value, nameof(value)));
    }

    public override string ToString() => Value;

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The identifier cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
