using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class ClosingLifecycleDeliveryCycle(SqliteTableSessionCommandStore store, IClosingLifecycleTransport transport,
    IClosingReportTransport parentTransport, TimeProvider time, IOptions<ClosingReportDeliveryOptions> options)
{
    private readonly string _worker = $"closing-lifecycle-{Environment.ProcessId}-{Guid.NewGuid():N}";
    public async Task<bool> RunOnceAsync(CancellationToken token = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.LifecycleEnabled) { return false; }
        var lease = await store.ClaimClosingLifecycleAsync(settings.BranchId, _worker, time.GetUtcNow(), settings.LeaseDuration, token);
        if (lease is null) { return false; }
        var failure = await transport.SendAsync(lease, token);
        if (failure == ClosingLifecycleFailure.ParentUnavailable)
        {
            // Replay the original immutable packet, never re-execute the sale or closing command.
            // This transport-only descriptor is not used to alter the parent's historical receipt or lease.
            var parent = new ClosingReportLease(lease.BranchId, lease.ReportId, lease.RequestId, lease.Operation,
                lease.ParentPayloadJson, "context-parent-replay", _worker, lease.LeaseExpiresAt, lease.AttemptCount);
            var parentFailure = await parentTransport.SendAsync(parent, token);
            if (parentFailure is null) { failure = await transport.SendAsync(lease, token); }
            else { failure = ClosingLifecycleFailure.ParentUnavailable; }
        }
        if (failure is null) { await store.ConfirmClosingLifecycleAsync(lease, time.GetUtcNow(), token); }
        else
        {
            var multiplier = Math.Pow(2, Math.Min(lease.AttemptCount - 1, 60));
            var delay = TimeSpan.FromMilliseconds(Math.Min(settings.MaximumRetryDelay.TotalMilliseconds,
                settings.RetryDelay.TotalMilliseconds * multiplier));
            await store.RetryClosingLifecycleAsync(lease, time.GetUtcNow(), delay, failure.Value, token);
        }
        return true;
    }
}
