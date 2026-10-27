using Npgsql;
using Pos.Application.Synchronization;
using Pos.Central.Persistence;
using Pos.Domain.TableSessions;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresBranchCloudEventIngestionStoreTests
{
    [Fact]
    public async Task New_event_is_committed_as_stored()
    {
        var session = new RecordingSession(inserted: true, []);
        var store = Store(session);

        var result = await store.PersistAsync(Envelope());

        Assert.Equal(BranchCloudEventPersistenceDisposition.Stored,
            result.Disposition);
        Assert.Equal("EVENT-01", result.EventId);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal(7, result.BranchSequence);
        Assert.Equal(
            PostgresBranchCloudEventRecord.FromEnvelope(Envelope()),
            session.InsertedEvent);
        Assert.True(session.Committed);
        Assert.False(session.IdentityRead);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Matching_identity_with_different_payload_conflicts()
    {
        var expected = PostgresBranchCloudEventRecord.FromEnvelope(Envelope());
        var session = new RecordingSession(
            inserted: false,
            [expected with { RequestId = "OTHER-REQUEST" }]);
        var store = Store(session);

        await Assert.ThrowsAsync<BranchCloudEventUniquenessConflictException>(
            () => store.PersistAsync(Envelope()));

        Assert.False(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Exact_retry_is_committed_as_already_stored()
    {
        var expected = PostgresBranchCloudEventRecord.FromEnvelope(Envelope());
        var session = new RecordingSession(inserted: false, [expected]);
        var store = Store(session);

        var result = await store.PersistAsync(Envelope());

        Assert.Equal(BranchCloudEventPersistenceDisposition.AlreadyStored,
            result.Disposition);
        Assert.True(session.Committed);
        Assert.True(session.IdentityRead);
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("OTHER-EVENT", "BRANCH-01", 7)]
    [InlineData("EVENT-01", "OTHER-BRANCH", 7)]
    [InlineData("EVENT-01", "BRANCH-01", 8)]
    public async Task Reused_identity_with_different_content_conflicts(
        string eventId,
        string branchId,
        long branchSequence)
    {
        var expected = PostgresBranchCloudEventRecord.FromEnvelope(Envelope());
        var conflicting = expected with
        {
            EventId = eventId,
            BranchId = branchId,
            BranchSequence = branchSequence
        };
        var session = new RecordingSession(inserted: false, [conflicting]);
        var store = Store(session);

        var exception = await Assert.ThrowsAsync<
            BranchCloudEventUniquenessConflictException>(() =>
                store.PersistAsync(Envelope()));

        Assert.Equal("EVENT-01", exception.EventId);
        Assert.Equal("BRANCH-01", exception.BranchId);
        Assert.Equal(7, exception.BranchSequence);
        Assert.False(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Two_identity_matches_conflict_without_commit()
    {
        var expected = PostgresBranchCloudEventRecord.FromEnvelope(Envelope());
        var session = new RecordingSession(
            inserted: false,
            [expected, expected with { RequestId = "OTHER-REQUEST" }]);
        var store = Store(session);

        await Assert.ThrowsAsync<BranchCloudEventUniquenessConflictException>(
            () => store.PersistAsync(Envelope()));

        Assert.False(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Missing_identity_after_insert_conflict_fails_closed()
    {
        var session = new RecordingSession(inserted: false, []);
        var store = Store(session);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.PersistAsync(Envelope()));

        Assert.Contains("No persisted Cloud event", exception.Message);
        Assert.False(session.Committed);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_every_session_operation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var session = new RecordingSession(inserted: true, []);
        var factory = new RecordingFactory(session);
        var store = new PostgresBranchCloudEventIngestionStore(factory);

        await store.PersistAsync(Envelope(), cancellation.Token);

        Assert.Equal(cancellation.Token, factory.CancellationToken);
        Assert.All(
            session.CancellationTokens,
            token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task Missing_dependencies_and_results_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PostgresBranchCloudEventIngestionStore(null!));
        Assert.Throws<ArgumentNullException>(() =>
            PostgresBranchCloudEventRecord.FromEnvelope(null!));

        var store = Store(new RecordingSession(inserted: true, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.PersistAsync(null!));

        var missingSessionStore =
            new PostgresBranchCloudEventIngestionStore(
                new RecordingFactory(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missingSessionStore.PersistAsync(Envelope()));

        var missingMatchesStore = Store(
            new RecordingSession(inserted: false, matches: null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missingMatchesStore.PersistAsync(Envelope()));
    }

    [Fact]
    public async Task Transient_npgsql_failure_maps_to_unavailable_boundary()
    {
        var cause = new NpgsqlException(
            "Synthetic transient failure.",
            new TimeoutException());
        var store = new PostgresBranchCloudEventIngestionStore(
            new ThrowingFactory(cause));

        var exception = await Assert.ThrowsAsync<
            BranchCloudEventPersistenceUnavailableException>(() =>
                store.PersistAsync(Envelope()));

        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public async Task Direct_timeout_maps_to_unavailable_boundary()
    {
        var cause = new TimeoutException("Synthetic timeout.");
        var store = new PostgresBranchCloudEventIngestionStore(
            new ThrowingFactory(cause));

        var exception = await Assert.ThrowsAsync<
            BranchCloudEventPersistenceUnavailableException>(() =>
                store.PersistAsync(Envelope()));

        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public async Task Non_transient_npgsql_failure_is_not_misclassified()
    {
        var cause = new NpgsqlException("Synthetic non-transient failure.");
        var store = new PostgresBranchCloudEventIngestionStore(
            new ThrowingFactory(cause));

        var exception = await Assert.ThrowsAsync<NpgsqlException>(() =>
            store.PersistAsync(Envelope()));

        Assert.Same(cause, exception);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_misclassified()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cause = new OperationCanceledException(cancellation.Token);
        var store = new PostgresBranchCloudEventIngestionStore(
            new ThrowingFactory(cause));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.PersistAsync(Envelope(), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static PostgresBranchCloudEventIngestionStore Store(
        RecordingSession session) =>
        new(new RecordingFactory(session));

    private static BranchCloudOutboxEventEnvelope Envelope() =>
        BranchCloudOutboxEventEnvelope.Create(
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            new DateTimeOffset(
                2026,
                10,
                27,
                8,
                30,
                0,
                TimeSpan.Zero));

    private sealed class RecordingFactory(
        IPostgresBranchCloudEventIngestionSession? session) :
        IPostgresBranchCloudEventIngestionSessionFactory
    {
        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<IPostgresBranchCloudEventIngestionSession>
            OpenTransactionAsync(
                CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            return ValueTask.FromResult(session!);
        }
    }

    private sealed class ThrowingFactory(Exception exception) :
        IPostgresBranchCloudEventIngestionSessionFactory
    {
        public ValueTask<IPostgresBranchCloudEventIngestionSession>
            OpenTransactionAsync(
                CancellationToken cancellationToken = default) =>
            ValueTask.FromException<
                IPostgresBranchCloudEventIngestionSession>(exception);
    }

    private sealed class RecordingSession(
        bool inserted,
        IReadOnlyList<PostgresBranchCloudEventRecord>? matches) :
        IPostgresBranchCloudEventIngestionSession
    {
        public bool Committed { get; private set; }

        public bool IdentityRead { get; private set; }

        public bool Disposed { get; private set; }

        public PostgresBranchCloudEventRecord? InsertedEvent { get; private set; }

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<bool> TryInsertAsync(
            PostgresBranchCloudEventRecord cloudEvent,
            CancellationToken cancellationToken = default)
        {
            InsertedEvent = cloudEvent;
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(inserted);
        }

        public Task<IReadOnlyList<PostgresBranchCloudEventRecord>>
            ReadIdentityMatchesAsync(
                string eventId,
                string branchId,
                long branchSequence,
                CancellationToken cancellationToken = default)
        {
            IdentityRead = true;
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(matches!);
        }

        public Task CommitAsync(
            CancellationToken cancellationToken = default)
        {
            Committed = true;
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
