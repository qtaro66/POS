using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxClaimStore :
    IBranchCloudOutboxClaimStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxClaimStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<BranchCloudOutboxLease?> ClaimNextAsync(
        string branchId,
        string leaseOwner,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        leaseOwner = RequireIdentifier(leaseOwner, nameof(leaseOwner));
        if (claimedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The claim timestamp must be UTC.",
                nameof(claimedAt));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var leaseExpiresAt = claimedAt.Add(leaseDuration);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        BranchCloudOutboxLease? lease;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                WITH earliest AS (
                    SELECT outbox_id
                    FROM cloud_sync_outbox
                    WHERE branch_id = $branchId
                      AND successfully_sent_at IS NULL
                    ORDER BY branch_sequence
                    LIMIT 1
                )
                UPDATE cloud_sync_outbox
                SET processing_status = 'Processing',
                    lease_owner = $leaseOwner,
                    claimed_at = $claimedAt,
                    lease_expires_at = $leaseExpiresAt
                WHERE branch_id = $branchId
                  AND outbox_id = (SELECT outbox_id FROM earliest)
                  AND successfully_sent_at IS NULL
                  AND (
                      next_retry_at IS NULL
                      OR julianday(next_retry_at) <= julianday($claimedAt))
                  AND (
                      lease_owner IS NULL
                      OR julianday(lease_expires_at) <= julianday($claimedAt))
                RETURNING outbox_id, branch_id, branch_sequence, request_id,
                          table_session_id, action, table_version, created_at,
                          retry_count;
                """;
            command.Parameters.AddWithValue("$branchId", branchId);
            command.Parameters.AddWithValue("$leaseOwner", leaseOwner);
            command.Parameters.AddWithValue(
                "$claimedAt",
                FormatTimestamp(claimedAt));
            command.Parameters.AddWithValue(
                "$leaseExpiresAt",
                FormatTimestamp(leaseExpiresAt));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            lease = await reader.ReadAsync(cancellationToken)
                ? ReadLease(
                    reader,
                    leaseOwner,
                    claimedAt,
                    leaseExpiresAt)
                : null;
        }

        await transaction.CommitAsync(cancellationToken);
        return lease;
    }

    private static BranchCloudOutboxLease ReadLease(
        SqliteDataReader reader,
        string leaseOwner,
        DateTimeOffset claimedAt,
        DateTimeOffset leaseExpiresAt)
    {
        var actionText = reader.GetString(5);
        if (!Enum.TryParse<TableSessionAction>(
                actionText,
                ignoreCase: false,
                out var action) ||
            !Enum.IsDefined(action))
        {
            throw new InvalidOperationException(
                $"Cloud outbox action '{actionText}' is not supported.");
        }

        return new BranchCloudOutboxLease(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            TableSessionId.From(reader.GetString(4)),
            action,
            reader.GetInt64(6),
            ParseUtcTimestamp(reader.GetString(7), "created_at"),
            reader.GetInt32(8),
            leaseOwner,
            claimedAt,
            leaseExpiresAt);
    }

    private static DateTimeOffset ParseUtcTimestamp(
        string value,
        string columnName)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                out var parsed) &&
            parsed.Offset == TimeSpan.Zero)
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Cloud outbox column '{columnName}' is not a valid UTC timestamp.");
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
