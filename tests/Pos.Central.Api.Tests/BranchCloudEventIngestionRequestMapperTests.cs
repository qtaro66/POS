using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionRequestMapperTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Versioned_request_maps_to_immutable_envelope()
    {
        var envelope = BranchCloudEventIngestionRequestMapper.Map(Request());

        Assert.Equal("EVENT-01", envelope.EventId);
        Assert.Equal("BRANCH-01", envelope.BranchId);
        Assert.Equal(7, envelope.BranchSequence);
        Assert.Equal("REQUEST-01", envelope.RequestId);
        Assert.Equal("SESSION-01", envelope.TableSessionId.Value);
        Assert.Equal(TableSessionAction.SubmitOrder, envelope.Action);
        Assert.Equal(3, envelope.TableVersion);
        Assert.Equal(OccurredAt, envelope.OccurredAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unsupported_schema_version_is_rejected(int schemaVersion)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BranchCloudEventIngestionRequestMapper.Map(
                Request(schemaVersion: schemaVersion)));

        Assert.Equal("request", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("submitorder")]
    [InlineData("1")]
    [InlineData("UnknownAction")]
    public void Undefined_or_noncanonical_action_is_rejected(string action)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BranchCloudEventIngestionRequestMapper.Map(Request(action: action)));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void Non_utc_timestamp_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BranchCloudEventIngestionRequestMapper.Map(Request(
                occurredAt: OccurredAt.ToOffset(TimeSpan.FromHours(7)))));

        Assert.Equal("occurredAt", exception.ParamName);
    }

    [Theory]
    [InlineData("", "BRANCH-01", 7, "REQUEST-01", "SESSION-01", 3)]
    [InlineData("EVENT-01", "", 7, "REQUEST-01", "SESSION-01", 3)]
    [InlineData("EVENT-01", "BRANCH-01", 0, "REQUEST-01", "SESSION-01", 3)]
    [InlineData("EVENT-01", "BRANCH-01", 7, "", "SESSION-01", 3)]
    [InlineData("EVENT-01", "BRANCH-01", 7, "REQUEST-01", "", 3)]
    [InlineData("EVENT-01", "BRANCH-01", 7, "REQUEST-01", "SESSION-01", 0)]
    public void Invalid_event_fields_are_rejected(
        string eventId,
        string branchId,
        long branchSequence,
        string requestId,
        string tableSessionId,
        long tableVersion)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            BranchCloudEventIngestionRequestMapper.Map(new(
                1,
                eventId,
                branchId,
                branchSequence,
                requestId,
                tableSessionId,
                nameof(TableSessionAction.SubmitOrder),
                tableVersion,
                OccurredAt)));
    }

    [Fact]
    public void Missing_request_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudEventIngestionRequestMapper.Map(null!));
    }

    private static BranchCloudEventIngestionRequest Request(
        int schemaVersion = 1,
        string action = nameof(TableSessionAction.SubmitOrder),
        DateTimeOffset? occurredAt = null) =>
        new(
            schemaVersion,
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            "SESSION-01",
            action,
            3,
            occurredAt ?? OccurredAt);
}
