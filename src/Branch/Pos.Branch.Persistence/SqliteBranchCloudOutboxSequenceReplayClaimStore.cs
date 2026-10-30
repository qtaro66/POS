using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxSequenceReplayClaimStore :
    IBranchCloudOutboxSequenceReplayClaimStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxSequenceReplayClaimStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<BranchCloudOutboxSequenceReplayLease?> ClaimNextAsync(
        string branchId,
        string leaseOwner,
        DateTimeOffset claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (claimedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The claim timestamp must be UTC.", nameof(claimedAt));
        }
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var expiresAt = claimedAt.Add(leaseDuration);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        BranchCloudOutboxSequenceReplayLease? lease;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT r.recovery_id, o.outbox_id, o.branch_id, o.branch_sequence,
                       o.request_id, o.table_session_id, o.action, o.table_version,
                       o.created_at, COALESCE(l.retry_count, 0),
                       l.next_retry_at, l.lease_owner, l.lease_expires_at
                FROM cloud_sync_sequence_replay_requests r
                JOIN cloud_sync_outbox o ON o.branch_id = r.branch_id
                    AND o.outbox_id = r.expected_outbox_id
                    AND o.branch_sequence = r.expected_branch_sequence
                LEFT JOIN cloud_sync_sequence_replay_leases l
                    ON l.recovery_id = r.recovery_id
                WHERE r.branch_id = $branchId AND r.completed_at IS NULL
                ORDER BY r.expected_branch_sequence, r.requested_at, r.recovery_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$branchId", branchId.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            if ((!reader.IsDBNull(10) && ParseUtc(reader.GetString(10)) > claimedAt)
                || (!reader.IsDBNull(11) && ParseUtc(reader.GetString(12)) > claimedAt))
            {
                return null;
            }

            var actionText = reader.GetString(6);
            if (!Enum.TryParse<TableSessionAction>(actionText, false, out var action)
                || !Enum.IsDefined(action))
            {
                throw new InvalidOperationException("The replay event action is not supported.");
            }
            lease = new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetString(4),
                TableSessionId.From(reader.GetString(5)), action, reader.GetInt64(7),
                ParseUtc(reader.GetString(8)), reader.GetInt32(9),
                leaseOwner.Trim(), claimedAt, expiresAt);
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            INSERT INTO cloud_sync_sequence_replay_leases (
                recovery_id, lease_owner, claimed_at, lease_expires_at)
            VALUES ($recoveryId, $owner, $claimedAt, $expiresAt)
            ON CONFLICT (recovery_id) DO UPDATE SET
                lease_owner = excluded.lease_owner,
                claimed_at = excluded.claimed_at,
                lease_expires_at = excluded.lease_expires_at;
            """;
        update.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
        update.Parameters.AddWithValue("$owner", lease.LeaseOwner);
        update.Parameters.AddWithValue("$claimedAt", claimedAt.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O", CultureInfo.InvariantCulture));
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return lease;
    }

    private static DateTimeOffset ParseUtc(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var timestamp)
        && timestamp.Offset == TimeSpan.Zero
            ? timestamp
            : throw new InvalidOperationException("The replay timestamp must be valid UTC.");
}
