using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqlitePrintJobStatusQueryTests
{
    [Fact]
    public async Task Query_returns_authoritative_job_and_ordered_attempt_history()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-01", "PRINT-01");

        Assert.NotNull(result);
        Assert.Equal("PRINT-01", result.PrintJobId);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal("REQUEST-01", result.RequestId);
        Assert.Equal("SESSION-01", result.TableSessionId.Value);
        Assert.Equal("SNAPSHOT-01", result.SnapshotId);
        Assert.Equal(PrintJobPurpose.BillPreview, result.Purpose);
        Assert.Equal("Cashier", result.TargetStation.Value);
        Assert.Equal(PersistentPrintJobStatus.Unknown, result.Status);
        Assert.Equal(2, result.AttemptCount);
        Assert.Null(result.NextRetryAt);
        Assert.Equal("ConnectionLostAfterFirstByte", result.LastError);
        Assert.Null(result.BlockedReason);
        Assert.Equal("PRINTER-01", result.AssignedPrinterId);
        Assert.Null(result.LeaseExpiresAt);
        Assert.Null(result.OriginalPrintJobId);
        Assert.False(result.IsReprint);

        Assert.Collection(
            result.Attempts,
            first =>
            {
                Assert.Equal("ATTEMPT-01", first.PrintAttemptId);
                Assert.Equal(1, first.AttemptNumber);
                Assert.Equal(PersistentPrintAttemptStatus.Failed, first.Status);
                Assert.True(first.AutomaticRetryAllowed);
                Assert.Equal("ConnectFailed", first.ErrorCode);
                Assert.Null(first.FirstByteAt);
                Assert.NotNull(first.FinishedAt);
            },
            second =>
            {
                Assert.Equal("ATTEMPT-02", second.PrintAttemptId);
                Assert.Equal(2, second.AttemptNumber);
                Assert.Equal(PersistentPrintAttemptStatus.Unknown, second.Status);
                Assert.False(second.AutomaticRetryAllowed);
                Assert.Equal("ConnectionLostAfterFirstByte", second.ErrorCode);
                Assert.NotNull(second.FirstByteAt);
                Assert.NotNull(second.FinishedAt);
            });
    }

    [Fact]
    public async Task Query_does_not_disclose_a_job_to_another_branch()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-02", "PRINT-01");

        Assert.Null(result);
    }

    [Fact]
    public async Task Query_returns_null_for_an_unknown_job()
    {
        await using var database = new StatusDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobStatusQuery(database.Path);

        var result = await query.FindAsync("BRANCH-01", "PRINT-MISSING");

        Assert.Null(result);
    }

    [Fact]
    public async Task Query_is_read_only_and_does_not_create_a_missing_database()
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-status-missing-tests",
            Guid.NewGuid().ToString("N"));
        var path = System.IO.Path.Combine(directory, "missing.db");
        var query = new SqlitePrintJobStatusQuery(path);

        await Assert.ThrowsAsync<SqliteException>(
            () => query.FindAsync("BRANCH-01", "PRINT-01"));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("", "PRINT-01")]
    [InlineData("BRANCH-01", " ")]
    public async Task Query_rejects_empty_identifiers(string branchId, string printJobId)
    {
        var query = new SqlitePrintJobStatusQuery("branch.db");

        await Assert.ThrowsAsync<ArgumentException>(
            () => query.FindAsync(branchId, printJobId));
    }

    private sealed class StatusDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-status-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            var store = new SqliteTableSessionCommandStore(Path);
            await store.InitializeAsync();
            await ExecuteAsync($$"""
                INSERT INTO printer_profiles (
                    printer_id, branch_id, display_name, production_station_id,
                    host, port, protocol, compatibility_profile_id,
                    manufacturer, model, product_code, firmware_version,
                    paper_width_millimeters, dpi, printable_width_dots,
                    render_mode, thai_encoding_mode, status_mode, supports_cash_drawer,
                    cutter_command_hex, drawer_pulse_command_hex, status_command_profile,
                    connect_timeout_ms, send_timeout_ms,
                    maximum_automatic_retries_before_send, automatic_retry_backoff_ms,
                    is_active, updated_at)
                VALUES (
                    'PRINTER-01', 'BRANCH-01', 'Cashier', 'Cashier',
                    'printer.branch.test', 9100, 'RawTcpEscPos',
                    'epson-tm-t82x-m352a-v1', 'Epson', 'TM-T82X', 'M352A',
                    '13.07 ESC/POS', 80, 203, 576, 'BitmapRaster',
                    'UnicodeRaster', 'Unsupported', 0, '1D5601', NULL, NULL,
                    2000, 10000, 1, 1000, 1,
                    '2026-10-17T08:00:00.0000000+00:00');

                INSERT INTO bill_preview_snapshots (
                    snapshot_id, branch_id, request_id, table_session_id, table_id,
                    document_label, content_schema_version, content_json,
                    content_sha256, created_at)
                VALUES (
                    'SNAPSHOT-01', 'BRANCH-01', 'REQUEST-01', 'SESSION-01', 'A01',
                    'Bill Preview', 1, '{}', '{{new string('A', 64)}}',
                    '2026-10-17T08:01:00.0000000+00:00');

                INSERT INTO print_jobs (
                    print_job_id, branch_id, request_id, table_session_id,
                    snapshot_id, purpose, target_station, status, attempt_count,
                    next_retry_at, last_error, blocked_reason, created_at,
                    assigned_printer_id, original_print_job_id, is_reprint)
                VALUES (
                    'PRINT-01', 'BRANCH-01', 'REQUEST-01', 'SESSION-01',
                    'SNAPSHOT-01', 'BillPreview', 'Cashier', 'Unknown', 2,
                    NULL, 'ConnectionLostAfterFirstByte', NULL,
                    '2026-10-17T08:01:00.0000000+00:00', 'PRINTER-01', NULL, 0);

                INSERT INTO print_attempts (
                    print_attempt_id, print_job_id, printer_id, attempt_number,
                    worker_id, payload_sha256, status, automatic_retry_allowed,
                    error_code, started_at, first_byte_at, finished_at)
                VALUES
                    ('ATTEMPT-02', 'PRINT-01', 'PRINTER-01', 2, 'WORKER-01',
                     '{{new string('B', 64)}}', 'Unknown', 0,
                     'ConnectionLostAfterFirstByte',
                     '2026-10-17T08:03:00.0000000+00:00',
                     '2026-10-17T08:03:01.0000000+00:00',
                     '2026-10-17T08:03:02.0000000+00:00'),
                    ('ATTEMPT-01', 'PRINT-01', 'PRINTER-01', 1, 'WORKER-01',
                     '{{new string('B', 64)}}', 'Failed', 1, 'ConnectFailed',
                     '2026-10-17T08:02:00.0000000+00:00', NULL,
                     '2026-10-17T08:02:01.0000000+00:00');
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
