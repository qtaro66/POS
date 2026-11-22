using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

/// <summary>One read snapshot of all Branch-to-Central delivery queues, not a network health probe.</summary>
public sealed class SqliteBranchDeliveryOverviewQuery
{
    private readonly string _connectionString;
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"
    ];
    private const string KeyFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public SqliteBranchDeliveryOverviewQuery(string databasePath)
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

    public async Task<BranchDeliverySnapshot> ReadAsync(string branchId, DateTimeOffset now, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (branchId != branchId.Trim() || branchId.Any(char.IsControl)) { throw new ArgumentException("Invalid Branch identity.", nameof(branchId)); }
        if (now.Offset != TimeSpan.Zero) { throw new ArgumentException("Delivery observation time must use UTC.", nameof(now)); }
        token.ThrowIfCancellationRequested();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token);
        var invalidTimestamp = false;
        // Normalize to seven fractional UTC digits before SQL comparison. julianday/strftime lose sub-millisecond precision.
        connection.CreateFunction<string?, string?>("delivery_utc_key", value =>
        {
            if (value is null) { return null; }
            if (!TryTimestamp(value, out var parsed)) { invalidTimestamp = true; return null; }
            return parsed.ToString(KeyFormat, CultureInfo.InvariantCulture);
        }, isDeterministic: true);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var tables = await ReadChannelAsync(connection, transaction, branchId, now, reports: false, token);
        var reports = await ReadChannelAsync(connection, transaction, branchId, now, reports: true, token);
        var lifecycle = await ReadChannelAsync(connection, transaction, branchId, now, reports: true, token, lifecycle: true);
        using var integrity = connection.CreateCommand();
        integrity.Transaction = transaction;
        integrity.CommandText = "SELECT EXISTS(SELECT 1 FROM closing_report_lifecycle_snapshots s LEFT JOIN closing_report_context_outbox o ON o.branch_id=s.branch_id AND o.request_id=s.request_id WHERE s.branch_id=$branch AND o.context_id IS NULL) OR EXISTS(SELECT 1 FROM closing_report_context_outbox o LEFT JOIN closing_report_lifecycle_snapshots s ON s.branch_id=o.branch_id AND s.request_id=o.request_id LEFT JOIN closing_report_outbox p ON p.branch_id=o.branch_id AND p.request_id=o.request_id WHERE o.branch_id=$branch AND (s.request_id IS NULL OR p.report_id IS NULL OR o.context_id != 'lifecycle:' || p.report_id));";
        integrity.Parameters.AddWithValue("$branch", branchId);
        if (Convert.ToInt64(await integrity.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) != 0) { throw new InvalidDataException("Saved lifecycle delivery is unavailable."); }
        using var replay = connection.CreateCommand();
        replay.Transaction = transaction;
        replay.CommandText = """
            WITH scoped AS MATERIALIZED (
                SELECT delivery_utc_key(requested_at) AS requested_key,
                       delivery_utc_key(completed_at) AS completed_key
                FROM cloud_sync_sequence_replay_requests WHERE branch_id=$branch
            )
            SELECT COUNT(*) FILTER (WHERE completed_key IS NULL),
                   COUNT(*) FILTER (WHERE completed_key IS NOT NULL), MAX(completed_key),
                   MIN(CASE WHEN completed_key IS NULL THEN requested_key END)
            FROM scoped;
            """;
        replay.Parameters.AddWithValue("$branch", branchId);
        long replayPending, replayCompleted;
        DateTimeOffset? replayLast, replayOldest;
        await using (var reader = await replay.ExecuteReaderAsync(token))
        {
            await reader.ReadAsync(token);
            replayPending = reader.GetInt64(0); replayCompleted = reader.GetInt64(1);
            replayLast = OptionalTimestamp(reader, 2); replayOldest = OptionalTimestamp(reader, 3);
        }
        if (invalidTimestamp) { throw new InvalidDataException("A saved delivery timestamp is invalid."); }
        await transaction.CommitAsync(token);
        var pending = tables.PendingCount > 0 || reports.PendingCount > 0 || lifecycle.PendingCount > 0 || replayPending > 0;
        var history = tables.DeliveredCount > 0 || reports.DeliveredCount > 0 || lifecycle.DeliveredCount > 0 || replayCompleted > 0;
        var last = Latest(tables.LastConfirmedAt, reports.LastConfirmedAt, lifecycle.LastConfirmedAt, replayLast);
        return new(branchId, now, pending ? "Pending" : history ? "Current" : "NoHistory", last,
            tables, reports, replayPending, replayCompleted, replayLast, replayOldest, lifecycle);
    }

    private static async Task<DeliveryChannelSummary> ReadChannelAsync(SqliteConnection connection, SqliteTransaction transaction,
        string branchId, DateTimeOffset now, bool reports, CancellationToken token, bool lifecycle = false)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var source = lifecycle ? """
            SELECT delivery_utc_key(created_at) AS created_key,
                   delivery_utc_key(delivered_at) AS ack_key,
                   delivery_utc_key(lease_expires_at) AS lease_key,
                   delivery_utc_key(available_at) AS delay_key,
                   last_failure IS NOT NULL AND last_failure NOT IN
                       ('TransportUnavailable','CentralRejected','AcknowledgmentUnavailable','ParentUnavailable') AS unknown
            FROM closing_report_context_outbox WHERE branch_id=$branch
            """ : reports ? """
            SELECT delivery_utc_key(created_at) AS created_key,
                   delivery_utc_key(delivered_at) AS ack_key,
                   delivery_utc_key(lease_expires_at) AS lease_key,
                   delivery_utc_key(available_at) AS delay_key,
                   last_failure IS NOT NULL AND last_failure NOT IN
                       ('TransportUnavailable','CentralRejected','AcknowledgmentUnavailable') AS unknown
            FROM closing_report_outbox WHERE branch_id=$branch
            """ : """
            SELECT delivery_utc_key(created_at) AS created_key,
                   delivery_utc_key(successfully_sent_at) AS ack_key,
                   delivery_utc_key(lease_expires_at) AS lease_key,
                   delivery_utc_key(next_retry_at) AS delay_key,
                   processing_status IS NULL OR processing_status NOT IN ('Pending','Processing','Failed') AS unknown
            FROM cloud_sync_outbox WHERE branch_id=$branch
            """;
        command.CommandText = $"""
            WITH scoped AS MATERIALIZED ({source})
            SELECT COUNT(*), COUNT(*) FILTER (WHERE ack_key IS NULL), COUNT(*) FILTER (WHERE ack_key IS NOT NULL),
                   COUNT(*) FILTER (WHERE ack_key IS NULL AND lease_key > $now),
                   COUNT(*) FILTER (WHERE ack_key IS NULL AND lease_key IS NULL AND delay_key > $now),
                   COUNT(*) FILTER (WHERE ack_key IS NULL AND lease_key <= $now),
                   COUNT(*) FILTER (WHERE ack_key IS NULL AND unknown),
                   MAX(ack_key), MIN(CASE WHEN ack_key IS NULL THEN created_key END)
            FROM scoped;
            """;
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$now", now.ToString(KeyFormat, CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), OptionalTimestamp(reader, 7), OptionalTimestamp(reader, 8));
    }

    private static DateTimeOffset? OptionalTimestamp(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : DateTimeOffset.ParseExact(reader.GetString(ordinal), KeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    private static DateTimeOffset? Latest(params DateTimeOffset?[] values) => values.Max();
    private static bool TryTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        return
        Regex.IsMatch(value, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) &&
        DateTimeOffset.TryParseExact(value, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp);
    }
}
