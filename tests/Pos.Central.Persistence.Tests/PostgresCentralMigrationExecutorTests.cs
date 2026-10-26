using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralMigrationExecutorTests
{
    [Fact]
    public async Task Empty_database_applies_pending_plan_and_commits()
    {
        var session = new RecordingSession([]);
        var factory = new RecordingSessionFactory(session);
        var executor = new PostgresCentralMigrationExecutor(factory);

        var result = await executor.ExecuteAsync();

        var applied = Assert.Single(session.Applied);
        Assert.Equal(1, applied.Version);
        Assert.Equal("0001_branch_cloud_events", applied.Name);
        Assert.Equal(1, session.CommitCount);
        Assert.True(session.Disposed);
        Assert.Equal(
            new PostgresCentralMigrationExecutionResult(0, 1, 1),
            result);
    }

    [Fact]
    public async Task Current_database_commits_without_reapplying_migration()
    {
        var session = new RecordingSession(
        [
            new PostgresAppliedMigration(1, "0001_branch_cloud_events")
        ]);
        var executor = new PostgresCentralMigrationExecutor(
            new RecordingSessionFactory(session));

        var result = await executor.ExecuteAsync();

        Assert.Empty(session.Applied);
        Assert.Equal(1, session.CommitCount);
        Assert.True(session.Disposed);
        Assert.Equal(
            new PostgresCentralMigrationExecutionResult(1, 1, 0),
            result);
    }

    [Fact]
    public async Task Drift_fails_before_sql_and_does_not_commit()
    {
        var session = new RecordingSession(
        [
            new PostgresAppliedMigration(1, "0001_renamed")
        ]);
        var executor = new PostgresCentralMigrationExecutor(
            new RecordingSessionFactory(session));

        await Assert.ThrowsAsync<PostgresCentralMigrationDriftException>(
            () => executor.ExecuteAsync());

        Assert.Empty(session.Applied);
        Assert.Equal(0, session.CommitCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Apply_failure_does_not_commit_and_disposes_session()
    {
        var session = new RecordingSession([])
        {
            ApplyException = new TestMigrationException()
        };
        var executor = new PostgresCentralMigrationExecutor(
            new RecordingSessionFactory(session));

        await Assert.ThrowsAsync<TestMigrationException>(
            () => executor.ExecuteAsync());

        Assert.Equal(0, session.CommitCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Caller_cancellation_is_forwarded_to_every_operation()
    {
        using var source = new CancellationTokenSource();
        var session = new RecordingSession([]);
        var factory = new RecordingSessionFactory(session);
        var executor = new PostgresCentralMigrationExecutor(factory);

        await executor.ExecuteAsync(source.Token);

        Assert.All(factory.ObservedTokens, token => Assert.Equal(source.Token, token));
        Assert.All(session.ObservedTokens, token => Assert.Equal(source.Token, token));
    }

    [Fact]
    public async Task Missing_session_is_rejected()
    {
        var executor = new PostgresCentralMigrationExecutor(
            new NullSessionFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync());
    }

    [Fact]
    public async Task Missing_applied_history_is_rejected_without_commit()
    {
        var session = new RecordingSession(null!);
        var executor = new PostgresCentralMigrationExecutor(
            new RecordingSessionFactory(session));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync());

        Assert.Equal(0, session.CommitCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void Missing_session_factory_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PostgresCentralMigrationExecutor(null!));
    }

    private sealed class RecordingSessionFactory(
        IPostgresCentralMigrationSession session) :
        IPostgresCentralMigrationSessionFactory
    {
        public List<CancellationToken> ObservedTokens { get; } = [];

        public ValueTask<IPostgresCentralMigrationSession>
            OpenLockedTransactionAsync(
                CancellationToken cancellationToken = default)
        {
            ObservedTokens.Add(cancellationToken);
            return ValueTask.FromResult(session);
        }
    }

    private sealed class NullSessionFactory :
        IPostgresCentralMigrationSessionFactory
    {
        public ValueTask<IPostgresCentralMigrationSession>
            OpenLockedTransactionAsync(
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IPostgresCentralMigrationSession>(null!);
    }

    private sealed class RecordingSession(
        IReadOnlyCollection<PostgresAppliedMigration> applied) :
        IPostgresCentralMigrationSession
    {
        public List<PostgresCentralMigration> Applied { get; } = [];

        public List<CancellationToken> ObservedTokens { get; } = [];

        public Exception? ApplyException { get; init; }

        public int CommitCount { get; private set; }

        public bool Disposed { get; private set; }

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
            if (ApplyException is not null)
            {
                return Task.FromException(ApplyException);
            }

            Applied.Add(migration);
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

    private sealed class TestMigrationException : Exception;
}
