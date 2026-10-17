using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite.Tests;

public sealed class SqliteRenderedPrintPayloadRetentionStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 17, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Inspection_separates_protected_recent_and_deletable_payloads()
    {
        await using var database = new RetentionDatabase();
        await database.InitializeAsync();
        await SeedScenarioAsync(database);
        var store = new SqliteRenderedPrintPayloadRetentionStore(database.Path);

        var snapshot = await store.InspectAsync(Now.AddDays(-1));

        Assert.Equal(8, snapshot.PayloadCount);
        Assert.Equal(108, snapshot.TotalBytes);
        Assert.Equal(4, snapshot.ProtectedPayloadCount);
        Assert.Equal(54, snapshot.ProtectedBytes);
        Assert.Equal(1, snapshot.RetainedByAgePayloadCount);
        Assert.Equal(11, snapshot.RetainedByAgeBytes);
        Assert.Equal(3, snapshot.DeletablePayloadCount);
        Assert.Equal(43, snapshot.DeletableBytes);
    }

    [Fact]
    public async Task Pruning_removes_oldest_eligible_payloads_only_and_keeps_metadata()
    {
        await using var database = new RetentionDatabase();
        await database.InitializeAsync();
        await SeedScenarioAsync(database);
        var store = new SqliteRenderedPrintPayloadRetentionStore(database.Path);

        var result = await store.PruneAsync(
            Now.AddDays(-1),
            maximumTotalBytes: 88,
            maximumDeletes: 100);

        Assert.True(result.TargetReached);
        Assert.Equal(2, result.DeletedPayloadCount);
        Assert.Equal(26, result.DeletedBytes);
        Assert.Equal(82, result.After.TotalBytes);
        Assert.Equal(0, await database.PayloadCountAsync("OLD-DISPATCHED"));
        Assert.Equal(0, await database.PayloadCountAsync("FAILED-TERMINAL"));
        Assert.Equal(1, await database.PayloadCountAsync("COMPLETED"));
        Assert.Equal(8, await database.CountAsync("print_jobs"));
        Assert.Equal(8, await database.CountAsync("bill_preview_snapshots"));
    }

    [Fact]
    public async Task Batch_limit_reports_when_protected_or_remaining_data_exceeds_quota()
    {
        await using var database = new RetentionDatabase();
        await database.InitializeAsync();
        await SeedScenarioAsync(database);
        var store = new SqliteRenderedPrintPayloadRetentionStore(database.Path);

        var result = await store.PruneAsync(
            Now.AddDays(-1),
            maximumTotalBytes: 0,
            maximumDeletes: 1);

        Assert.False(result.TargetReached);
        Assert.Equal(1, result.DeletedPayloadCount);
        Assert.Equal(10, result.DeletedBytes);
        Assert.Equal(98, result.After.TotalBytes);
        Assert.Equal(4, result.After.ProtectedPayloadCount);
        Assert.Equal(1, result.After.RetainedByAgePayloadCount);
    }

    [Fact]
    public async Task Pruning_rejects_negative_quota_or_nonpositive_batch_limit()
    {
        await using var database = new RetentionDatabase();
        await database.InitializeAsync();
        var store = new SqliteRenderedPrintPayloadRetentionStore(database.Path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.PruneAsync(Now, -1, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.PruneAsync(Now, 0, 0));
    }

    private static async Task SeedScenarioAsync(RetentionDatabase database)
    {
        await database.AddPayloadAsync(
            "OLD-DISPATCHED",
            "PrintDispatched",
            Now.AddDays(-10),
            10);
        await database.AddPayloadAsync(
            "FAILED-TERMINAL",
            "Failed",
            Now.AddDays(-9),
            16);
        await database.AddPayloadAsync(
            "COMPLETED",
            "Completed",
            Now.AddDays(-8),
            17);
        await database.AddPayloadAsync(
            "QUEUED",
            "PrintQueued",
            Now.AddDays(-7),
            12);
        await database.AddPayloadAsync(
            "DISPATCHING",
            "Dispatching",
            Now.AddDays(-6),
            13,
            leaseOwner: "WORKER-01");
        await database.AddPayloadAsync(
            "UNKNOWN",
            "Unknown",
            Now.AddDays(-5),
            14);
        await database.AddPayloadAsync(
            "FAILED-RETRY",
            "Failed",
            Now.AddDays(-4),
            15,
            nextRetryAt: Now.AddHours(1));
        await database.AddPayloadAsync(
            "RECENT-DISPATCHED",
            "PrintDispatched",
            Now,
            11);
    }

    private sealed class RetentionDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-print-retention-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
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
                    'printer.invalid', 9100, 'RawTcpEscPos', 'test-profile',
                    'Epson', 'TM-T82X', 'M352A', 'test',
                    80, 203, 576, 'BitmapRaster', 'UnicodeRaster', 'Unsupported', 0,
                    '1D5601', NULL, NULL, 2000, 10000, 3, 1000, 1,
                    '2026-10-17T04:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        public async Task AddPayloadAsync(
            string suffix,
            string status,
            DateTimeOffset createdAt,
            int payloadBytes,
            DateTimeOffset? nextRetryAt = null,
            string? leaseOwner = null)
        {
            var requestId = $"REQUEST-{suffix}";
            var snapshotId = $"SNAPSHOT-{suffix}";
            var printJobId = $"PRINT-{suffix}";
            var payload = new byte[payloadBytes];
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO bill_preview_snapshots (
                    snapshot_id, branch_id, request_id, table_session_id, table_id,
                    document_label, content_schema_version, content_json,
                    content_sha256, created_at)
                VALUES (
                    $snapshotId, 'BRANCH-01', $requestId, $tableSessionId, 'A01',
                    'Bill Preview', 1, '{}', $sourceHash, $createdAt);

                INSERT INTO print_jobs (
                    print_job_id, branch_id, request_id, table_session_id,
                    snapshot_id, purpose, target_station, status, attempt_count,
                    next_retry_at, created_at, assigned_printer_id,
                    lease_owner, lease_expires_at)
                VALUES (
                    $printJobId, 'BRANCH-01', $requestId, $tableSessionId,
                    $snapshotId, 'BillPreview', 'Cashier', $status, 0,
                    $nextRetryAt, $createdAt, 'PRINTER-01',
                    $leaseOwner, $leaseExpiresAt);

                INSERT INTO rendered_print_payloads (
                    print_job_id, snapshot_id, printer_id, document_id,
                    payload_bytes, payload_sha256, created_at)
                VALUES (
                    $printJobId, $snapshotId, 'PRINTER-01', $snapshotId,
                    $payload, $payloadHash, $createdAt);
                """;
            command.Parameters.AddWithValue("$snapshotId", snapshotId);
            command.Parameters.AddWithValue("$requestId", requestId);
            command.Parameters.AddWithValue("$tableSessionId", $"SESSION-{suffix}");
            command.Parameters.AddWithValue("$printJobId", printJobId);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$sourceHash", new string('A', 64));
            command.Parameters.AddWithValue("$createdAt", Timestamp(createdAt));
            command.Parameters.AddWithValue(
                "$nextRetryAt",
                nextRetryAt is null ? DBNull.Value : Timestamp(nextRetryAt.Value));
            command.Parameters.AddWithValue(
                "$leaseOwner",
                leaseOwner is null ? DBNull.Value : leaseOwner);
            command.Parameters.AddWithValue(
                "$leaseExpiresAt",
                leaseOwner is null ? DBNull.Value : Timestamp(Now.AddHours(1)));
            command.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
            command.Parameters.AddWithValue("$payloadHash", hash);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public Task<long> CountAsync(string table) =>
            ScalarAsync<long>($"SELECT COUNT(*) FROM {table};");

        public Task<long> PayloadCountAsync(string suffix) =>
            ScalarAsync<long>($"""
                SELECT COUNT(*) FROM rendered_print_payloads
                WHERE print_job_id = 'PRINT-{suffix}';
                """);

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());

        private static string Timestamp(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
