using System.Text.Json;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusResponseMapperTests
{
    [Fact]
    public void Successful_send_status_maps_to_versioned_utc_response()
    {
        var sentAt = new DateTimeOffset(
            2026,
            10,
            23,
            12,
            15,
            0,
            TimeSpan.Zero);

        var response = BranchCloudDataSentStatusResponseMapper.Map(
            "BRANCH-01",
            new BranchCloudDataSentStatus("BRANCH-01", sentAt));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal(sentAt, response.LastSentAt);
        Assert.Equal(TimeSpan.Zero, response.LastSentAt?.Offset);
    }

    [Fact]
    public void Missing_status_maps_to_never_sent_response()
    {
        var response = BranchCloudDataSentStatusResponseMapper.Map(
            "BRANCH-01",
            null);

        Assert.Equal(1, response.SchemaVersion);
        Assert.Null(response.LastSentAt);
    }

    [Fact]
    public void Mismatched_branch_fails_closed()
    {
        var status = new BranchCloudDataSentStatus(
            "BRANCH-02",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            BranchCloudDataSentStatusResponseMapper.Map(
                "BRANCH-01",
                status));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_branch_identifier_is_rejected(string branchId)
    {
        Assert.Throws<ArgumentException>(() =>
            BranchCloudDataSentStatusResponseMapper.Map(branchId, null));
    }

    [Fact]
    public void Public_json_omits_branch_outbox_device_and_employee_identity()
    {
        var response = BranchCloudDataSentStatusResponseMapper.Map(
            "BRANCH-01",
            new BranchCloudDataSentStatus(
                "BRANCH-01",
                new DateTimeOffset(2026, 10, 23, 12, 15, 0, TimeSpan.Zero)));
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "2026-10-23T12:15:00+00:00",
            root.GetProperty("lastSentAt").GetString());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("outboxId", out _));
        Assert.False(root.TryGetProperty("deviceId", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
    }
}
