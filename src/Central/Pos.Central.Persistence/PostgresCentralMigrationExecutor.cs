namespace Pos.Central.Persistence;

public interface IPostgresCentralMigrationSessionFactory
{
    ValueTask<IPostgresCentralMigrationSession> OpenLockedTransactionAsync(
        CancellationToken cancellationToken = default);
}

public interface IPostgresCentralMigrationSession : IAsyncDisposable
{
    Task<IReadOnlyCollection<PostgresAppliedMigration>>
        ReadAppliedMigrationsAsync(
            CancellationToken cancellationToken = default);

    Task ApplyMigrationAsync(
        PostgresCentralMigration migration,
        CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed record PostgresCentralMigrationExecutionResult(
    int InitialVersion,
    int FinalVersion,
    int AppliedCount);

public sealed class PostgresCentralMigrationExecutor
{
    private readonly IPostgresCentralMigrationSessionFactory _sessionFactory;

    public PostgresCentralMigrationExecutor(
        IPostgresCentralMigrationSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ??
            throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task<PostgresCentralMigrationExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        await using var session = await _sessionFactory
            .OpenLockedTransactionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The PostgreSQL migration session factory returned no session.");
        var applied = await session.ReadAppliedMigrationsAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The PostgreSQL migration session returned no applied history.");
        var pending = PostgresCentralMigrationPlanner.Plan(applied);
        var initialVersion = applied.Count == 0
            ? 0
            : applied.Max(migration => migration.Version);

        foreach (var migration in pending)
        {
            await session.ApplyMigrationAsync(migration, cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
        var finalVersion = pending.Count == 0
            ? initialVersion
            : pending[^1].Version;
        return new PostgresCentralMigrationExecutionResult(
            initialVersion,
            finalVersion,
            pending.Count);
    }
}
