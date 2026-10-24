using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudOutboxLeaseTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 24, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Captures_normalized_identity_sequence_and_lease_window()
    {
        var lease = Create(
            outboxId: " OUTBOX-01 ",
            branchId: " BRANCH-01 ",
            requestId: " REQUEST-01 ",
            leaseOwner: " WORKER-01 ");

        Assert.Equal("OUTBOX-01", lease.OutboxId);
        Assert.Equal("BRANCH-01", lease.BranchId);
        Assert.Equal(7, lease.BranchSequence);
        Assert.Equal("REQUEST-01", lease.RequestId);
        Assert.Equal("SESSION-01", lease.TableSessionId.Value);
        Assert.Equal(TableSessionAction.SubmitOrder, lease.Action);
        Assert.Equal(3, lease.TableVersion);
        Assert.Equal(ClaimedAt.AddMinutes(-1), lease.CreatedAt);
        Assert.Equal(2, lease.RetryCount);
        Assert.Equal("WORKER-01", lease.LeaseOwner);
        Assert.Equal(ClaimedAt, lease.ClaimedAt);
        Assert.Equal(ClaimedAt.AddSeconds(30), lease.LeaseExpiresAt);
    }

    [Theory]
    [InlineData(null, "BRANCH-01", "REQUEST-01", "WORKER-01", "outboxId")]
    [InlineData(" ", "BRANCH-01", "REQUEST-01", "WORKER-01", "outboxId")]
    [InlineData("OUTBOX-01", null, "REQUEST-01", "WORKER-01", "branchId")]
    [InlineData("OUTBOX-01", "BRANCH-01", "", "WORKER-01", "requestId")]
    [InlineData("OUTBOX-01", "BRANCH-01", "REQUEST-01", " ", "leaseOwner")]
    public void Empty_identity_is_rejected(
        string? outboxId,
        string? branchId,
        string? requestId,
        string? leaseOwner,
        string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(
            outboxId: outboxId!,
            branchId: branchId!,
            requestId: requestId!,
            leaseOwner: leaseOwner!));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_branch_sequence_is_rejected(long branchSequence)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(branchSequence: branchSequence));

        Assert.Equal("branchSequence", exception.ParamName);
    }

    [Fact]
    public void Missing_table_session_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudOutboxLease(
                "OUTBOX-01",
                "BRANCH-01",
                7,
                "REQUEST-01",
                null!,
                TableSessionAction.SubmitOrder,
                3,
                ClaimedAt.AddMinutes(-1),
                2,
                "WORKER-01",
                ClaimedAt,
                ClaimedAt.AddSeconds(30)));

        Assert.Equal("tableSessionId", exception.ParamName);
    }

    [Fact]
    public void Undefined_action_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(action: (TableSessionAction)int.MaxValue));

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
    public void Non_utc_timestamp_is_rejected(string field)
    {
        var nonUtc = ClaimedAt.ToOffset(TimeSpan.FromHours(7));

        var exception = Assert.Throws<ArgumentException>(() => field switch
        {
            "createdAt" => Create(createdAt: nonUtc),
            "claimedAt" => Create(claimedAt: nonUtc),
            _ => Create(leaseExpiresAt: nonUtc)
        });

        Assert.Equal(field, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_future_lease_expiry_is_rejected(int secondsFromClaim)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Create(leaseExpiresAt: ClaimedAt.AddSeconds(secondsFromClaim)));

        Assert.Equal("leaseExpiresAt", exception.ParamName);
    }

    private static BranchCloudOutboxLease Create(
        string outboxId = "OUTBOX-01",
        string branchId = "BRANCH-01",
        long branchSequence = 7,
        string requestId = "REQUEST-01",
        TableSessionId? tableSessionId = null,
        TableSessionAction action = TableSessionAction.SubmitOrder,
        long tableVersion = 3,
        DateTimeOffset? createdAt = null,
        int retryCount = 2,
        string leaseOwner = "WORKER-01",
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? leaseExpiresAt = null) =>
        new(
            outboxId,
            branchId,
            branchSequence,
            requestId,
            tableSessionId ?? TableSessionId.From("SESSION-01"),
            action,
            tableVersion,
            createdAt ?? ClaimedAt.AddMinutes(-1),
            retryCount,
            leaseOwner,
            claimedAt ?? ClaimedAt,
            leaseExpiresAt ?? ClaimedAt.AddSeconds(30));
}
