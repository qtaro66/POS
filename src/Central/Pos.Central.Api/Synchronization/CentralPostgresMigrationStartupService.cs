using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralPostgresMigrationStartupService : IHostedService
{
    private readonly PostgresCentralMigrationExecutor _executor;

    public CentralPostgresMigrationStartupService(
        PostgresCentralMigrationExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public PostgresCentralMigrationExecutionResult? LastResult { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LastResult = null;
        LastResult = await _executor.ExecuteAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
