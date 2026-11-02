using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudReplayStatusQuery : IBranchCloudReplayStatusQuery
{
    private readonly string _connectionString;
    public SqliteBranchCloudReplayStatusQuery(string databasePath)
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
    public async Task<BranchCloudReplayStatusPage> ListAsync(string branchId, int maximumResults, long? afterSequence = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (maximumResults is < 1 or > 200) { throw new ArgumentOutOfRangeException(nameof(maximumResults)); }
        if (afterSequence is <= 0) { throw new ArgumentOutOfRangeException(nameof(afterSequence)); }
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        BranchCloudReplaySummary summary;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(l.lease_owner IS NOT NULL), 0),
                       COALESCE(SUM(l.lease_owner IS NULL), 0), COALESCE(SUM(COALESCE(l.retry_count,0) > 0), 0)
                FROM cloud_sync_sequence_replay_requests r
                LEFT JOIN cloud_sync_sequence_replay_leases l ON l.recovery_id = r.recovery_id
                WHERE r.branch_id = $branch AND r.completed_at IS NULL;
                """;
            count.Parameters.AddWithValue("$branch", branchId.Trim());
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            summary = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }
        var items = new List<BranchCloudReplayStatusItem>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT r.recovery_id, r.expected_outbox_id, r.expected_branch_sequence, r.received_branch_sequence,
                       o.request_id, o.table_session_id, o.action, r.requested_at, l.lease_owner IS NOT NULL,
                       COALESCE(l.retry_count,0), l.next_retry_at, l.lease_expires_at, f.failed_at, f.expected_branch_sequence
                FROM cloud_sync_sequence_replay_requests r
                JOIN cloud_sync_outbox o ON o.branch_id = r.branch_id AND o.outbox_id = r.expected_outbox_id AND o.branch_sequence = r.expected_branch_sequence
                LEFT JOIN cloud_sync_sequence_replay_leases l ON l.recovery_id = r.recovery_id
                LEFT JOIN cloud_sync_sequence_replay_failures f ON f.failure_id = (
                    SELECT failure_id FROM cloud_sync_sequence_replay_failures
                    WHERE recovery_id = r.recovery_id AND branch_id = r.branch_id AND outbox_id = r.expected_outbox_id AND branch_sequence = r.expected_branch_sequence
                    ORDER BY failed_at DESC, failure_id DESC LIMIT 1)
                WHERE r.branch_id = $branch AND r.completed_at IS NULL AND r.expected_branch_sequence > $after
                ORDER BY r.expected_branch_sequence ASC LIMIT $limit;
                """;
            read.Parameters.AddWithValue("$branch", branchId.Trim());
            read.Parameters.AddWithValue("$after", afterSequence ?? 0);
            read.Parameters.AddWithValue("$limit", maximumResults + 1);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4),
                    reader.GetString(5), reader.GetString(6), Timestamp(reader.GetString(7)), reader.GetBoolean(8), reader.GetInt32(9),
                    OptionalTime(reader, 10), OptionalTime(reader, 11), OptionalTime(reader, 12), reader.IsDBNull(13) ? null : reader.GetInt64(13)));
            }
        }
        await transaction.CommitAsync(cancellationToken);
        long? next = null;
        if (items.Count > maximumResults) { items.RemoveAt(items.Count - 1); next = items[^1].ExpectedBranchSequence; }
        return new(summary, items, next);
    }
    private static DateTimeOffset? OptionalTime(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Timestamp(reader.GetString(ordinal));
    private static DateTimeOffset Timestamp(string value) => DateTimeOffset.ParseExact(value, ["O", "yyyy-MM-dd'T'HH:mm:ss'Z'"], CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
