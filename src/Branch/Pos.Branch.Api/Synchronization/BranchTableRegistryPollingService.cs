using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Ordering;

namespace Pos.Branch.Api.Synchronization;

public sealed class BranchTableRegistryPollingService(
    IHttpClientFactory clients, SqliteTableRegistryStore store, string branchId,
    ILogger<BranchTableRegistryPollingService> logger) : BackgroundService
{
    public const string ClientName = "BranchTableRegistry";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var client = clients.CreateClient(ClientName);
                var publication = await client.GetFromJsonAsync<PublishedTableRegistry>("/api/v1/branch-table-registry", stoppingToken);
                if (publication is null) { throw new InvalidDataException("Missing table registry publication."); }

                await store.ApplyAsync(TableRegistry.Read(publication, branchId), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested &&
                exception is HttpRequestException or IOException or JsonException or FormatException or SqliteException or OperationCanceledException)
            {
                logger.LogWarning("The Branch table registry could not be refreshed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
