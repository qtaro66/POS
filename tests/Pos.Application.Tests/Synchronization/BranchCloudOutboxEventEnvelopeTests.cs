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
    public void Replay_envelope_matches_original_durable_event_without_recovery_metadata()
    {
        var occurredAt = new DateTimeOffset(2026, 10, 30, 13, 0, 0, TimeSpan.Zero);
        var session = TableSessionId.From("SESSION-01");
        var original = new BranchCloudOutboxLease(
            "EVENT-01", "BRANCH-01", 6, "REQUEST-01", session,
            TableSessionAction.SubmitOrder, 3, occurredAt, 0, "ORIGINAL-WORKER",
            occurredAt.AddMinutes(1), occurredAt.AddMinutes(2));
        var replay = new BranchCloudOutboxSequenceReplayLease(
            "RECOVERY-01", "EVENT-01", "BRANCH-01", 6, "REQUEST-01", session,
            TableSessionAction.SubmitOrder, 3, occurredAt, 4, "REPLAY-WORKER",
            occurredAt.AddHours(1), occurredAt.AddHours(1).AddMinutes(1));

        var expected = BranchCloudOutboxEventEnvelope.FromLease(original);
        var actual = BranchCloudOutboxEventEnvelope.FromReplayLease(replay);

        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.BranchId, actual.BranchId);
        Assert.Equal(expected.BranchSequence, actual.BranchSequence);
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal(expected.TableSessionId, actual.TableSessionId);
        Assert.Equal(expected.Action, actual.Action);
        Assert.Equal(expected.TableVersion, actual.TableVersion);
        Assert.Equal(expected.OccurredAt, actual.OccurredAt);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(expected),
            System.Text.Json.JsonSerializer.Serialize(actual));
        Assert.DoesNotContain(actual.GetType().GetProperties(), property => property.Name is
            "RecoveryId" or "RetryCount" or "LeaseOwner" or "ClaimedAt" or "LeaseExpiresAt");
    }

    [Fact]
    public void Replay_envelope_rejects_missing_lease()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudOutboxEventEnvelope.FromReplayLease(null!));
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
