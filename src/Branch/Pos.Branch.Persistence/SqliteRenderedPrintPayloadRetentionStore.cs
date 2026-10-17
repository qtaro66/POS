using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record RenderedPrintPayloadRetentionSnapshot(
    long PayloadCount,
    long TotalBytes,
    long ProtectedPayloadCount,
    long ProtectedBytes,
    long RetainedByAgePayloadCount,
    long RetainedByAgeBytes,
    long DeletablePayloadCount,
    long DeletableBytes);

public sealed record RenderedPrintPayloadPruneResult(
    RenderedPrintPayloadRetentionSnapshot Before,
    RenderedPrintPayloadRetentionSnapshot After,
    long DeletedPayloadCount,
    long DeletedBytes,
    bool TargetReached);

public sealed class SqliteRenderedPrintPayloadRetentionStore
{
    private const string EligibleTerminalPredicate = """
        j.lease_owner IS NULL
        AND (
            j.status IN ('PrintDispatched', 'Completed')
            OR (j.status = 'Failed' AND j.next_retry_at IS NULL)
        )
        """;

    private readonly SqliteConnectionFactory _connections;

    public SqliteRenderedPrintPayloadRetentionStore(string databasePath)
    {
        _connections = new SqliteConnectionFactory(databasePath);
    }

    public async Task<RenderedPrintPayloadRetentionSnapshot> InspectAsync(
        DateTimeOffset deleteBefore,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await InspectCoreAsync(
            connection,
            transaction: null,
            deleteBefore,
            cancellationToken);
    }

    public async Task<RenderedPrintPayloadPruneResult> PruneAsync(
        DateTimeOffset deleteBefore,
        long maximumTotalBytes,
        int maximumDeletes,
        CancellationToken cancellationToken = default)
    {
        if (maximumTotalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTotalBytes));
        }

        if (maximumDeletes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDeletes));
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            var before = await InspectCoreAsync(
                connection,
                transaction,
                deleteBefore,
                cancellationToken);
            var bytesToDelete = Math.Max(0, before.TotalBytes - maximumTotalBytes);
            long deletedPayloadCount = 0;
            long deletedBytes = 0;

            if (bytesToDelete > 0)
            {
                var candidates = await ReadCandidatesAsync(
                    connection,
                    transaction,
                    deleteBefore,
                    maximumDeletes,
                    cancellationToken);
                foreach (var candidate in candidates)
                {
                    if (deletedBytes >= bytesToDelete)
                    {
                        break;
                    }

                    using var delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = $$"""
                        DELETE FROM rendered_print_payloads
                        WHERE print_job_id = $printJobId
                          AND created_at < $deleteBefore
                          AND EXISTS (
                              SELECT 1
                              FROM print_jobs AS j
                              WHERE j.print_job_id = rendered_print_payloads.print_job_id
                                AND {{EligibleTerminalPredicate}});
                        """;
                    delete.Parameters.AddWithValue("$printJobId", candidate.PrintJobId);
                    delete.Parameters.AddWithValue("$deleteBefore", Timestamp(deleteBefore));
                    if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
                    {
                        throw new InvalidOperationException(
                            "The selected rendered print payload is no longer deletable.");
                    }

                    deletedPayloadCount++;
                    deletedBytes = checked(deletedBytes + candidate.PayloadBytes);
                }
            }

            var after = await InspectCoreAsync(
                connection,
                transaction,
                deleteBefore,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new RenderedPrintPayloadPruneResult(
                before,
                after,
                deletedPayloadCount,
                deletedBytes,
                after.TotalBytes <= maximumTotalBytes);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<RenderedPrintPayloadRetentionSnapshot> InspectCoreAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset deleteBefore,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            SELECT
                COUNT(*),
                COALESCE(SUM(length(r.payload_bytes)), 0),
                COALESCE(SUM(CASE WHEN NOT ({{EligibleTerminalPredicate}})
                    THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN NOT ({{EligibleTerminalPredicate}})
                    THEN length(r.payload_bytes) ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN ({{EligibleTerminalPredicate}})
                                      AND r.created_at >= $deleteBefore
                    THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN ({{EligibleTerminalPredicate}})
                                      AND r.created_at >= $deleteBefore
                    THEN length(r.payload_bytes) ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN ({{EligibleTerminalPredicate}})
                                      AND r.created_at < $deleteBefore
                    THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN ({{EligibleTerminalPredicate}})
                                      AND r.created_at < $deleteBefore
                    THEN length(r.payload_bytes) ELSE 0 END), 0)
            FROM rendered_print_payloads AS r
            JOIN print_jobs AS j ON j.print_job_id = r.print_job_id;
            """;
        command.Parameters.AddWithValue("$deleteBefore", Timestamp(deleteBefore));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The rendered print payload inventory was not returned.");
        }

        return new RenderedPrintPayloadRetentionSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7));
    }

    private static async Task<IReadOnlyList<PayloadCandidate>> ReadCandidatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset deleteBefore,
        int maximumDeletes,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $$"""
            SELECT r.print_job_id, length(r.payload_bytes)
            FROM rendered_print_payloads AS r
            JOIN print_jobs AS j ON j.print_job_id = r.print_job_id
            WHERE {{EligibleTerminalPredicate}}
              AND r.created_at < $deleteBefore
            ORDER BY r.created_at, r.print_job_id
            LIMIT $maximumDeletes;
            """;
        command.Parameters.AddWithValue("$deleteBefore", Timestamp(deleteBefore));
        command.Parameters.AddWithValue("$maximumDeletes", maximumDeletes);
        var candidates = new List<PayloadCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new PayloadCandidate(reader.GetString(0), reader.GetInt64(1)));
        }

        return candidates;
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original operation or commit exception.
        }
    }

    private sealed record PayloadCandidate(string PrintJobId, long PayloadBytes);
}
