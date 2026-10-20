using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchPairingRedemptionStoreTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valid_challenge_atomically_creates_device_credential_and_consumes_token()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        var service = Service(database, IssuedAt.AddMinutes(1));

        var result = await service.RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Dining Tablet 01");

        var redeemed = Assert.IsType<RedeemedBranchDevice>(result);
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $pairingId
              AND lifecycle_status = 'Consumed'
              AND terminal_at = '2026-10-20T03:01:00.0000000+00:00'
              AND terminal_reason = 'Consumed'
              AND replaced_by_pairing_id IS NULL;
            """, ("$pairingId", pairing.PairingId)));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_devices
            WHERE branch_id = 'BRANCH-01'
              AND device_id = $deviceId
              AND display_name = 'Dining Tablet 01'
              AND lifecycle_status = 'Active'
              AND paired_by_employee_id = 'EMPLOYEE-01'
              AND paired_at = '2026-10-20T03:01:00.0000000+00:00';
            """, ("$deviceId", redeemed.DeviceId)));

        Assert.True(BranchDeviceCredentialFormat.TryParse(
            redeemed.DeviceCredential,
            out var parsedCredential));
        using var credential = Assert.IsType<ParsedBranchDeviceCredential>(parsedCredential);
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_device_credentials
            WHERE credential_id = $credentialId
              AND branch_id = 'BRANCH-01'
              AND device_id = $deviceId
              AND lifecycle_status = 'Active'
              AND issued_at = '2026-10-20T03:01:00.0000000+00:00';
            """,
            ("$credentialId", credential.CredentialId),
            ("$deviceId", redeemed.DeviceId)));
        Assert.True(BranchDeviceCredentialFormat.VerifySecretHash(
            await database.BlobAsync(
                "SELECT secret_hash FROM branch_device_credentials WHERE credential_id = $id;",
                ("$id", credential.CredentialId)),
            credential.Secret.Span));
    }

    [Fact]
    public async Task Wrong_secret_returns_null_without_writes_or_state_change()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        var wrong = BranchPairingTokenFormat.Generate();

        var result = await Service(database, IssuedAt.AddMinutes(1)).RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            wrong.Token,
            "Tablet 01");

        Assert.Null(result);
        await AssertNoDeviceWritesAndActiveAsync(database, pairing.PairingId);
    }

    [Fact]
    public async Task Expiry_boundary_is_strict_and_leaves_challenge_active()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);

        var result = await Service(database, IssuedAt.AddMinutes(5)).RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 01");

        Assert.Null(result);
        await AssertNoDeviceWritesAndActiveAsync(database, pairing.PairingId);
    }

    [Fact]
    public async Task Replaced_or_cross_branch_challenge_has_one_null_result()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var replaced = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt.AddMinutes(1));

        var replacedResult = await Service(database, IssuedAt.AddMinutes(2)).RedeemAsync(
            "BRANCH-01",
            replaced.PairingId,
            replaced.Token,
            "Tablet 01");
        var crossBranchResult = await Service(database, IssuedAt.AddMinutes(2)).RedeemAsync(
            "BRANCH-OTHER",
            replaced.PairingId,
            replaced.Token,
            "Tablet 02");

        Assert.Null(replacedResult);
        Assert.Null(crossBranchResult);
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_devices;"));
    }

    [Fact]
    public async Task Consumed_challenge_cannot_create_second_device()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        var service = Service(database, IssuedAt.AddMinutes(1));

        var first = await service.RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 01");
        var second = await service.RedeemAsync(
            "BRANCH-01",
            pairing.PairingId,
            pairing.Token,
            "Tablet 02");

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_devices;"));
        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_device_credentials;"));
    }

    [Fact]
    public async Task Credential_conflict_rolls_back_device_insert_and_token_consumption()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        var credential = BranchDeviceCredentialFormat.Generate();
        await database.InsertExistingCredentialAsync(credential, IssuedAt);
        var registration = new BranchPairingRedemptionRegistration(
            "BRANCH-01",
            pairing.PairingId,
            BranchDeviceIdentifierFormat.Generate(),
            "Tablet Conflict",
            credential.CredentialId,
            IssuedAt.AddMinutes(1),
            credential.CopySecretHash());
        Assert.True(BranchPairingTokenFormat.TryParse(pairing.Token, out var parsed));
        using var pairingToken = Assert.IsType<ParsedBranchPairingToken>(parsed);

        await Assert.ThrowsAsync<SqliteException>(() =>
            database.Store.TryRedeemAsync(registration, pairingToken.Secret));

        Assert.Equal(0L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_devices WHERE display_name = 'Tablet Conflict';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $pairingId AND lifecycle_status = 'Active';
            """, ("$pairingId", pairing.PairingId)));
    }

    [Fact]
    public async Task Pre_cancelled_operation_does_not_write()
    {
        await using var database = await PairingDatabase.CreateAsync();
        var pairing = await database.IssueAsync("BRANCH-01", "EMPLOYEE-01", IssuedAt);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(database, IssuedAt.AddMinutes(1)).RedeemAsync(
                "BRANCH-01",
                pairing.PairingId,
                pairing.Token,
                "Tablet 01",
                source.Token));

        await AssertNoDeviceWritesAndActiveAsync(database, pairing.PairingId);
    }

    private static BranchPairingRedemptionService Service(
        PairingDatabase database,
        DateTimeOffset utcNow) =>
        new(database.Store, new FixedTimeProvider(utcNow));

    private static async Task AssertNoDeviceWritesAndActiveAsync(
        PairingDatabase database,
        string pairingId)
    {
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_devices;"));
        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_device_credentials;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $pairingId AND lifecycle_status = 'Active';
            """, ("$pairingId", pairingId)));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class PairingDatabase : IDisposable, IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-pairing-redemption-tests",
            Guid.NewGuid().ToString("N"));

        private PairingDatabase()
        {
            Store = new SqliteBranchPairingRedemptionStore(Path);
        }

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public SqliteBranchPairingRedemptionStore Store { get; }

        public static async Task<PairingDatabase> CreateAsync()
        {
            var database = new PairingDatabase();
            await new SqliteTableSessionCommandStore(database.Path).InitializeAsync();
            return database;
        }

        public async Task<IssuedBranchPairingToken> IssueAsync(
            string branchId,
            string employeeId,
            DateTimeOffset issuedAt)
        {
            var token = BranchPairingTokenFormat.Generate();
            await new SqliteBranchPairingTokenIssuanceStore(Path)
                .ReplaceActiveForCreatorAsync(new BranchPairingTokenRegistration(
                    branchId,
                    token.PairingId,
                    employeeId,
                    issuedAt,
                    issuedAt.AddMinutes(5),
                    token.CopySecretHash()));
            return token;
        }

        public async Task InsertExistingCredentialAsync(
            IssuedBranchDeviceCredential credential,
            DateTimeOffset issuedAt)
        {
            await ExecuteAsync("""
                INSERT INTO branch_devices (
                    branch_id, device_id, display_name, lifecycle_status,
                    paired_by_employee_id, paired_at)
                VALUES (
                    'BRANCH-01', 'EXISTING-DEVICE', 'Existing', 'Active',
                    'EMPLOYEE-01', $issuedAt);

                INSERT INTO branch_device_credentials (
                    credential_id, branch_id, device_id, secret_hash,
                    lifecycle_status, issued_at)
                VALUES (
                    $credentialId, 'BRANCH-01', 'EXISTING-DEVICE', $secretHash,
                    'Active', $issuedAt);
                """,
                ("$issuedAt", issuedAt.ToString("O", CultureInfo.InvariantCulture)),
                ("$credentialId", credential.CredentialId),
                ("$secretHash", credential.CopySecretHash()));
        }

        public async Task<T> ScalarAsync<T>(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = Command(connection, sql, parameters);
            var value = await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Scalar query returned null.");
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public async Task<byte[]> BlobAsync(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = Command(connection, sql, parameters);
            return (byte[])(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Blob query returned null."));
        }

        public async Task ExecuteAsync(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = Command(connection, sql, parameters);
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());

        private static SqliteCommand Command(
            SqliteConnection connection,
            string sql,
            IEnumerable<(string Name, object? Value)> parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(
                    parameter.Name,
                    parameter.Value ?? DBNull.Value);
            }

            return command;
        }
    }
}
