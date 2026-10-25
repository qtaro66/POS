using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionHandlerTests
{
    [Theory]
    [InlineData(BranchCloudEventPersistenceDisposition.Stored)]
    [InlineData(BranchCloudEventPersistenceDisposition.AlreadyStored)]
    public async Task Durable_persistence_returns_versioned_acknowledgment(
        BranchCloudEventPersistenceDisposition disposition)
    {
        var store = new RecordingStore(disposition);
        var response = await BranchCloudEventIngestionHandler.HandleAsync(
            Request(),
            new BranchCloudEventIngestionService(store));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("EVENT-01", response.EventId);
        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(7, response.BranchSequence);
        Assert.Equal("REQUEST-01", store.Envelope?.RequestId);
        Assert.Equal(TableSessionAction.SubmitOrder, store.Envelope?.Action);
    }

    [Fact]
    public async Task Uniqueness_conflict_is_not_mapped_to_acknowledgment()
    {
        var service = new BranchCloudEventIngestionService(
            new ConflictingStore());

        var exception = await Assert.ThrowsAsync<
            BranchCloudEventUniquenessConflictException>(() =>
            BranchCloudEventIngestionHandler.HandleAsync(Request(), service));

        Assert.Equal("EVENT-01", exception.EventId);
        Assert.Equal("BRANCH-01", exception.BranchId);
        Assert.Equal(7, exception.BranchSequence);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_persistence()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new RecordingStore(
            BranchCloudEventPersistenceDisposition.Stored);

        await BranchCloudEventIngestionHandler.HandleAsync(
            Request(),
            new BranchCloudEventIngestionService(store),
            cancellation.Token);

        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Missing_arguments_are_rejected()
    {
        var service = new BranchCloudEventIngestionService(
            new RecordingStore(BranchCloudEventPersistenceDisposition.Stored));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionHandler.HandleAsync(null!, service));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionHandler.HandleAsync(Request(), null!));
    }

    private static BranchCloudEventIngestionRequest Request() =>
        new(
            1,
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            "SESSION-01",
            nameof(TableSessionAction.SubmitOrder),
            3,
            new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private sealed class RecordingStore(
        BranchCloudEventPersistenceDisposition disposition) :
        IBranchCloudEventIngestionStore
    {
        public BranchCloudOutboxEventEnvelope? Envelope { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            CancellationToken = cancellationToken;
            return Task.FromResult(new BranchCloudEventPersistenceResult(
                disposition,
                envelope.EventId,
                envelope.BranchId,
                envelope.BranchSequence));
        }
    }

    private sealed class ConflictingStore : IBranchCloudEventIngestionStore
    {
        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromException<BranchCloudEventPersistenceResult>(
                new BranchCloudEventUniquenessConflictException(
                    envelope.EventId,
                    envelope.BranchId,
                    envelope.BranchSequence));
    }
}
