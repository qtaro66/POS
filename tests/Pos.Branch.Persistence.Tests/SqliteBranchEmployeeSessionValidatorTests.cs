using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchEmployeeSessionValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Valid_active_session_returns_bound_employee_identity()
    {
        await using var database = new SessionDatabase();
        var token = await database.SeedAsync();
        var employeeStatus = new StubEmployeeStatusReader(isActive: true);
        var validator = CreateValidator(database.Path, employeeStatus);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            var result = await validator.ValidateActiveAsync(
                "BRANCH-01",
                "DEVICE-01",
                parsed.SessionId,
                parsed.Secret);

            var session = Assert.IsType<VerifiedBranchEmployeeSession>(result);
            Assert.Equal("BRANCH-01", session.BranchId);
            Assert.Equal("DEVICE-01", session.DeviceId);
            Assert.Equal("EMPLOYEE-01", session.EmployeeId);
            Assert.Equal(("BRANCH-01", "EMPLOYEE-01"), employeeStatus.LastRequest);
        }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("device")]
    [InlineData("employee")]
    public async Task Inactive_dependency_returns_same_unauthenticated_result(
        string inactiveDependency)
    {
        await using var database = new SessionDatabase();
        var token = await database.SeedAsync(
            sessionStatus: inactiveDependency == "session" ? "Revoked" : "Active",
            deviceStatus: inactiveDependency == "device" ? "Revoked" : "Active");
        var validator = CreateValidator(
            database.Path,
            new StubEmployeeStatusReader(inactiveDependency != "employee"));
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            Assert.Null(await validator.ValidateActiveAsync(
                "BRANCH-01",
                "DEVICE-01",
                parsed.SessionId,
                parsed.Secret));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Session_at_or_before_expiry_boundary_is_rejected(int seconds)
    {
        await using var database = new SessionDatabase();
        var token = await database.SeedAsync(expiresAt: Now.AddSeconds(seconds));
        var employeeStatus = new StubEmployeeStatusReader(isActive: true);
        var validator = CreateValidator(database.Path, employeeStatus);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            Assert.Null(await validator.ValidateActiveAsync(
                "BRANCH-01",
                "DEVICE-01",
                parsed.SessionId,
                parsed.Secret));
            Assert.Null(employeeStatus.LastRequest);
        }
    }

    [Fact]
    public async Task Wrong_secret_or_binding_is_rejected_before_employee_status_lookup()
    {
        await using var database = new SessionDatabase();
        var token = await database.SeedAsync();
        var employeeStatus = new StubEmployeeStatusReader(isActive: true);
        var validator = CreateValidator(database.Path, employeeStatus);
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            var wrongSecret = parsed.Secret.ToArray();
            wrongSecret[0] ^= byte.MaxValue;

            Assert.Null(await validator.ValidateActiveAsync(
                "BRANCH-01", "DEVICE-01", parsed.SessionId, wrongSecret));
            Assert.Null(await validator.ValidateActiveAsync(
                "BRANCH-02", "DEVICE-01", parsed.SessionId, parsed.Secret));
            Assert.Null(await validator.ValidateActiveAsync(
                "BRANCH-01", "DEVICE-02", parsed.SessionId, parsed.Secret));
            Assert.Null(employeeStatus.LastRequest);
        }
    }

    [Fact]
    public async Task Malformed_token_material_fails_without_database_or_status_access()
    {
        var missingPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            "missing.db");
        var employeeStatus = new StubEmployeeStatusReader(isActive: true);
        var validator = CreateValidator(missingPath, employeeStatus);

        Assert.Null(await validator.ValidateActiveAsync(
            "BRANCH-01", "DEVICE-01", "not-canonical", new byte[32]));
        var canonicalId = BranchEmployeeSessionTokenFormat.Generate().SessionId;
        Assert.Null(await validator.ValidateActiveAsync(
            "BRANCH-01", "DEVICE-01", canonicalId, new byte[31]));
        Assert.False(File.Exists(missingPath));
        Assert.Null(employeeStatus.LastRequest);
    }

    [Fact]
    public async Task Corrupt_expiry_and_employee_status_failures_are_not_hidden()
    {
        await using var database = new SessionDatabase();
        var token = await database.SeedAsync();
        await database.ExecuteAsync("""
            DROP TRIGGER branch_employee_session_identity_immutable;
            UPDATE branch_employee_sessions
            SET expires_at = 'not-a-timestamp'
            WHERE session_id = $sessionId;
            """, ("$sessionId", token.SessionId));
        var validator = CreateValidator(
            database.Path,
            new StubEmployeeStatusReader(isActive: true));
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                validator.ValidateActiveAsync(
                    "BRANCH-01", "DEVICE-01", parsed.SessionId, parsed.Secret));
        }

        await database.ExecuteAsync("""
            UPDATE branch_employee_sessions
            SET expires_at = '2026-10-21T18:00:00.0000000+00:00'
            WHERE session_id = $sessionId;
            """, ("$sessionId", token.SessionId));
        var failingValidator = CreateValidator(
            database.Path,
            new StubEmployeeStatusReader(new IOException("Status unavailable.")));
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var retry));
        using (retry)
        {
            await Assert.ThrowsAsync<IOException>(() =>
                failingValidator.ValidateActiveAsync(
                    "BRANCH-01", "DEVICE-01", retry.SessionId, retry.Secret));
        }
    }

    [Fact]
    public async Task Missing_database_is_not_created_by_validator()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-session-validator-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var token = BranchEmployeeSessionTokenFormat.Generate();
        var validator = CreateValidator(
            path,
            new StubEmployeeStatusReader(isActive: true));
        Assert.True(BranchEmployeeSessionTokenFormat.TryParse(token.Token, out var parsed));
        using (parsed)
        {
            await Assert.ThrowsAsync<SqliteException>(() =>
                validator.ValidateActiveAsync(
                    "BRANCH-01", "DEVICE-01", parsed.SessionId, parsed.Secret));
        }

        Assert.False(File.Exists(path));
    }

    private static SqliteBranchEmployeeSessionValidator CreateValidator(
        string path,
        IBranchEmployeeStatusReader statusReader) =>
        new(path, statusReader, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubEmployeeStatusReader : IBranchEmployeeStatusReader
    {
        private readonly bool _isActive;
        private readonly Exception? _exception;

        public StubEmployeeStatusReader(bool isActive) => _isActive = isActive;

        public StubEmployeeStatusReader(Exception exception) =>
            _exception = exception;

        public (string BranchId, string EmployeeId)? LastRequest { get; private set; }

        public Task<bool> IsActiveAsync(
            string branchId,
            string employeeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = (branchId, employeeId);
            return _exception is null
                ? Task.FromResult(_isActive)
                : Task.FromException<bool>(_exception);
        }
    }

    private sealed class SessionDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-session-validator-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task<IssuedBranchEmployeeSessionToken> SeedAsync(
            string sessionStatus = "Active",
            string deviceStatus = "Active",
            DateTimeOffset? expiresAt = null)
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            await ExecuteAsync("""
                INSERT INTO branch_devices (
                    branch_id, device_id, display_name, lifecycle_status,
                    paired_by_employee_id, paired_at,
                    retired_by_employee_id, retired_at, retirement_reason)
                VALUES (
                    'BRANCH-01', 'DEVICE-01', 'Synthetic test device', $deviceStatus,
                    'EMPLOYEE-01', '2026-10-21T08:00:00.0000000+00:00',
                    $retiredBy, $retiredAt, $reason);
                """,
                ("$deviceStatus", deviceStatus),
                ("$retiredBy", deviceStatus == "Active" ? null : "EMPLOYEE-02"),
                ("$retiredAt", deviceStatus == "Active" ? null : Now.ToString("O")),
                ("$reason", deviceStatus == "Active" ? null : "Synthetic test retirement"));
            var token = BranchEmployeeSessionTokenFormat.Generate();
            var expiry = expiresAt ?? Now.AddHours(8);
            var store = new SqliteBranchEmployeeSessionIssuanceStore(Path);
            await store.CreateAsync(new BranchEmployeeSessionRegistration(
                "BRANCH-01",
                "DEVICE-01",
                "EMPLOYEE-01",
                token.SessionId,
                Now.AddHours(-1),
                expiry,
                token.CopySecretHash()));
            if (sessionStatus != "Active")
            {
                await ExecuteAsync("""
                    UPDATE branch_employee_sessions
                    SET lifecycle_status = 'Revoked',
                        revoked_at = $revokedAt,
                        revocation_reason = 'Synthetic test revocation'
                    WHERE session_id = $sessionId;
                    """,
                    ("$revokedAt", Now.ToString("O")),
                    ("$sessionId", token.SessionId));
            }

            return token;
        }

        public async Task ExecuteAsync(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadWrite,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString());
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
}
