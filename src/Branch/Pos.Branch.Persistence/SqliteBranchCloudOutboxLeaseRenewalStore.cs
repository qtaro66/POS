using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCloudOutboxLeaseRenewalStore :
    IBranchCloudOutboxLeaseRenewalStore
{
    private readonly string _connectionString;

    public SqliteBranchCloudOutboxLeaseRenewalStore(string databasePath)
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

    public async Task<BranchCloudOutboxLease> RenewAsync(
        BranchCloudOutboxLease lease,
        DateTimeOffset renewedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (renewedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The renewal timestamp must be UTC.",
                nameof(renewedAt));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        var leaseExpiresAt = renewedAt.Add(leaseDuration);
        if (leaseExpiresAt <= lease.LeaseExpiresAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "The renewed lease must extend the current lease expiry.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE cloud_sync_outbox
            SET lease_expires_at = $leaseExpiresAt
            WHERE branch_id = $branchId
              AND outbox_id = $outboxId
              AND branch_sequence = $branchSequence
              AND successfully_sent_at IS NULL
              AND lease_owner = $leaseOwner
              AND claimed_at = $claimedAt
              AND lease_expires_at = $currentLeaseExpiresAt
              AND julianday(lease_expires_at) > julianday($renewedAt);
            """;
        command.Parameters.AddWithValue("$branchId", lease.BranchId);
        command.Parameters.AddWithValue("$outboxId", lease.OutboxId);
        command.Parameters.AddWithValue(
            "$branchSequence",
            lease.BranchSequence);
        command.Parameters.AddWithValue("$leaseOwner", lease.LeaseOwner);
        command.Parameters.AddWithValue(
            "$claimedAt",
            FormatTimestamp(lease.ClaimedAt));
        command.Parameters.AddWithValue(
            "$currentLeaseExpiresAt",
            FormatTimestamp(lease.LeaseExpiresAt));
        command.Parameters.AddWithValue(
            "$renewedAt",
            FormatTimestamp(renewedAt));
        command.Parameters.AddWithValue(
            "$leaseExpiresAt",
            FormatTimestamp(leaseExpiresAt));

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The Cloud outbox lease is expired, replaced, confirmed, or missing.");
        }

        return new BranchCloudOutboxLease(
            lease.OutboxId,
            lease.BranchId,
            lease.BranchSequence,
            lease.RequestId,
            lease.TableSessionId,
            lease.Action,
            lease.TableVersion,
            lease.CreatedAt,
            lease.RetryCount,
            lease.LeaseOwner,
            lease.ClaimedAt,
            leaseExpiresAt);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
