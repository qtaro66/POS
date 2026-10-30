using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxSequenceReplayLeaseTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 30, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_lease_preserves_replay_and_event_identity()
    {
        var lease = Create();

        Assert.Equal("RECOVERY-01", lease.RecoveryId);
        Assert.Equal("OUTBOX-06", lease.OutboxId);
        Assert.Equal("BRANCH-01", lease.BranchId);
        Assert.Equal(6, lease.BranchSequence);
        Assert.Equal("REQUEST-06", lease.RequestId);
        Assert.Equal("SESSION-06", lease.TableSessionId.Value);
        Assert.Equal(TableSessionAction.SubmitOrder, lease.Action);
        Assert.Equal(3, lease.TableVersion);
        Assert.Equal(ClaimedAt.AddMinutes(-5), lease.CreatedAt);
        Assert.Equal(2, lease.RetryCount);
        Assert.Equal("WORKER-01", lease.LeaseOwner);
        Assert.Equal(ClaimedAt, lease.ClaimedAt);
        Assert.Equal(ClaimedAt.AddSeconds(30), lease.LeaseExpiresAt);
    }

    [Theory]
    [InlineData("recoveryId")]
    [InlineData("outboxId")]
    [InlineData("branchId")]
    [InlineData("requestId")]
    [InlineData("leaseOwner")]
    public void Empty_identifiers_are_rejected(string parameterName)
    {
        var values = ValidValues();
        values[parameterName] = " ";

        var exception = Assert.Throws<ArgumentException>(() => Create(values));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_Branch_sequence_is_rejected(long sequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(branchSequence: sequence));

        Assert.Equal("branchSequence", exception.ParamName);
    }

    [Fact]
    public void Missing_table_session_is_rejected()
    {
        var values = ValidValues();
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudOutboxSequenceReplayLease(
                values["recoveryId"],
                values["outboxId"],
                values["branchId"],
                6,
                values["requestId"],
                null!,
                TableSessionAction.SubmitOrder,
                3,
                ClaimedAt.AddMinutes(-5),
                2,
                values["leaseOwner"],
                ClaimedAt,
                ClaimedAt.AddSeconds(30)));

        Assert.Equal("tableSessionId", exception.ParamName);
    }

    [Fact]
    public void Undefined_action_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(action: (TableSessionAction)999));

        Assert.Equal("action", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_table_version_is_rejected(long tableVersion)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(tableVersion: tableVersion));

        Assert.Equal("tableVersion", exception.ParamName);
    }

    [Fact]
    public void Negative_retry_count_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(retryCount: -1));

        Assert.Equal("retryCount", exception.ParamName);
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("claimedAt")]
    [InlineData("leaseExpiresAt")]
    public void Non_UTC_timestamps_are_rejected(string parameterName)
    {
        var local = ClaimedAt.ToOffset(TimeSpan.FromHours(7));

        var exception = Assert.Throws<ArgumentException>(() =>
            Create(
                createdAt: parameterName == "createdAt" ? local : null,
                claimedAt: parameterName == "claimedAt" ? local : null,
                leaseExpiresAt: parameterName == "leaseExpiresAt" ? local : null));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_future_lease_expiry_is_rejected(int offsetSeconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(leaseExpiresAt: ClaimedAt.AddSeconds(offsetSeconds)));

        Assert.Equal("leaseExpiresAt", exception.ParamName);
    }

    private static BranchCloudOutboxSequenceReplayLease Create(
        IReadOnlyDictionary<string, string>? values = null,
        long branchSequence = 6,
        TableSessionId? tableSessionId = default,
        TableSessionAction action = TableSessionAction.SubmitOrder,
        long tableVersion = 3,
        DateTimeOffset? createdAt = null,
        int retryCount = 2,
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? leaseExpiresAt = null)
    {
        values ??= ValidValues();
        var effectiveClaimedAt = claimedAt ?? ClaimedAt;
        return new(
            values["recoveryId"],
            values["outboxId"],
            values["branchId"],
            branchSequence,
            values["requestId"],
            tableSessionId ?? TableSessionId.From("SESSION-06"),
            action,
            tableVersion,
            createdAt ?? ClaimedAt.AddMinutes(-5),
            retryCount,
            values["leaseOwner"],
            effectiveClaimedAt,
            leaseExpiresAt ?? effectiveClaimedAt.AddSeconds(30));
    }

    private static Dictionary<string, string> ValidValues() =>
        new(StringComparer.Ordinal)
        {
            ["recoveryId"] = "RECOVERY-01",
            ["outboxId"] = "OUTBOX-06",
            ["branchId"] = "BRANCH-01",
            ["requestId"] = "REQUEST-06",
            ["leaseOwner"] = "WORKER-01"
        };
}
