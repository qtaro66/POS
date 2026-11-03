using System.Text.Json;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchEmployeePermissionPollingService(
    IHttpClientFactory clients, BranchEmployeePermissionCache cache, ILogger<BranchEmployeePermissionPollingService> logger)
    : BackgroundService
{
    public const string ClientName = "BranchEmployeePermissions";
    public const string Route = "/api/v1/branch-employee-permissions";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private int _refreshing;
    private bool _restored;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RefreshAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<PermissionRefreshOutcome> RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) { return PermissionRefreshOutcome.Busy; }
        try
        {
            if (!_restored)
            {
                try
                {
                    await cache.RestoreAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogWarning("The Branch employee permission cache could not be restored.");
                }
                _restored = true;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                using var client = clients.CreateClient(ClientName);
                using var response = await client.GetAsync(Route, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    return await cache.ApplyAsync(stream, timeout.Token)
                        ? PermissionRefreshOutcome.Updated : PermissionRefreshOutcome.NoUpdate;
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                exception is HttpRequestException or IOException or UnauthorizedAccessException or JsonException
                    or OperationCanceledException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                logger.LogWarning("The Branch employee permission snapshot could not be refreshed.");
            }
            return PermissionRefreshOutcome.Unavailable;
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }
}

public enum PermissionRefreshOutcome { Updated, NoUpdate, Busy, Unavailable }
