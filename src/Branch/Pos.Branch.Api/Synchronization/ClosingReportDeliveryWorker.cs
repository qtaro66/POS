using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class ClosingReportDeliveryCycle(SqliteTableSessionCommandStore store, IClosingReportTransport transport,
    TimeProvider time, IOptions<ClosingReportDeliveryOptions> options)
{
    private readonly string _workerId = $"closing-report-{Environment.ProcessId}-{Guid.NewGuid():N}";

    public async Task<bool> RunOnceAsync(CancellationToken token = default)
    {
        var settings = options.Value;
        var lease = await store.ClaimClosingReportAsync(settings.BranchId, _workerId, time.GetUtcNow(), settings.LeaseDuration, token);
        if (lease is null) { return false; }
        // Cancellation leaves the lease intact so a restarted worker can recover it.
        var failure = await transport.SendAsync(lease, token);
        if (failure is null)
        {
            await store.ConfirmClosingReportAsync(lease, time.GetUtcNow(), token);
        }
        else
        {
            var multiplier = Math.Pow(2, Math.Min(lease.AttemptCount - 1, 60));
            var retry = TimeSpan.FromMilliseconds(Math.Min(settings.MaximumRetryDelay.TotalMilliseconds,
                settings.RetryDelay.TotalMilliseconds * multiplier));
            await store.RetryClosingReportAsync(lease, time.GetUtcNow(), retry, failure.Value, token);
        }
        return true;
    }
}

public sealed class ClosingReportDeliveryWorker(ClosingReportDeliveryCycle cycle,
    IOptions<ClosingReportDeliveryOptions> options, TimeProvider time, ILogger<ClosingReportDeliveryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = options.Value.IdleDelay;
            try
            {
                if (await cycle.RunOnceAsync(stoppingToken)) { delay = TimeSpan.Zero; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                logger.LogWarning("Closing report delivery cycle failed; durable reports are retained.");
                delay = options.Value.RetryDelay;
            }
            try { if (delay > TimeSpan.Zero) { await Task.Delay(delay, time, stoppingToken); } }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
