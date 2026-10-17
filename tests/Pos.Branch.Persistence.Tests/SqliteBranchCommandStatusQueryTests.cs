using Microsoft.Data.Sqlite;
using Pos.Application.TableSessions;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqliteBranchCommandStatusQueryTests
{
    [Fact]
    public async Task Query_returns_the_persisted_accepted_receipt_unchanged()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqliteBranchCommandStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-01", "REQUEST-ACCEPTED");

        Assert.NotNull(result);
        Assert.Equal("REQUEST-ACCEPTED", result.RequestId);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal("DEVICE-01", result.DeviceId);
        Assert.Equal("EMPLOYEE-01", result.EmployeeId);
        Assert.Equal(BranchCommandOutcome.BranchAccepted, result.Outcome);
        Assert.Equal("SESSION-01", result.TableSessionId.Value);
        Assert.Equal(TableSessionStatus.Ordering, result.SessionStatus);
        Assert.Equal(3, result.TableVersion);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorDetailsJson);
        Assert.Equal(41, result.BranchSequence);
    }

    [Fact]
    public async Task Query_returns_the_persisted_rejection_evidence()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqliteBranchCommandStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-01", "REQUEST-REJECTED");

        Assert.NotNull(result);
        Assert.Equal(BranchCommandOutcome.Rejected, result.Outcome);
        Assert.Equal("PaymentPending", result.ErrorCode);
        Assert.Equal("{\"currentStatus\":\"PaymentPending\"}", result.ErrorDetailsJson);
        Assert.Null(result.BranchSequence);
    }

    [Fact]
    public async Task Query_does_not_disclose_a_receipt_to_another_branch()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqliteBranchCommandStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-02", "REQUEST-ACCEPTED");

        Assert.Null(result);
    }

    [Fact]
    public async Task Query_returns_null_when_the_branch_has_no_durable_result()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqliteBranchCommandStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-01", "REQUEST-MISSING");

        Assert.Null(result);
    }

    [Fact]
    public async Task Query_is_read_only_and_does_not_create_a_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-command-status-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var query = new SqliteBranchCommandStatusQuery(path);

        await Assert.ThrowsAsync<SqliteException>(
            () => query.FindAsync("BRANCH-01", "REQUEST-01"));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("", "REQUEST-01")]
    [InlineData("BRANCH-01", " ")]
    public async Task Query_rejects_empty_identifiers(string branchId, string requestId)
    {
        var query = new SqliteBranchCommandStatusQuery("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => query.FindAsync(branchId, requestId));
    }

    private sealed class StatusDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-command-status-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            var store = new SqliteTableSessionCommandStore(Path);
            await store.InitializeAsync();
            await ExecuteAsync("""
                INSERT INTO command_receipts (
                    branch_id, request_id, device_id, employee_id,
                    client_created_at, outcome, table_session_id, session_status,
                    table_version, error_code, server_received_at,
                    error_details_json, branch_sequence)
                VALUES
                    ('BRANCH-01', 'REQUEST-ACCEPTED', 'DEVICE-01', 'EMPLOYEE-01',
                     '2026-10-17T09:00:00.0000000+00:00', 'BranchAccepted',
                     'SESSION-01', 'Ordering', 3, NULL,
                     '2026-10-17T09:00:01.0000000+00:00', NULL, 41),
                    ('BRANCH-01', 'REQUEST-REJECTED', 'DEVICE-02', 'EMPLOYEE-02',
                     '2026-10-17T09:01:00.0000000+00:00', 'Rejected',
                     'SESSION-01', 'PaymentPending', 4, 'PaymentPending',
                     '2026-10-17T09:01:01.0000000+00:00',
                     '{"currentStatus":"PaymentPending"}', NULL);
                """);
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

        private async Task ExecuteAsync(string sql)
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
            await command.ExecuteNonQueryAsync();
        }
    }
}
