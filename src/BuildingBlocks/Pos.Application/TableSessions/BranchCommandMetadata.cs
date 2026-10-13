using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public sealed record BranchCommandMetadata
{
    public BranchCommandMetadata(
        string requestId,
        string branchId,
        string deviceId,
        string employeeId,
        TableSessionId tableSessionId,
        DateTimeOffset clientCreatedAt)
    {
        RequestId = Require(requestId, nameof(requestId));
        BranchId = Require(branchId, nameof(branchId));
        DeviceId = Require(deviceId, nameof(deviceId));
        EmployeeId = Require(employeeId, nameof(employeeId));
        TableSessionId = tableSessionId ?? throw new ArgumentNullException(nameof(tableSessionId));
        ClientCreatedAt = clientCreatedAt;
    }

    public string RequestId { get; }

    public string BranchId { get; }

    public string DeviceId { get; }

    public string EmployeeId { get; }

    public TableSessionId TableSessionId { get; }

    public DateTimeOffset ClientCreatedAt { get; }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
