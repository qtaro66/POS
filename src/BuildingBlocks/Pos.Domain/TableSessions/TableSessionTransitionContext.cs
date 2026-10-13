namespace Pos.Domain.TableSessions;

public sealed record TableSessionTransitionContext
{
    public TableSessionTransitionContext(
        string employeeId,
        string deviceId,
        DateTimeOffset occurredAt,
        string? reason = null)
    {
        EmployeeId = RequireValue(employeeId, nameof(employeeId));
        DeviceId = RequireValue(deviceId, nameof(deviceId));
        OccurredAt = occurredAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public string EmployeeId { get; }

    public string DeviceId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? Reason { get; }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
