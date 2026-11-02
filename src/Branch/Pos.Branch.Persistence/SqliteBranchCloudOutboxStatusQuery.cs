using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxStatusQuery : IBranchCloudOutboxStatusQuery
{
    private readonly string _connectionString;
    public SqliteBranchCloudOutboxStatusQuery(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<BranchCloudOutboxStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (maximumResults is < 1 or > 200) { throw new ArgumentOutOfRangeException(nameof(maximumResults)); }
        if (afterSequence is <= 0) { throw new ArgumentOutOfRangeException(nameof(afterSequence)); }
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        BranchCloudOutboxSummary summary;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(processing_status = 'Pending'), 0),
                       COALESCE(SUM(processing_status = 'Processing'), 0), COALESCE(SUM(processing_status = 'Failed'), 0),
                       COALESCE(SUM(processing_status NOT IN ('Pending','Processing','Failed')), 0)
                FROM cloud_sync_outbox WHERE branch_id = $branch AND successfully_sent_at IS NULL;
                """;
            count.Parameters.AddWithValue("$branch", branchId.Trim());
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            summary = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
        }
        var items = new List<BranchCloudOutboxStatusItem>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT o.outbox_id, o.branch_sequence, o.request_id, o.table_session_id, o.action,
                       CASE WHEN o.processing_status IN ('Pending','Processing','Failed') THEN o.processing_status ELSE 'Other' END,
                       o.created_at, o.retry_count, o.next_retry_at, o.lease_expires_at, f.failed_at, f.expected_branch_sequence
                FROM cloud_sync_outbox o
                LEFT JOIN cloud_sync_delivery_failures f ON f.failure_id = (
                    SELECT failure_id FROM cloud_sync_delivery_failures
                    WHERE branch_id = o.branch_id AND outbox_id = o.outbox_id AND branch_sequence = o.branch_sequence
                    ORDER BY failed_at DESC, failure_id DESC LIMIT 1)
                WHERE o.branch_id = $branch AND o.successfully_sent_at IS NULL AND o.branch_sequence > $after
                ORDER BY o.branch_sequence ASC LIMIT $limit;
                """;
            read.Parameters.AddWithValue("$branch", branchId.Trim());
            read.Parameters.AddWithValue("$after", afterSequence ?? 0);
            read.Parameters.AddWithValue("$limit", maximumResults + 1);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetString(5), Timestamp(reader.GetString(6)), reader.GetInt32(7), OptionalTime(reader, 8), OptionalTime(reader, 9),
                    OptionalTime(reader, 10), reader.IsDBNull(11) ? null : reader.GetInt64(11)));
            }
        }
        await transaction.CommitAsync(cancellationToken);
        long? next = null;
        if (items.Count > maximumResults) { items.RemoveAt(items.Count - 1); next = items[^1].BranchSequence; }
        return new(summary, items, next);
    }

    private static DateTimeOffset? OptionalTime(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Timestamp(reader.GetString(ordinal));
    private static DateTimeOffset Timestamp(string value) => DateTimeOffset.ParseExact(value, ["O", "yyyy-MM-dd'T'HH:mm:ss'Z'"], CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
