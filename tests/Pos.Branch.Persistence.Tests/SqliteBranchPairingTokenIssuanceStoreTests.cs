using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchPairingTokenIssuanceStoreTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 19, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Current_schema_creates_pairing_table_indexes_and_guards()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();

        Assert.Equal(14L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_pairing_tokens';
            """));
        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN (
                  'ux_branch_pairing_tokens_active_creator',
                  'ix_branch_pairing_tokens_lookup');
            """));
        Assert.Equal(3L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'trigger'
              AND name IN (
                  'branch_pairing_token_identity_immutable',
                  'branch_pairing_token_terminal_status_immutable',
                  'branch_pairing_token_history_preserved');
            """));
    }

    [Fact]
    public async Task Store_persists_only_hash_and_lifecycle_metadata()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var issued = BranchPairingTokenFormat.Generate();
        var registration = Registration(issued, "EMPLOYEE-01", IssuedAt);
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);

        await store.ReplaceActiveForCreatorAsync(registration);

        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $pairingId
              AND branch_id = 'BRANCH-01'
              AND created_by_employee_id = 'EMPLOYEE-01'
              AND lifecycle_status = 'Active'
              AND issued_at = '2026-10-19T10:15:00.0000000+00:00'
              AND expires_at = '2026-10-19T10:20:00.0000000+00:00'
              AND terminal_at IS NULL
              AND terminal_reason IS NULL
              AND replaced_by_pairing_id IS NULL;
            """, ("$pairingId", issued.PairingId)));
        Assert.Equal(
            registration.CopySecretHash(),
            await database.BlobAsync(
                "SELECT secret_hash FROM branch_pairing_tokens WHERE pairing_id = $pairingId;",
                ("$pairingId", issued.PairingId)));
        Assert.Equal(0L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM pragma_table_info('branch_pairing_tokens')
            WHERE name IN ('token', 'plaintext_token', 'secret');
            """));
    }

    [Fact]
    public async Task New_challenge_atomically_replaces_previous_active_challenge_for_creator()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var previous = BranchPairingTokenFormat.Generate();
        var replacement = BranchPairingTokenFormat.Generate();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        await store.ReplaceActiveForCreatorAsync(
            Registration(previous, "EMPLOYEE-01", IssuedAt));

        await store.ReplaceActiveForCreatorAsync(
            Registration(replacement, "EMPLOYEE-01", IssuedAt.AddMinutes(1)));

        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE branch_id = 'BRANCH-01'
              AND created_by_employee_id = 'EMPLOYEE-01'
              AND lifecycle_status = 'Active'
              AND pairing_id = $pairingId;
            """, ("$pairingId", replacement.PairingId)));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $previousPairingId
              AND lifecycle_status = 'Replaced'
              AND terminal_at = '2026-10-19T10:16:00.0000000+00:00'
              AND terminal_reason = 'ReplacedByNewChallenge'
              AND replaced_by_pairing_id = $replacementPairingId;
            """,
            ("$previousPairingId", previous.PairingId),
            ("$replacementPairingId", replacement.PairingId)));
    }

    [Fact]
    public async Task Different_creators_can_each_have_one_active_challenge()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        await store.ReplaceActiveForCreatorAsync(Registration(
            BranchPairingTokenFormat.Generate(),
            "EMPLOYEE-01",
            IssuedAt));
        await store.ReplaceActiveForCreatorAsync(Registration(
            BranchPairingTokenFormat.Generate(),
            "EMPLOYEE-02",
            IssuedAt));

        Assert.Equal(2L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE branch_id = 'BRANCH-01' AND lifecycle_status = 'Active';
            """));
    }

    [Fact]
    public async Task Insert_failure_rolls_back_previous_challenge_replacement()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var employeeOne = BranchPairingTokenFormat.Generate();
        var employeeTwo = BranchPairingTokenFormat.Generate();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        await store.ReplaceActiveForCreatorAsync(
            Registration(employeeOne, "EMPLOYEE-01", IssuedAt));
        await store.ReplaceActiveForCreatorAsync(
            Registration(employeeTwo, "EMPLOYEE-02", IssuedAt));
        var conflicting = new BranchPairingTokenRegistration(
            "BRANCH-01",
            employeeTwo.PairingId,
            "EMPLOYEE-01",
            IssuedAt.AddMinutes(1),
            IssuedAt.AddMinutes(6),
            BranchPairingTokenFormat.Generate().CopySecretHash());

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.ReplaceActiveForCreatorAsync(conflicting));

        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM branch_pairing_tokens
            WHERE pairing_id = $pairingId
              AND lifecycle_status = 'Active'
              AND terminal_at IS NULL
              AND replaced_by_pairing_id IS NULL;
            """, ("$pairingId", employeeOne.PairingId)));
    }

    [Fact]
    public async Task Terminal_history_cannot_be_reactivated_or_deleted()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var previous = BranchPairingTokenFormat.Generate();
        var replacement = BranchPairingTokenFormat.Generate();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        await store.ReplaceActiveForCreatorAsync(
            Registration(previous, "EMPLOYEE-01", IssuedAt));
        await store.ReplaceActiveForCreatorAsync(
            Registration(replacement, "EMPLOYEE-01", IssuedAt.AddMinutes(1)));

        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            UPDATE branch_pairing_tokens
            SET lifecycle_status = 'Active',
                terminal_at = NULL,
                terminal_reason = NULL,
                replaced_by_pairing_id = NULL
            WHERE pairing_id = $pairingId;
            """, ("$pairingId", previous.PairingId)));
        await Assert.ThrowsAsync<SqliteException>(() => database.ExecuteAsync("""
            DELETE FROM branch_pairing_tokens WHERE pairing_id = $pairingId;
            """, ("$pairingId", previous.PairingId)));
    }

    [Fact]
    public async Task Store_does_not_create_missing_database()
    {
        using var database = new PairingDatabase();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        var registration = Registration(
            BranchPairingTokenFormat.Generate(),
            "EMPLOYEE-01",
            IssuedAt);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.ReplaceActiveForCreatorAsync(registration));

        Assert.False(File.Exists(database.Path));
    }

    [Fact]
    public async Task Pre_cancelled_operation_does_not_write()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var store = new SqliteBranchPairingTokenIssuanceStore(database.Path);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ReplaceActiveForCreatorAsync(
                Registration(
                    BranchPairingTokenFormat.Generate(),
                    "EMPLOYEE-01",
                    IssuedAt),
                source.Token));

        Assert.Equal(
            0L,
            await database.ScalarAsync<long>("SELECT COUNT(*) FROM branch_pairing_tokens;"));
    }

    private static BranchPairingTokenRegistration Registration(
        IssuedBranchPairingToken token,
        string employeeId,
        DateTimeOffset issuedAt) =>
        new(
            "BRANCH-01",
            token.PairingId,
            employeeId,
            issuedAt,
            issuedAt.AddMinutes(5),
            token.CopySecretHash());

    private sealed class PairingDatabase : IDisposable, IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-pairing-token-issuance-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

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
