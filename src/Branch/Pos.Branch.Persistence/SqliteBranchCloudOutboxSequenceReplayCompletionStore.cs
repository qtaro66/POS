using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxSequenceReplayCompletionStore :
    IBranchCloudOutboxSequenceReplayCompletionStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxSequenceReplayCompletionStore(string databasePath)
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

    public async Task<BranchCloudOutboxSequenceReplayCompletionResult> CompleteAsync(
        BranchCloudOutboxSequenceReplayCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var lease = completion.Lease;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT expected_outbox_id, expected_branch_sequence, completed_at
                FROM cloud_sync_sequence_replay_requests
                WHERE recovery_id = $recoveryId AND branch_id = $branchId;
                """;
            read.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
            read.Parameters.AddWithValue("$branchId", lease.BranchId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return BranchCloudOutboxSequenceReplayCompletionResult.NotFound;
            }
            if (!string.Equals(reader.GetString(0), lease.OutboxId, StringComparison.Ordinal)
                || reader.GetInt64(1) != lease.BranchSequence)
            {
                throw new InvalidOperationException("The replay event identity does not match.");
            }
            if (!reader.IsDBNull(2))
            {
                return BranchCloudOutboxSequenceReplayCompletionResult.AlreadyCompleted;
            }
        }

        using var complete = connection.CreateCommand();
        complete.Transaction = transaction;
        complete.CommandText = """
            UPDATE cloud_sync_sequence_replay_requests
            SET completed_at = $acknowledgedAt
            WHERE recovery_id = $recoveryId AND completed_at IS NULL
              AND EXISTS (
                  SELECT 1 FROM cloud_sync_sequence_replay_leases l
                  WHERE l.recovery_id = cloud_sync_sequence_replay_requests.recovery_id
                    AND l.lease_owner = $owner
                    AND l.claimed_at = $claimedAt
                    AND l.lease_expires_at = $expiresAt);
            """;
        complete.Parameters.AddWithValue("$acknowledgedAt", Format(completion.AcknowledgedAt));
        complete.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
        complete.Parameters.AddWithValue("$owner", lease.LeaseOwner);
        complete.Parameters.AddWithValue("$claimedAt", Format(lease.ClaimedAt));
        complete.Parameters.AddWithValue("$expiresAt", Format(lease.LeaseExpiresAt));
        if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The replay lease is replaced or missing.");
        }

        using var release = connection.CreateCommand();
        release.Transaction = transaction;
        release.CommandText = """
            UPDATE cloud_sync_sequence_replay_leases
            SET lease_owner = NULL, claimed_at = NULL, lease_expires_at = NULL
            WHERE recovery_id = $recoveryId;
            """;
        release.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
        await release.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BranchCloudOutboxSequenceReplayCompletionResult.Completed;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
