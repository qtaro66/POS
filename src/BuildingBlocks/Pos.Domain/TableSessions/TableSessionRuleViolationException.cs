namespace Pos.Domain.TableSessions;

public sealed class TableSessionRuleViolationException : InvalidOperationException
{
    public TableSessionRuleViolationException(string rule, string message)
        : base(message)
    {
        Rule = rule;
    }

    public string Rule { get; }
}
