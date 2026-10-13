namespace Pos.Domain.TableSessions;

public sealed record TableId
{
    private TableId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static TableId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The table identifier cannot be empty.", nameof(value));
        }

        return new TableId(value.Trim());
    }

    public override string ToString() => Value;
}
