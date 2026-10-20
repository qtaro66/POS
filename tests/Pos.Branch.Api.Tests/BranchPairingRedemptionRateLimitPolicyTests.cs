using System.Net;
using Microsoft.AspNetCore.Http;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingRedemptionRateLimitPolicyTests
{
    [Fact]
    public void Direct_client_ip_is_the_partition_key()
    {
        var policy = new BranchPairingRedemptionRateLimitPolicy();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.2.15");

        var partition = policy.GetPartition(context);

        Assert.Equal("192.168.2.15", partition.PartitionKey);
    }

    [Fact]
    public void Missing_client_ip_uses_one_bounded_fallback_partition()
    {
        var policy = new BranchPairingRedemptionRateLimitPolicy();

        var first = policy.GetPartition(new DefaultHttpContext());
        var second = policy.GetPartition(new DefaultHttpContext());

        Assert.Equal("unknown-client", first.PartitionKey);
        Assert.Equal(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void Fixed_window_allows_ten_attempts_and_queues_none()
    {
        var policy = new BranchPairingRedemptionRateLimitPolicy();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.2.16");
        var partition = policy.GetPartition(context);
        using var limiter = partition.Factory(partition.PartitionKey);

        for (var attempt = 0;
             attempt < BranchPairingRedemptionRateLimitPolicy.PermitLimit;
             attempt++)
        {
            using var lease = limiter.AttemptAcquire();
            Assert.True(lease.IsAcquired);
        }

        using var rejected = limiter.AttemptAcquire();
        Assert.False(rejected.IsAcquired);
        Assert.Equal(TimeSpan.FromMinutes(1), BranchPairingRedemptionRateLimitPolicy.Window);
        Assert.Null(policy.OnRejected);
    }

    [Fact]
    public void Missing_http_context_is_rejected()
    {
        var policy = new BranchPairingRedemptionRateLimitPolicy();

        Assert.Throws<ArgumentNullException>(() => policy.GetPartition(null!));
    }
}
