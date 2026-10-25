using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxEventEnvelopeTests
{
    [Fact]
    public void From_lease_projects_only_durable_event_data()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            10,
            25,
            4,
            50,
            0,
            TimeSpan.Zero);
        var lease = new BranchCloudOutboxLease(
            "event-001",
            "branch-001",
            42,
            "request-001",
            TableSessionId.From("session-001"),
            TableSessionAction.SubmitOrder,
            7,
            occurredAt,
            3,
            "worker-001",
            occurredAt.AddMinutes(1),
            occurredAt.AddMinutes(2));

        var envelope = BranchCloudOutboxEventEnvelope.FromLease(lease);

        Assert.Equal("event-001", envelope.EventId);
        Assert.Equal("branch-001", envelope.BranchId);
        Assert.Equal(42, envelope.BranchSequence);
        Assert.Equal("request-001", envelope.RequestId);
        Assert.Equal("session-001", envelope.TableSessionId.Value);
        Assert.Equal(TableSessionAction.SubmitOrder, envelope.Action);
        Assert.Equal(7, envelope.TableVersion);
        Assert.Equal(occurredAt, envelope.OccurredAt);
        Assert.DoesNotContain(
            envelope.GetType().GetProperties(),
            property => property.Name is
                nameof(BranchCloudOutboxLease.RetryCount) or
                nameof(BranchCloudOutboxLease.LeaseOwner) or
                nameof(BranchCloudOutboxLease.ClaimedAt) or
                nameof(BranchCloudOutboxLease.LeaseExpiresAt));
    }

    [Fact]
    public void From_lease_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudOutboxEventEnvelope.FromLease(null!));
    }

    [Fact]
    public void Create_normalizes_identifiers()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            10,
            25,
            12,
            0,
            0,
            TimeSpan.Zero);

        var envelope = BranchCloudOutboxEventEnvelope.Create(
            " EVENT-01 ",
            " BRANCH-01 ",
            7,
            " REQUEST-01 ",
            TableSessionId.From(" SESSION-01 "),
            TableSessionAction.SubmitOrder,
            3,
            occurredAt);

        Assert.Equal("EVENT-01", envelope.EventId);
        Assert.Equal("BRANCH-01", envelope.BranchId);
        Assert.Equal("REQUEST-01", envelope.RequestId);
        Assert.Equal("SESSION-01", envelope.TableSessionId.Value);
        Assert.Equal(occurredAt, envelope.OccurredAt);
    }
}
