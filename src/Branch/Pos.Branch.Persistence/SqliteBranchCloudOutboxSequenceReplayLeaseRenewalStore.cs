using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore :
    IBranchCloudOutboxSequenceReplayLeaseRenewalStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxSequenceReplayLeaseRenewalStore(string databasePath)
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

    public async Task<BranchCloudOutboxSequenceReplayLease> RenewAsync(
        BranchCloudOutboxSequenceReplayLease lease,
        DateTimeOffset renewedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var renewed = lease.Renew(renewedAt, leaseDuration);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE cloud_sync_sequence_replay_leases
            SET lease_expires_at = $newExpiry
            WHERE recovery_id = $recoveryId
              AND lease_owner = $owner
              AND claimed_at = $claimedAt
              AND lease_expires_at = $currentExpiry
              AND EXISTS (
                  SELECT 1 FROM cloud_sync_sequence_replay_requests r
                  WHERE r.recovery_id = cloud_sync_sequence_replay_leases.recovery_id
                    AND r.branch_id = $branchId
                    AND r.expected_outbox_id = $outboxId
                    AND r.expected_branch_sequence = $sequence
                    AND r.completed_at IS NULL);
            """;
        command.Parameters.AddWithValue("$newExpiry", Format(renewed.LeaseExpiresAt));
        command.Parameters.AddWithValue("$recoveryId", lease.RecoveryId);
        command.Parameters.AddWithValue("$owner", lease.LeaseOwner);
        command.Parameters.AddWithValue("$claimedAt", Format(lease.ClaimedAt));
        command.Parameters.AddWithValue("$currentExpiry", Format(lease.LeaseExpiresAt));
        command.Parameters.AddWithValue("$branchId", lease.BranchId);
        command.Parameters.AddWithValue("$outboxId", lease.OutboxId);
        command.Parameters.AddWithValue("$sequence", lease.BranchSequence);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The replay lease is completed, replaced, or missing.");
        }
        return renewed;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
