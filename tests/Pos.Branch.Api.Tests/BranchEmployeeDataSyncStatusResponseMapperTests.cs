using System.Text.Json;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeDataSyncStatusResponseMapperTests
{
    [Fact]
    public void Synchronized_status_maps_to_versioned_utc_response()
    {
        var synchronizedAt =
            new DateTimeOffset(2026, 10, 23, 6, 0, 0, TimeSpan.Zero);

        var response = BranchEmployeeDataSyncStatusResponseMapper.Map(
            "BRANCH-01",
            new BranchEmployeeDataSyncStatus("BRANCH-01", synchronizedAt));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal(synchronizedAt, response.LastSynchronizedAt);
        Assert.Equal(TimeSpan.Zero, response.LastSynchronizedAt?.Offset);
    }

    [Fact]
    public void Missing_status_maps_to_never_synchronized_response()
    {
        var response = BranchEmployeeDataSyncStatusResponseMapper.Map(
            "BRANCH-01",
            null);

        Assert.Equal(1, response.SchemaVersion);
        Assert.Null(response.LastSynchronizedAt);
    }

    [Fact]
    public void Mismatched_branch_fails_closed()
    {
        var status = new BranchEmployeeDataSyncStatus(
            "BRANCH-02",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            BranchEmployeeDataSyncStatusResponseMapper.Map(
                "BRANCH-01",
                status));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_branch_identifier_is_rejected(string branchId)
    {
        Assert.Throws<ArgumentException>(() =>
            BranchEmployeeDataSyncStatusResponseMapper.Map(branchId, null));
    }

    [Fact]
    public void Public_json_omits_branch_device_and_employee_identity()
    {
        var response = BranchEmployeeDataSyncStatusResponseMapper.Map(
            "BRANCH-01",
            new BranchEmployeeDataSyncStatus(
                "BRANCH-01",
                new DateTimeOffset(2026, 10, 23, 6, 0, 0, TimeSpan.Zero)));
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "2026-10-23T06:00:00+00:00",
            root.GetProperty("lastSynchronizedAt").GetString());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("deviceId", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
    }
}
