using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchEmployeeSessionIssuanceStoreTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 21, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Current_schema_creates_employee_session_table_indexes_and_guards()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();

        Assert.Equal(18L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_employee_sessions';
            """));
        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN (
                  'ix_branch_employee_sessions_lookup',
                  'ix_branch_employee_sessions_principal');
            """));
        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'trigger'
              AND name IN (
                  'branch_employee_session_identity_immutable',
                  'branch_employee_session_terminal_status_immutable');
            """));
    }

    [Fact]
    public async Task Create_persists_active_session_hash_and_lifecycle_metadata()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        await database.SeedDeviceAsync();
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var registration = Registration(issued);
        var store = new SqliteBranchEmployeeSessionIssuanceStore(database.Path);

        await store.CreateAsync(registration);

        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_employee_sessions
            WHERE session_id = $sessionId
              AND branch_id = 'BRANCH-01'
              AND device_id = 'DEVICE-01'
              AND employee_id = 'EMPLOYEE-01'
              AND issued_at = '2026-10-21T08:30:00.0000000+00:00'
              AND expires_at = '2026-10-21T16:30:00.0000000+00:00'
              AND lifecycle_status = 'Active'
              AND revoked_at IS NULL
              AND revocation_reason IS NULL;
            """, ("$sessionId", issued.SessionId)));
        var storedHash = await database.BlobAsync(
            "SELECT secret_hash FROM branch_employee_sessions WHERE session_id = $sessionId;",
            ("$sessionId", issued.SessionId));
        Assert.Equal(issued.CopySecretHash(), storedHash);
        Assert.False(storedHash.AsSpan().SequenceEqual(
            System.Text.Encoding.UTF8.GetBytes(issued.Token)));
    }

    [Fact]
    public async Task Duplicate_session_identifier_fails_without_second_row()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        await database.SeedDeviceAsync();
        var registration = Registration(BranchEmployeeSessionTokenFormat.Generate());
        var store = new SqliteBranchEmployeeSessionIssuanceStore(database.Path);
        await store.CreateAsync(registration);

        await Assert.ThrowsAsync<SqliteException>(() => store.CreateAsync(registration));

        Assert.Equal(1L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_employee_sessions;"));
    }

    [Fact]
    public async Task Missing_device_fails_foreign_key_check()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchEmployeeSessionIssuanceStore(database.Path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.CreateAsync(Registration(BranchEmployeeSessionTokenFormat.Generate())));

        Assert.Equal(0L, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM branch_employee_sessions;"));
    }

    [Fact]
    public async Task Multiple_active_sessions_are_not_restricted_by_storage_policy()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        await database.SeedDeviceAsync();
        var store = new SqliteBranchEmployeeSessionIssuanceStore(database.Path);

        await store.CreateAsync(Registration(BranchEmployeeSessionTokenFormat.Generate()));
        await store.CreateAsync(Registration(BranchEmployeeSessionTokenFormat.Generate()));

        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_employee_sessions
            WHERE branch_id = 'BRANCH-01'
              AND device_id = 'DEVICE-01'
              AND employee_id = 'EMPLOYEE-01'
              AND lifecycle_status = 'Active';
            """));
    }

    [Fact]
    public async Task Session_identity_is_immutable_and_revoked_session_cannot_reactivate()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        await database.SeedDeviceAsync();
        var issued = BranchEmployeeSessionTokenFormat.Generate();
        var store = new SqliteBranchEmployeeSessionIssuanceStore(database.Path);
        await store.CreateAsync(Registration(issued));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            UPDATE branch_employee_sessions
            SET employee_id = 'EMPLOYEE-02'
            WHERE session_id = $sessionId;
            """, ("$sessionId", issued.SessionId)));

        await database.ExecuteAsync("""
            UPDATE branch_employee_sessions
            SET lifecycle_status = 'Revoked',
                revoked_at = '2026-10-21T09:00:00.0000000+00:00',
                revocation_reason = 'Synthetic test revocation'
            WHERE session_id = $sessionId;
            """, ("$sessionId", issued.SessionId));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            UPDATE branch_employee_sessions
            SET lifecycle_status = 'Active',
                revoked_at = NULL,
                revocation_reason = NULL
            WHERE session_id = $sessionId;
            """, ("$sessionId", issued.SessionId)));
    }

    [Fact]
    public async Task Store_does_not_create_a_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-session-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var store = new SqliteBranchEmployeeSessionIssuanceStore(path);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.CreateAsync(Registration(BranchEmployeeSessionTokenFormat.Generate())));

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_database_path_is_rejected(string path)
    {
        Assert.Throws<ArgumentException>(() =>
            new SqliteBranchEmployeeSessionIssuanceStore(path));
    }

    private static BranchEmployeeSessionRegistration Registration(
        IssuedBranchEmployeeSessionToken issued) =>
        new(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            issued.SessionId,
            IssuedAt,
            IssuedAt.AddHours(8),
            issued.CopySecretHash());

    private sealed class SessionDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-session-issuance-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public Task SeedDeviceAsync() => ExecuteAsync("""
            INSERT INTO branch_devices (
                branch_id, device_id, display_name, lifecycle_status,
                paired_by_employee_id, paired_at)
            VALUES (
                'BRANCH-01', 'DEVICE-01', 'Synthetic test device', 'Active',
                'EMPLOYEE-01', '2026-10-21T08:00:00.0000000+00:00');
            """);

        public async Task<T> ScalarAsync<T>(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, parameters);
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
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, parameters);
            return (byte[])(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Blob query returned null."));
        }

        public async Task ExecuteAsync(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameters(command, parameters);
            await command.ExecuteNonQueryAsync();
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

        private static void AddParameters(
            SqliteCommand command,
            IEnumerable<(string Name, object? Value)> parameters)
        {
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(
                    parameter.Name,
                    parameter.Value ?? DBNull.Value);
            }
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
