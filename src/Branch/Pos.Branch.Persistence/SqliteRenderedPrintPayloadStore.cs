using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteRenderedPrintPayloadStore
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteRenderedPrintPayloadStore(string databasePath)
    {
        _connections = new SqliteConnectionFactory(databasePath);
    }

    public async Task<RenderedPrintPayload?> FindAsync(
        PrintJobLease lease,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        return await ReadPayloadAsync(connection, lease, readAt, cancellationToken);
    }

    public async Task<PrintSourceDocument> ReadSourceDocumentAsync(
        PrintJobLease lease,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using var connection = await _connections.OpenAsync(cancellationToken);
        using var job = connection.CreateCommand();
        job.CommandText = """
            SELECT purpose
            FROM print_jobs
            WHERE print_job_id = $printJobId
              AND branch_id = $branchId
              AND target_station = $stationId
              AND snapshot_id = $snapshotId
              AND assigned_printer_id = $printerId
              AND lease_owner = $workerId
              AND lease_expires_at > $readAt
              AND status = 'PrintQueued';
            """;
        AddLeaseParameters(job, lease);
        job.Parameters.AddWithValue("$readAt", Timestamp(readAt));
        var purpose = await job.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidOperationException("The print-job lease is not active.");

        var kind = ParseKind(purpose);
        using var snapshot = connection.CreateCommand();
        snapshot.CommandText = kind switch
        {
            PrintDocumentKind.BillPreview => """
                SELECT content_schema_version, content_json, content_sha256
                FROM bill_preview_snapshots
                WHERE snapshot_id = $snapshotId
                  AND branch_id = $branchId;
                """,
            PrintDocumentKind.KitchenOrder => """
                SELECT content_schema_version, content_json, content_sha256
                FROM production_order_snapshots
                WHERE snapshot_id = $snapshotId
                  AND branch_id = $branchId
                  AND production_station_id = $stationId;
                """,
            _ => throw new InvalidOperationException("Unsupported print document kind.")
        };
        snapshot.Parameters.AddWithValue("$snapshotId", lease.SnapshotId);
        snapshot.Parameters.AddWithValue("$branchId", lease.BranchId);
        snapshot.Parameters.AddWithValue("$stationId", lease.ProductionStationId);
        await using var reader = await snapshot.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The immutable print source snapshot was not found.");
        }

        var schemaVersion = reader.GetInt32(0);
        var json = reader.GetString(1);
        var storedHash = RequireSha256(reader.GetString(2));
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        if (!string.Equals(storedHash, actualHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The immutable print source snapshot hash is invalid.");
        }

        return new PrintSourceDocument(
            lease.SnapshotId,
            kind,
            schemaVersion,
            json,
            storedHash);
    }

    public async Task<RenderedPrintPayload> SaveAsync(
        PrintJobLease lease,
        RenderedPrintPayload payload,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(payload);
        if (!string.Equals(payload.DocumentId, lease.SnapshotId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The rendered payload document does not match the leased snapshot.",
                nameof(payload));
        }

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO rendered_print_payloads (
                    print_job_id, snapshot_id, printer_id, document_id,
                    payload_bytes, payload_sha256, created_at)
                SELECT j.print_job_id, j.snapshot_id, j.assigned_printer_id, $documentId,
                       $payloadBytes, $payloadSha256, $createdAt
                FROM print_jobs AS j
                WHERE j.print_job_id = $printJobId
                  AND j.branch_id = $branchId
                  AND j.target_station = $stationId
                  AND j.snapshot_id = $snapshotId
                  AND j.assigned_printer_id = $printerId
                  AND j.lease_owner = $workerId
                  AND j.lease_expires_at > $createdAt
                  AND j.status = 'PrintQueued';
                """;
            AddLeaseParameters(insert, lease);
            insert.Parameters.AddWithValue("$documentId", payload.DocumentId);
            insert.Parameters.Add("$payloadBytes", SqliteType.Blob).Value = payload.Bytes.ToArray();
            insert.Parameters.AddWithValue("$payloadSha256", payload.Sha256);
            insert.Parameters.AddWithValue("$createdAt", Timestamp(createdAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            var stored = await ReadPayloadAsync(
                connection,
                lease,
                createdAt,
                cancellationToken,
                transaction);
            if (stored is null)
            {
                throw new InvalidOperationException("The rendered print payload was not persisted.");
            }

            if (!string.Equals(stored.DocumentId, payload.DocumentId, StringComparison.Ordinal) ||
                !string.Equals(stored.Sha256, payload.Sha256, StringComparison.Ordinal) ||
                !stored.Bytes.Span.SequenceEqual(payload.Bytes.Span))
            {
                throw new InvalidOperationException(
                    "A different rendered payload already exists for the print job.");
            }

            await transaction.CommitAsync(cancellationToken);
            return stored;
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<RenderedPrintPayload?> ReadPayloadAsync(
        SqliteConnection connection,
        PrintJobLease lease,
        DateTimeOffset readAt,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.document_id, r.payload_bytes, r.payload_sha256
            FROM rendered_print_payloads AS r
            JOIN print_jobs AS j
              ON j.print_job_id = r.print_job_id
            WHERE r.print_job_id = $printJobId
              AND r.snapshot_id = $snapshotId
              AND r.printer_id = $printerId
              AND j.branch_id = $branchId
              AND j.target_station = $stationId
              AND j.assigned_printer_id = $printerId
              AND j.lease_owner = $workerId
              AND j.lease_expires_at > $readAt
              AND j.status = 'PrintQueued';
            """;
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue("$readAt", Timestamp(readAt));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var documentId = reader.GetString(0);
        var bytes = (byte[])reader[1];
        var storedHash = RequireSha256(reader.GetString(2));
        var payload = new RenderedPrintPayload(documentId, bytes);
        if (!string.Equals(payload.Sha256, storedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The persisted rendered payload hash is invalid.");
        }

        return payload;
    }

    private static PrintDocumentKind ParseKind(string purpose) => purpose switch
    {
        "BillPreview" => PrintDocumentKind.BillPreview,
        "KitchenOrder" => PrintDocumentKind.KitchenOrder,
        _ => throw new InvalidOperationException($"Unsupported print-job purpose '{purpose}'.")
    };

    private static void AddLeaseParameters(SqliteCommand command, PrintJobLease lease)
    {
        command.Parameters.AddWithValue("$printJobId", lease.PrintJobId);
        command.Parameters.AddWithValue("$branchId", lease.BranchId);
        command.Parameters.AddWithValue("$stationId", lease.ProductionStationId);
        command.Parameters.AddWithValue("$snapshotId", lease.SnapshotId);
        command.Parameters.AddWithValue("$printerId", lease.PrinterId);
        command.Parameters.AddWithValue("$workerId", lease.WorkerId);
    }

    private static string RequireSha256(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("A stored SHA-256 value is invalid.");
        }

        return normalized;
    }

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original operation or commit exception.
        }
    }
}
