using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Pos.Branch.Api.Security;

public sealed class BranchPairingRedemptionRateLimitPolicy : IRateLimiterPolicy<string>
{
    public const string PolicyName = "BranchPairingRedemption";

    public const int PermitLimit = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = PermitLimit,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = Window
            });
    }
}
