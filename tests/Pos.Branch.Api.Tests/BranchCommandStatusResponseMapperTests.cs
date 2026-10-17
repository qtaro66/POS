using System.Text.Json;
using Pos.Application.TableSessions;
using Pos.Branch.Api.Commands;
using Pos.Contracts.Commands;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCommandStatusResponseMapperTests
{
    [Fact]
    public void Accepted_receipt_maps_to_versioned_public_response()
    {
        var receivedAt = DateTimeOffset.Parse("2026-10-17T12:00:01Z");
        var response = BranchCommandStatusResponseMapper.Map(
            "BRANCH-01",
            "REQ-01",
            Receipt(
                BranchCommandOutcome.BranchAccepted,
                TableSessionStatus.Ordering,
                4,
                null,
                receivedAt,
                18));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("REQ-01", response.RequestId);
        Assert.Equal(BranchCommandStatusValues.BranchAccepted, response.Status);
        Assert.Equal("SESSION-01", response.TableSessionId);
        Assert.Equal("Ordering", response.TableSessionStatus);
        Assert.Equal(4, response.TableVersion);
        Assert.Null(response.ErrorCode);
        Assert.Equal(receivedAt, response.ServerReceivedAt);
        Assert.Equal(18, response.BranchSequence);
    }

    [Fact]
    public void Rejected_receipt_preserves_safe_recovery_evidence()
    {
        var response = BranchCommandStatusResponseMapper.Map(
            "BRANCH-01",
            "REQ-01",
            Receipt(
                BranchCommandOutcome.Rejected,
                TableSessionStatus.PaymentPending,
                7,
                "InvalidTableState",
                DateTimeOffset.Parse("2026-10-17T12:00:01Z"),
                null));

        Assert.Equal(BranchCommandStatusValues.Rejected, response.Status);
        Assert.Equal("PaymentPending", response.TableSessionStatus);
        Assert.Equal(7, response.TableVersion);
        Assert.Equal("InvalidTableState", response.ErrorCode);
        Assert.Null(response.BranchSequence);
    }

    [Fact]
    public void Missing_receipt_maps_to_no_durable_result_not_rejection()
    {
        var response = BranchCommandStatusResponseMapper.Map(
            "BRANCH-01",
            "REQ-01",
            null);

        Assert.Equal(BranchCommandStatusValues.NoDurableResult, response.Status);
        Assert.Equal("REQ-01", response.RequestId);
        Assert.Null(response.TableSessionId);
        Assert.Null(response.TableSessionStatus);
        Assert.Null(response.TableVersion);
        Assert.Null(response.ErrorCode);
        Assert.Null(response.ServerReceivedAt);
        Assert.Null(response.BranchSequence);
    }

    [Theory]
    [InlineData("BRANCH-OTHER", "REQ-01")]
    [InlineData("BRANCH-01", "REQ-OTHER")]
    public void Mismatched_receipt_identity_fails_closed(
        string branchId,
        string requestId)
    {
        var receipt = Receipt(
            BranchCommandOutcome.BranchAccepted,
            TableSessionStatus.Ordering,
            1,
            null,
            DateTimeOffset.Parse("2026-10-17T12:00:01Z"),
            1);

        Assert.Throws<InvalidOperationException>(() =>
            BranchCommandStatusResponseMapper.Map(branchId, requestId, receipt));
    }

    [Fact]
    public void Public_json_omits_branch_actor_and_internal_error_details()
    {
        var response = BranchCommandStatusResponseMapper.Map(
            "BRANCH-01",
            "REQ-01",
            Receipt(
                BranchCommandOutcome.Rejected,
                null,
                null,
                "RejectedForTest",
                DateTimeOffset.Parse("2026-10-17T12:00:01Z"),
                null));
        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Rejected", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("branchId", out _));
        Assert.False(root.TryGetProperty("deviceId", out _));
        Assert.False(root.TryGetProperty("employeeId", out _));
        Assert.False(root.TryGetProperty("errorDetailsJson", out _));
    }

    private static BranchCommandReceipt Receipt(
        BranchCommandOutcome outcome,
        TableSessionStatus? sessionStatus,
        long? tableVersion,
        string? errorCode,
        DateTimeOffset serverReceivedAt,
        long? branchSequence) =>
        new(
            "REQ-01",
            "BRANCH-01",
            "DEVICE-SECRET",
            "EMPLOYEE-SECRET",
            DateTimeOffset.Parse("2026-10-17T12:00:00Z"),
            outcome,
            TableSessionId.From("SESSION-01"),
            sessionStatus,
            tableVersion,
            errorCode,
            serverReceivedAt,
            "{\"internal\":\"details\"}",
            branchSequence);
}
