using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Persistence.Tests;

public sealed class SqlitePrintJobAttentionQueryTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Query_returns_attention_states_and_latest_attempt_only()
    {
        await using var database = new AttentionDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobAttentionQuery(database.Path);

        var page = await query.ListAsync("BRANCH-01", ObservedAt, 20);

        Assert.Equal(5, page.TotalCount);
        Assert.Null(page.NextCursor);
        Assert.Equal(
            [
                "PRINT-UNKNOWN",
                "PRINT-ORPHANED",
                "PRINT-EXPIRED",
                "PRINT-FAILED",
                "PRINT-QUEUED"
            ],
            page.Items.Select(item => item.PrintJobId));

        var unknown = page.Items[0];
        Assert.Equal(PersistentPrintJobStatus.Unknown, unknown.Status);
        Assert.Equal("PRINTER-01", unknown.AssignedPrinterId);
        Assert.Equal("ConnectionLostAfterFirstByte", unknown.LastError);
        Assert.Equal(PersistentPrintAttemptStatus.Unknown, unknown.LatestAttemptStatus);
        Assert.Equal("ConnectionLostAfterFirstByte", unknown.LatestErrorCode);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 17, 8, 6, 3, TimeSpan.Zero),
            unknown.LatestAttemptFinishedAt);

        var orphaned = page.Items[1];
        Assert.Equal(PersistentPrintJobStatus.Dispatching, orphaned.Status);

        var expired = page.Items[2];
        Assert.Equal(PersistentPrintJobStatus.Dispatching, expired.Status);
        Assert.Equal("SNAPSHOT-EXPIRED", expired.SnapshotId);
    }

    [Fact]
    public async Task Query_uses_stable_keyset_pagination_and_preserves_total_count()
    {
        await using var database = new AttentionDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobAttentionQuery(database.Path);

        var first = await query.ListAsync("BRANCH-01", ObservedAt, 3);
        var second = await query.ListAsync(
            "BRANCH-01",
            ObservedAt,
            3,
            first.NextCursor);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(
            ["PRINT-UNKNOWN", "PRINT-ORPHANED", "PRINT-EXPIRED"],
            first.Items.Select(x => x.PrintJobId));
        Assert.Equal(["PRINT-FAILED", "PRINT-QUEUED"], second.Items.Select(x => x.PrintJobId));
        Assert.NotNull(first.NextCursor);
        Assert.Equal("PRINT-EXPIRED", first.NextCursor.PrintJobId);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Query_excludes_other_branches_and_nonattention_states()
    {
        await using var database = new AttentionDatabase();
        await database.InitializeAsync();
        var query = new SqlitePrintJobAttentionQuery(database.Path);

        var branchOne = await query.ListAsync("BRANCH-01", ObservedAt, 20);
        var branchTwo = await query.ListAsync("BRANCH-02", ObservedAt, 20);

        Assert.DoesNotContain(branchOne.Items, item => item.PrintJobId == "PRINT-ACTIVE");
        Assert.DoesNotContain(branchOne.Items, item => item.PrintJobId == "PRINT-COMPLETED");
        Assert.Single(branchTwo.Items);
        Assert.Equal("PRINT-OTHER-BRANCH", branchTwo.Items[0].PrintJobId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Query_rejects_an_out_of_range_page_size(int maximumResults)
    {
        var query = new SqlitePrintJobAttentionQuery("branch.db");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => query.ListAsync("BRANCH-01", ObservedAt, maximumResults));
    }

    [Fact]
    public async Task Query_rejects_an_empty_cursor_identifier_before_database_access()
    {
        var query = new SqlitePrintJobAttentionQuery("branch.db");
        var cursor = new PrintJobAttentionCursor(ObservedAt, " ");

        await Assert.ThrowsAsync<ArgumentException>(
            () => query.ListAsync("BRANCH-01", ObservedAt, 20, cursor));
    }

    private sealed class AttentionDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-attention-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            var store = new SqliteTableSessionCommandStore(Path);
            await store.InitializeAsync();
            await ExecuteAsync(SeedSql());
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

        private static string SeedSql()
        {
            var sql = """
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
                """;
            var jobs = new[]
            {
                new Job("QUEUED", "BRANCH-01", "PrintQueued", "08:03", null),
                new Job("FAILED", "BRANCH-01", "Failed", "08:04", null),
                new Job("EXPIRED", "BRANCH-01", "Dispatching", "08:05", "08:59"),
                new Job("ORPHANED", "BRANCH-01", "Dispatching", "08:05", null),
                new Job("UNKNOWN", "BRANCH-01", "Unknown", "08:06", null),
                new Job("ACTIVE", "BRANCH-01", "Dispatching", "08:02", "09:01"),
                new Job("COMPLETED", "BRANCH-01", "Completed", "08:01", null),
                new Job("OTHER-BRANCH", "BRANCH-02", "Failed", "08:07", null)
            };
            foreach (var job in jobs)
            {
                var lease = job.LeaseExpiresAt is null
                    ? "NULL"
                    : $"'2026-10-17T{job.LeaseExpiresAt}:00.0000000+00:00'";
                sql += $$"""

                    INSERT INTO bill_preview_snapshots (
                        snapshot_id, branch_id, request_id, table_session_id, table_id,
                        document_label, content_schema_version, content_json,
                        content_sha256, created_at)
                    VALUES (
                        'SNAPSHOT-{{job.Suffix}}', '{{job.BranchId}}', 'REQUEST-{{job.Suffix}}',
                        'SESSION-{{job.Suffix}}', 'A01', 'Bill Preview', 1, '{}',
                        '{{new string('A', 64)}}',
                        '2026-10-17T{{job.CreatedAt}}:00.0000000+00:00');

                    INSERT INTO print_jobs (
                        print_job_id, branch_id, request_id, table_session_id,
                        snapshot_id, purpose, target_station, status, attempt_count,
                        last_error, created_at, assigned_printer_id, lease_expires_at)
                    VALUES (
                        'PRINT-{{job.Suffix}}', '{{job.BranchId}}', 'REQUEST-{{job.Suffix}}',
                        'SESSION-{{job.Suffix}}', 'SNAPSHOT-{{job.Suffix}}', 'BillPreview',
                        'Cashier', '{{job.Status}}',
                        {{(job.Suffix == "UNKNOWN" ? 2 : 0)}},
                        {{(job.Suffix == "UNKNOWN" ? "'ConnectionLostAfterFirstByte'" : "NULL")}},
                        '2026-10-17T{{job.CreatedAt}}:00.0000000+00:00',
                        {{(job.BranchId == "BRANCH-01" ? "'PRINTER-01'" : "NULL")}}, {{lease}});
                    """;
            }

            sql += $$"""

                INSERT INTO print_attempts (
                    print_attempt_id, print_job_id, printer_id, attempt_number,
                    worker_id, payload_sha256, status, automatic_retry_allowed,
                    error_code, started_at, first_byte_at, finished_at)
                VALUES
                    ('ATTEMPT-UNKNOWN-01', 'PRINT-UNKNOWN', 'PRINTER-01', 1,
                     'WORKER-01', '{{new string('B', 64)}}', 'Failed', 1,
                     'ConnectFailed', '2026-10-17T08:06:01.0000000+00:00', NULL,
                     '2026-10-17T08:06:01.0000000+00:00'),
                    ('ATTEMPT-UNKNOWN-02', 'PRINT-UNKNOWN', 'PRINTER-01', 2,
                     'WORKER-01', '{{new string('B', 64)}}', 'Unknown', 0,
                     'ConnectionLostAfterFirstByte',
                     '2026-10-17T08:06:01.0000000+00:00',
                     '2026-10-17T08:06:02.0000000+00:00',
                     '2026-10-17T08:06:03.0000000+00:00');
                """;
            return sql;
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

        private sealed record Job(
            string Suffix,
            string BranchId,
            string Status,
            string CreatedAt,
            string? LeaseExpiresAt);
    }
}
