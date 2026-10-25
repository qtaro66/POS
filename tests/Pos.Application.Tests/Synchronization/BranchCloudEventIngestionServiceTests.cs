using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudEventIngestionServiceTests
{
    [Theory]
    [InlineData(BranchCloudEventPersistenceDisposition.Stored)]
    [InlineData(BranchCloudEventPersistenceDisposition.AlreadyStored)]
    public async Task Matching_durable_result_is_returned(
        BranchCloudEventPersistenceDisposition disposition)
    {
        var envelope = Envelope();
        var store = new RecordingStore(new BranchCloudEventPersistenceResult(
            disposition,
            envelope.EventId,
            envelope.BranchId,
            envelope.BranchSequence));
        var service = new BranchCloudEventIngestionService(store);

        var result = await service.IngestAsync(envelope);

        Assert.Equal(disposition, result.Disposition);
        Assert.Same(envelope, store.Envelope);
    }

    [Theory]
    [InlineData("OTHER-EVENT", "BRANCH-01", 7)]
    [InlineData("EVENT-01", "OTHER-BRANCH", 7)]
    [InlineData("EVENT-01", "BRANCH-01", 8)]
    public async Task Mismatched_store_identity_fails_closed(
        string eventId,
        string branchId,
        long branchSequence)
    {
        var service = new BranchCloudEventIngestionService(
            new RecordingStore(new BranchCloudEventPersistenceResult(
                BranchCloudEventPersistenceDisposition.Stored,
                eventId,
                branchId,
                branchSequence)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync(Envelope()));

        Assert.Contains("different event identity", exception.Message);
    }

    [Fact]
    public async Task Missing_store_result_fails_closed()
    {
        var service = new BranchCloudEventIngestionService(
            new RecordingStore(null));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IngestAsync(Envelope()));

        Assert.Contains("no persistence result", exception.Message);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_store()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new RecordingStore(new BranchCloudEventPersistenceResult(
            BranchCloudEventPersistenceDisposition.Stored,
            "EVENT-01",
            "BRANCH-01",
            7));
        var service = new BranchCloudEventIngestionService(store);

        await service.IngestAsync(Envelope(), cancellation.Token);

        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Missing_dependencies_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudEventIngestionService(null!));

        var service = new BranchCloudEventIngestionService(
            new RecordingStore(null));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.IngestAsync(null!));
    }

    private static BranchCloudOutboxEventEnvelope Envelope()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            10,
            25,
            9,
            50,
            0,
            TimeSpan.Zero);
        var lease = new BranchCloudOutboxLease(
            "EVENT-01",
            "BRANCH-01",
            7,
            "REQUEST-01",
            TableSessionId.From("SESSION-01"),
            TableSessionAction.SubmitOrder,
            3,
            occurredAt,
            0,
            "WORKER-01",
            occurredAt.AddMinutes(1),
            occurredAt.AddMinutes(2));
        return BranchCloudOutboxEventEnvelope.FromLease(lease);
    }

    private sealed class RecordingStore(BranchCloudEventPersistenceResult? result) :
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
            return Task.FromResult(result!);
        }
    }
}
