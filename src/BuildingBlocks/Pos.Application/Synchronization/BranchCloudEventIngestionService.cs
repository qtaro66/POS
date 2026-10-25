namespace Pos.Application.Synchronization;

public sealed class BranchCloudEventIngestionService
{
    private readonly IBranchCloudEventIngestionStore _store;

    public BranchCloudEventIngestionService(
        IBranchCloudEventIngestionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<BranchCloudEventPersistenceResult> IngestAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var result = await _store.PersistAsync(envelope, cancellationToken)
            ?? throw new InvalidOperationException(
                "The Cloud event ingestion store returned no persistence result.");

        if (!string.Equals(result.EventId, envelope.EventId, StringComparison.Ordinal) ||
            !string.Equals(result.BranchId, envelope.BranchId, StringComparison.Ordinal) ||
            result.BranchSequence != envelope.BranchSequence)
        {
            throw new InvalidOperationException(
                "The Cloud event ingestion store returned a different event identity.");
        }

        return result;
    }
}
