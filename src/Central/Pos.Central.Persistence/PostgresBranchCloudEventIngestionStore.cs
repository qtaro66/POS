using Npgsql;
using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Persistence;

public sealed record PostgresBranchCloudEventRecord(
    string EventId,
    string BranchId,
    long BranchSequence,
    string RequestId,
    string TableSessionId,
    TableSessionAction Action,
    long TableVersion,
    DateTimeOffset OccurredAt)
{
    public static PostgresBranchCloudEventRecord FromEnvelope(
        BranchCloudOutboxEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return new PostgresBranchCloudEventRecord(
            envelope.EventId,
            envelope.BranchId,
            envelope.BranchSequence,
            envelope.RequestId,
            envelope.TableSessionId.Value,
            envelope.Action,
            envelope.TableVersion,
            NormalizeTimestamp(envelope.OccurredAt));
    }

    public static PostgresBranchCloudEventRecord FromStorage(
        string eventId,
        string branchId,
        long branchSequence,
        string requestId,
        string tableSessionId,
        string action,
        long tableVersion,
        DateTimeOffset occurredAt)
    {
        if (!Enum.TryParse<TableSessionAction>(
                action,
                ignoreCase: false,
                out var parsedAction) ||
            !Enum.IsDefined(parsedAction))
        {
            throw new InvalidOperationException(
                $"PostgreSQL Cloud event action '{action}' is not supported.");
        }

        if (occurredAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "PostgreSQL Cloud event occurred_at is not UTC.");
        }

        return new PostgresBranchCloudEventRecord(
            eventId,
            branchId,
            branchSequence,
            requestId,
            tableSessionId,
            parsedAction,
            tableVersion,
            occurredAt);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value) =>
        new(
            value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
}

public interface IPostgresBranchCloudEventIngestionSessionFactory
{
    ValueTask<IPostgresBranchCloudEventIngestionSession> OpenTransactionAsync(
        CancellationToken cancellationToken = default);
}

public interface IPostgresBranchCloudEventIngestionSession : IAsyncDisposable
{
    Task<long> ReadNextBranchSequenceAsync(
        string branchId,
        CancellationToken cancellationToken = default);

    Task<bool> TryInsertAsync(
        PostgresBranchCloudEventRecord cloudEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PostgresBranchCloudEventRecord>>
        ReadIdentityMatchesAsync(
            string eventId,
            string branchId,
            long branchSequence,
            CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}

public sealed class PostgresBranchCloudEventIngestionStore :
    IBranchCloudEventIngestionStore
{
    private readonly IPostgresBranchCloudEventIngestionSessionFactory
        _sessionFactory;

    public PostgresBranchCloudEventIngestionStore(
        IPostgresBranchCloudEventIngestionSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory ??
            throw new ArgumentNullException(nameof(sessionFactory));
    }

    public async Task<BranchCloudEventPersistenceResult> PersistAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            return await PersistCoreAsync(envelope, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NpgsqlException exception) when (exception.IsTransient)
        {
            throw new BranchCloudEventPersistenceUnavailableException(
                exception);
        }
        catch (TimeoutException exception)
        {
            throw new BranchCloudEventPersistenceUnavailableException(
                exception);
        }
    }

    private async Task<BranchCloudEventPersistenceResult> PersistCoreAsync(
        BranchCloudOutboxEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var expected = PostgresBranchCloudEventRecord.FromEnvelope(envelope);
        await using var session = await _sessionFactory
            .OpenTransactionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The PostgreSQL ingestion session factory returned no session.");

        var nextBranchSequence = await session.ReadNextBranchSequenceAsync(
            expected.BranchId,
            cancellationToken);
        if (nextBranchSequence < 1)
        {
            throw new InvalidOperationException(
                "The PostgreSQL ingestion session returned an invalid next Branch sequence.");
        }

        if (expected.BranchSequence > nextBranchSequence)
        {
            throw new BranchCloudEventSequenceGapException(
                expected.BranchId,
                nextBranchSequence,
                expected.BranchSequence);
        }

        if (await session.TryInsertAsync(expected, cancellationToken))
        {
            await session.CommitAsync(cancellationToken);
            return Result(
                BranchCloudEventPersistenceDisposition.Stored,
                expected);
        }

        var matches = await session.ReadIdentityMatchesAsync(
            expected.EventId,
            expected.BranchId,
            expected.BranchSequence,
            cancellationToken) ?? throw new InvalidOperationException(
                "The PostgreSQL ingestion session returned no identity result.");

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "No persisted Cloud event was found after an identity conflict.");
        }

        if (matches.Count != 1 || matches[0] != expected)
        {
            throw new BranchCloudEventUniquenessConflictException(
                expected.EventId,
                expected.BranchId,
                expected.BranchSequence);
        }

        await session.CommitAsync(cancellationToken);
        return Result(
            BranchCloudEventPersistenceDisposition.AlreadyStored,
            expected);
    }

    private static BranchCloudEventPersistenceResult Result(
        BranchCloudEventPersistenceDisposition disposition,
        PostgresBranchCloudEventRecord cloudEvent) =>
        new(
            disposition,
            cloudEvent.EventId,
            cloudEvent.BranchId,
            cloudEvent.BranchSequence);
}
