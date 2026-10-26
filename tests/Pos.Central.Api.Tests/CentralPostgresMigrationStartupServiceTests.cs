using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralPostgresMigrationStartupServiceTests
{
    [Fact]
    public async Task Startup_executes_pending_migrations_before_succeeding()
    {
        var session = new RecordingSession([]);
        var factory = new RecordingSessionFactory(session);
        var service = Service(factory);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(1, factory.OpenCount);
        Assert.Equal(1, session.ApplyCount);
        Assert.Equal(1, session.CommitCount);
        Assert.True(session.Disposed);
        Assert.Equal(
            new PostgresCentralMigrationExecutionResult(0, 1, 1),
            service.LastResult);
    }

    [Fact]
    public async Task Schema_drift_fails_startup_without_commit()
    {
        var session = new RecordingSession(
        [
            new PostgresAppliedMigration(1, "0001_renamed")
        ]);
        var service = Service(new RecordingSessionFactory(session));

        await Assert.ThrowsAsync<PostgresCentralMigrationDriftException>(() =>
            service.StartAsync(CancellationToken.None));

        Assert.Equal(0, session.ApplyCount);
        Assert.Equal(0, session.CommitCount);
        Assert.True(session.Disposed);
        Assert.Null(service.LastResult);
    }

    [Fact]
    public async Task Startup_forwards_host_cancellation()
    {
        using var source = new CancellationTokenSource();
        var session = new RecordingSession([]);
        var factory = new RecordingSessionFactory(session);
        var service = Service(factory);

        await service.StartAsync(source.Token);

        Assert.All(factory.ObservedTokens, token => Assert.Equal(source.Token, token));
        Assert.All(session.ObservedTokens, token => Assert.Equal(source.Token, token));
    }

    [Fact]
    public async Task Stop_does_not_open_a_database_session()
    {
        var factory = new RecordingSessionFactory(new RecordingSession([]));
        var service = Service(factory);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, factory.OpenCount);
        Assert.Null(service.LastResult);
    }

    [Fact]
    public void Missing_executor_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CentralPostgresMigrationStartupService(null!));
    }

    private static CentralPostgresMigrationStartupService Service(
        IPostgresCentralMigrationSessionFactory factory) =>
        new(new PostgresCentralMigrationExecutor(factory));

    private sealed class RecordingSessionFactory(
        IPostgresCentralMigrationSession session) :
        IPostgresCentralMigrationSessionFactory
    {
        public int OpenCount { get; private set; }

        public List<CancellationToken> ObservedTokens { get; } = [];

        public ValueTask<IPostgresCentralMigrationSession>
            OpenLockedTransactionAsync(
                CancellationToken cancellationToken = default)
        {
            OpenCount++;
            ObservedTokens.Add(cancellationToken);
            return ValueTask.FromResult(session);
        }
    }

    private sealed class RecordingSession(
        IReadOnlyCollection<PostgresAppliedMigration> applied) :
        IPostgresCentralMigrationSession
    {
        public int ApplyCount { get; private set; }

        public int CommitCount { get; private set; }

        public bool Disposed { get; private set; }

        public List<CancellationToken> ObservedTokens { get; } = [];

        public Task<IReadOnlyCollection<PostgresAppliedMigration>>
            ReadAppliedMigrationsAsync(
                CancellationToken cancellationToken = default)
        {
            ObservedTokens.Add(cancellationToken);
            return Task.FromResult(applied);
        }

        public Task ApplyMigrationAsync(
            PostgresCentralMigration migration,
            CancellationToken cancellationToken = default)
        {
            ObservedTokens.Add(cancellationToken);
            ApplyCount++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(
            CancellationToken cancellationToken = default)
        {
            ObservedTokens.Add(cancellationToken);
            CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
