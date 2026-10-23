using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchDeviceCredentialValidatorTests
{
    [Fact]
    public async Task Current_schema_creates_device_credential_tables_and_guards()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();

        Assert.Equal(15L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('branch_devices', 'branch_device_credentials');
            """));
        Assert.Equal(5L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'trigger'
              AND name IN (
                  'branch_device_terminal_status_immutable',
                  'branch_device_history_preserved',
                  'branch_device_credential_identity_immutable',
                  'branch_device_credential_terminal_status_immutable',
                  'branch_device_credential_history_preserved');
            """));
    }

    [Fact]
    public async Task Active_device_and_matching_credential_return_verified_identity()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(issued);
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (parsed)
        {
            var presentedSecret = parsed.Secret.ToArray();

            var result = await validator.ValidateActiveAsync(
                parsed.CredentialId,
                presentedSecret);

            var device = Assert.IsType<VerifiedBranchDevice>(result);
            Assert.Equal("BRANCH-01", device.BranchId);
            Assert.Equal("DEVICE-01", device.DeviceId);
            Assert.Equal(parsed.Secret.ToArray(), presentedSecret);
        }
    }

    [Fact]
    public async Task Wrong_secret_returns_unauthenticated_result()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(issued);
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (parsed)
        {
            var wrongSecret = parsed.Secret.ToArray();
            wrongSecret[0] ^= byte.MaxValue;

            var result = await validator.ValidateActiveAsync(
                parsed.CredentialId,
                wrongSecret);

            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData("Revoked")]
    [InlineData("Lost")]
    public async Task Inactive_device_returns_same_unauthenticated_result(string status)
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(issued, deviceStatus: status);
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (parsed)
        {
            var result = await validator.ValidateActiveAsync(
                parsed.CredentialId,
                parsed.Secret);

            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData("Revoked")]
    [InlineData("Lost")]
    public async Task Inactive_credential_returns_same_unauthenticated_result(string status)
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(issued, credentialStatus: status);
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);
        Assert.True(BranchDeviceCredentialFormat.TryParse(issued.Token, out var parsed));
        using (parsed)
        {
            var result = await validator.ValidateActiveAsync(
                parsed.CredentialId,
                parsed.Secret);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task Missing_credential_and_unexpected_secret_length_fail_closed()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);

        var missing = await validator.ValidateActiveAsync(
            "AAAAAAAAAAAAAAAAAAAAAA",
            new byte[BranchDeviceCredentialFormat.SecretByteLength]);
        var wrongLength = await validator.ValidateActiveAsync(
            "AAAAAAAAAAAAAAAAAAAAAA",
            new byte[BranchDeviceCredentialFormat.SecretByteLength - 1]);

        Assert.Null(missing);
        Assert.Null(wrongLength);
    }

    [Fact]
    public async Task Terminal_device_and_credential_states_cannot_be_reactivated()
    {
        await using var deviceDatabase = new CredentialDatabase();
        await deviceDatabase.InitializeAsync();
        var deviceCredential = BranchDeviceCredentialFormat.Generate();
        await deviceDatabase.SeedAsync(deviceCredential, deviceStatus: "Revoked");

        await Assert.ThrowsAsync<SqliteException>(() => deviceDatabase.ExecuteAsync("""
            UPDATE branch_devices
            SET lifecycle_status = 'Active',
                retired_by_employee_id = NULL,
                retired_at = NULL,
                retirement_reason = NULL
            WHERE branch_id = 'BRANCH-01' AND device_id = 'DEVICE-01';
            """));

        await using var credentialDatabase = new CredentialDatabase();
        await credentialDatabase.InitializeAsync();
        var retiredCredential = BranchDeviceCredentialFormat.Generate();
        await credentialDatabase.SeedAsync(
            retiredCredential,
            credentialStatus: "Revoked");

        await Assert.ThrowsAsync<SqliteException>(() => credentialDatabase.ExecuteAsync("""
            UPDATE branch_device_credentials
            SET lifecycle_status = 'Active',
                retired_at = NULL,
                retirement_reason = NULL
            WHERE credential_id = $credentialId;
            """, ("$credentialId", retiredCredential.CredentialId)));
    }

    [Fact]
    public async Task Only_one_active_credential_is_allowed_per_device()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        await database.SeedAsync(BranchDeviceCredentialFormat.Generate());
        var second = BranchDeviceCredentialFormat.Generate();

        await Assert.ThrowsAsync<SqliteException>(() =>
            database.InsertCredentialAsync(second, "Active"));
    }

    [Fact]
    public async Task Rotation_replaces_old_credential_and_authenticates_only_the_new_one()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var previous = BranchDeviceCredentialFormat.Generate();
        var replacement = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(previous);
        await database.RotateAsync(previous.CredentialId, replacement);
        var validator = new SqliteBranchDeviceCredentialValidator(database.Path);
        Assert.True(BranchDeviceCredentialFormat.TryParse(previous.Token, out var oldToken));
        Assert.True(BranchDeviceCredentialFormat.TryParse(replacement.Token, out var newToken));
        using (oldToken)
        using (newToken)
        {
            var oldResult = await validator.ValidateActiveAsync(
                oldToken.CredentialId,
                oldToken.Secret);
            var newResult = await validator.ValidateActiveAsync(
                newToken.CredentialId,
                newToken.Secret);

            Assert.Null(oldResult);
            var device = Assert.IsType<VerifiedBranchDevice>(newResult);
            Assert.Equal("BRANCH-01", device.BranchId);
            Assert.Equal("DEVICE-01", device.DeviceId);
        }
    }

    [Fact]
    public async Task Device_and_credential_history_cannot_be_deleted()
    {
        await using var database = new CredentialDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedAsync(issued, credentialStatus: "Revoked");

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            DELETE FROM branch_device_credentials WHERE credential_id = $credentialId;
            """, ("$credentialId", issued.CredentialId)));
        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            DELETE FROM branch_devices
            WHERE branch_id = 'BRANCH-01' AND device_id = 'DEVICE-01';
            """));
    }

    [Fact]
    public async Task Validator_is_read_only_and_does_not_create_a_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-device-credential-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var validator = new SqliteBranchDeviceCredentialValidator(path);

        await Assert.ThrowsAsync<SqliteException>(() => validator.ValidateActiveAsync(
            "AAAAAAAAAAAAAAAAAAAAAA",
            new byte[BranchDeviceCredentialFormat.SecretByteLength]));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_credential_identifier_is_rejected(string credentialId)
    {
        var validator = new SqliteBranchDeviceCredentialValidator("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(() => validator.ValidateActiveAsync(
            credentialId,
            new byte[BranchDeviceCredentialFormat.SecretByteLength]));
    }

    private sealed class CredentialDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-device-credential-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            var store = new SqliteTableSessionCommandStore(Path);
            await store.InitializeAsync();
        }

        public async Task SeedAsync(
            IssuedBranchDeviceCredential credential,
            string deviceStatus = "Active",
            string credentialStatus = "Active")
        {
            var deviceIsActive = deviceStatus == "Active";
            await ExecuteAsync("""
                INSERT INTO branch_devices (
                    branch_id, device_id, display_name, lifecycle_status,
                    paired_by_employee_id, paired_at, last_employee_id, last_seen_at,
                    retired_by_employee_id, retired_at, retirement_reason)
                VALUES (
                    'BRANCH-01', 'DEVICE-01', 'Synthetic test device', $deviceStatus,
                    'EMPLOYEE-01', '2026-10-19T00:00:00.0000000+00:00', NULL, NULL,
                    $retiredBy, $retiredAt, $retirementReason);
                """,
                ("$deviceStatus", deviceStatus),
                ("$retiredBy", deviceIsActive ? null : "EMPLOYEE-02"),
                ("$retiredAt", deviceIsActive ? null : "2026-10-19T01:00:00.0000000+00:00"),
                ("$retirementReason", deviceIsActive ? null : "Synthetic test retirement"));
            await InsertCredentialAsync(credential, credentialStatus);
        }

        public Task InsertCredentialAsync(
            IssuedBranchDeviceCredential credential,
            string credentialStatus)
        {
            var isActive = credentialStatus == "Active";
            return ExecuteAsync("""
                INSERT INTO branch_device_credentials (
                    credential_id, branch_id, device_id, secret_hash,
                    lifecycle_status, issued_at, retired_at, retirement_reason,
                    replaced_by_credential_id)
                VALUES (
                    $credentialId, 'BRANCH-01', 'DEVICE-01', $secretHash,
                    $credentialStatus, '2026-10-19T00:00:00.0000000+00:00',
                    $retiredAt, $retirementReason, NULL);
                """,
                ("$credentialId", credential.CredentialId),
                ("$secretHash", credential.CopySecretHash()),
                ("$credentialStatus", credentialStatus),
                ("$retiredAt", isActive ? null : "2026-10-19T01:00:00.0000000+00:00"),
                ("$retirementReason", isActive ? null : "Synthetic test retirement"));
        }

        public async Task RotateAsync(
            string previousCredentialId,
            IssuedBranchDeviceCredential replacement)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction(deferred: false);

            using (var retire = connection.CreateCommand())
            {
                retire.Transaction = transaction;
                retire.CommandText = """
                    UPDATE branch_device_credentials
                    SET lifecycle_status = 'Replaced',
                        retired_at = '2026-10-19T01:00:00.0000000+00:00',
                        retirement_reason = 'Synthetic test rotation',
                        replaced_by_credential_id = $replacementCredentialId
                    WHERE credential_id = $previousCredentialId;
                    """;
                retire.Parameters.AddWithValue(
                    "$replacementCredentialId",
                    replacement.CredentialId);
                retire.Parameters.AddWithValue(
                    "$previousCredentialId",
                    previousCredentialId);
                await retire.ExecuteNonQueryAsync();
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO branch_device_credentials (
                        credential_id, branch_id, device_id, secret_hash,
                        lifecycle_status, issued_at, retired_at, retirement_reason,
                        replaced_by_credential_id)
                    VALUES (
                        $credentialId, 'BRANCH-01', 'DEVICE-01', $secretHash,
                        'Active', '2026-10-19T01:00:00.0000000+00:00',
                        NULL, NULL, NULL);
                    """;
                insert.Parameters.AddWithValue(
                    "$credentialId",
                    replacement.CredentialId);
                insert.Parameters.AddWithValue(
                    "$secretHash",
                    replacement.CopySecretHash());
                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Scalar query returned null.");
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public async Task ExecuteAsync(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(
                    parameter.Name,
                    parameter.Value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
    }
}
