using Microsoft.Data.Sqlite;
using Pos.Application.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCloudOutboxLeaseRenewalStoreTests
{
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Active_matching_lease_is_extended_without_changing_claim_time()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        var renewed = await store.RenewAsync(
            lease,
            ClaimedAt.AddSeconds(10),
            TimeSpan.FromSeconds(30));

        Assert.Equal(ClaimedAt, renewed.ClaimedAt);
        Assert.Equal(ClaimedAt.AddSeconds(40), renewed.LeaseExpiresAt);
        var row = await database.ReadLeaseAsync();
        Assert.Equal("WORKER-01", row.LeaseOwner);
        Assert.Equal("2026-10-24T12:00:00.0000000+00:00", row.ClaimedAt);
        Assert.Equal(
            "2026-10-24T12:00:40.0000000+00:00",
            row.LeaseExpiresAt);
    }

    [Fact]
    public async Task Expired_lease_cannot_be_renewed()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(
            lease,
            lease.LeaseExpiresAt,
            TimeSpan.FromSeconds(30)));

        Assert.Equal(
            "2026-10-24T12:00:30.0000000+00:00",
            (await database.ReadLeaseAsync()).LeaseExpiresAt);
    }

    [Fact]
    public async Task Replaced_owner_token_cannot_renew_lease()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        var stale = CopyLease(lease, leaseOwner: "OTHER-WORKER");
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(
            stale,
            ClaimedAt.AddSeconds(10),
            TimeSpan.FromSeconds(30)));

        Assert.Equal("WORKER-01", (await database.ReadLeaseAsync()).LeaseOwner);
    }

    [Fact]
    public async Task Stale_expiry_token_cannot_renew_lease()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        var stale = CopyLease(
            lease,
            leaseExpiresAt: lease.LeaseExpiresAt.AddSeconds(-1));
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(
            stale,
            ClaimedAt.AddSeconds(10),
            TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Confirmed_event_cannot_be_renewed()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        await database.ConfirmAsync();
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RenewAsync(
            lease,
            ClaimedAt.AddSeconds(10),
            TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Concurrent_renewal_allows_one_exact_token_update()
    {
        await using var database = new RenewalDatabase();
        var lease = await database.CreateLeaseAsync();
        var first = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);
        var second = new SqliteBranchCloudOutboxLeaseRenewalStore(database.Path);

        var attempts = await Task.WhenAll(
            CaptureAsync(() => first.RenewAsync(
                lease,
                ClaimedAt.AddSeconds(10),
                TimeSpan.FromSeconds(30))),
            CaptureAsync(() => second.RenewAsync(
                lease,
                ClaimedAt.AddSeconds(10),
                TimeSpan.FromSeconds(30))));

        Assert.Single(attempts, attempt => attempt.Lease is not null);
        Assert.Single(
            attempts,
            attempt => attempt.Error is InvalidOperationException);
    }

    [Fact]
    public async Task Non_extending_expiry_is_rejected_before_database_access()
    {
        var lease = LeaseFixture();
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore("missing.db");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.RenewAsync(
                lease,
                ClaimedAt.AddSeconds(10),
                TimeSpan.FromSeconds(20)));

        Assert.Equal("leaseDuration", exception.ParamName);
    }

    [Fact]
    public async Task Missing_lease_is_rejected()
    {
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore("missing.db");

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.RenewAsync(
            null!,
            ClaimedAt,
            TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Non_utc_renewal_time_is_rejected()
    {
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore("missing.db");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.RenewAsync(
                LeaseFixture(),
                ClaimedAt.ToOffset(TimeSpan.FromHours(7)),
                TimeSpan.FromSeconds(40)));

        Assert.Equal("renewedAt", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_duration_is_rejected(int seconds)
    {
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore("missing.db");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.RenewAsync(
                LeaseFixture(),
                ClaimedAt,
                TimeSpan.FromSeconds(seconds)));

        Assert.Equal("leaseDuration", exception.ParamName);
    }

    [Fact]
    public async Task Missing_database_is_not_created()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-renewal-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var store = new SqliteBranchCloudOutboxLeaseRenewalStore(path);

        await Assert.ThrowsAsync<SqliteException>(() => store.RenewAsync(
            LeaseFixture(),
            ClaimedAt.AddSeconds(10),
            TimeSpan.FromSeconds(30)));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string databasePath)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchCloudOutboxLeaseRenewalStore(databasePath));
    }

    private static async Task<RenewalAttempt> CaptureAsync(
        Func<Task<BranchCloudOutboxLease>> operation)
    {
        try
        {
            return new RenewalAttempt(await operation(), null);
        }
        catch (Exception exception)
        {
            return new RenewalAttempt(null, exception);
        }
    }

    private static BranchCloudOutboxLease CopyLease(
        BranchCloudOutboxLease lease,
        string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null) =>
        new(
            lease.OutboxId,
            lease.BranchId,
            lease.BranchSequence,
            lease.RequestId,
            lease.TableSessionId,
            lease.Action,
            lease.TableVersion,
            lease.CreatedAt,
            lease.RetryCount,
            leaseOwner ?? lease.LeaseOwner,
            lease.ClaimedAt,
            leaseExpiresAt ?? lease.LeaseExpiresAt);

    private static BranchCloudOutboxLease LeaseFixture() =>
        new(
            "OUTBOX-01",
            "BRANCH-01",
            1,
            "REQUEST-01",
            Pos.Domain.TableSessions.TableSessionId.From("SESSION-01"),
            Pos.Domain.TableSessions.TableSessionAction.SubmitOrder,
            3,
            ClaimedAt.AddMinutes(-1),
            2,
            "WORKER-01",
            ClaimedAt,
            ClaimedAt.AddSeconds(30));

    private sealed class RenewalDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-cloud-renewal-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task<BranchCloudOutboxLease> CreateLeaseAsync()
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            await using var connection = await OpenAsync();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO cloud_sync_outbox (
                        outbox_id, branch_id, request_id, table_session_id,
                        action, table_version, created_at, processing_status,
                        retry_count, next_retry_at, last_error,
                        successfully_sent_at, branch_sequence)
                    VALUES (
                        'OUTBOX-01', 'BRANCH-01', 'REQUEST-01', 'SESSION-01',
                        'SubmitOrder', 3,
                        '2026-10-24T11:00:00.0000000+00:00', 'Pending',
                        2, NULL, NULL, NULL, 1);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            return (await new SqliteBranchCloudOutboxClaimStore(Path).ClaimNextAsync(
                "BRANCH-01",
                "WORKER-01",
                ClaimedAt,
                TimeSpan.FromSeconds(30)))!;
        }

        public async Task ConfirmAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE cloud_sync_outbox
                SET successfully_sent_at =
                    '2026-10-24T12:00:05.0000000+00:00'
                WHERE outbox_id = 'OUTBOX-01';
                """;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<LeaseRow> ReadLeaseAsync()
        {
            await using var connection = await OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT lease_owner, claimed_at, lease_expires_at
                FROM cloud_sync_outbox
                WHERE outbox_id = 'OUTBOX-01';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return new LeaseRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2));
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
                    ForeignKeys = true,
                    Pooling = false,
                    DefaultTimeout = 5
                }.ToString());
            await connection.OpenAsync();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record LeaseRow(
        string LeaseOwner,
        string ClaimedAt,
        string LeaseExpiresAt);

    private sealed record RenewalAttempt(
        BranchCloudOutboxLease? Lease,
        Exception? Error);
}
