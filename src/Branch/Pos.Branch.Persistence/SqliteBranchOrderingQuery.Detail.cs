using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Application.TableSessions;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteBranchOrderingQuery
{
    public async Task<BranchOrderingSessionDetail?> DetailAsync(string branchId, string tableSessionId, DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableSessionId);
        if (asOf.Offset != TimeSpan.Zero) { throw new ArgumentException("Read timestamp must be UTC.", nameof(asOf)); }
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        using var header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT s.table_id, s.status, s.version, EXISTS (
                SELECT 1 FROM cancellation_requests c
                WHERE c.branch_id = s.branch_id AND c.table_session_id = s.table_session_id
                  AND c.status IN ('CancellationRequested', 'ProductionStatusChecking', 'StatusRecorded'))
            FROM table_sessions s WHERE s.branch_id = $branchId AND s.table_session_id = $sessionId
              AND s.status IN ('Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid');
            """;
        header.Parameters.AddWithValue("$branchId", branchId);
        header.Parameters.AddWithValue("$sessionId", tableSessionId);
        BranchActiveTableSession session;
        bool hasOpenCancellation;
        await using (var reader = await header.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) { return null; }
            session = new(tableSessionId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
            hasOpenCancellation = reader.GetBoolean(3);
        }

        // Reuse the authoritative calculator within this read-only snapshot. No preview/job is persisted.
        var source = new SqliteBillPreviewContentSource();
        BillPreviewContent? preview = null;
        if (await source.HasActiveOrderLinesAsync(connection, transaction, branchId, tableSessionId, cancellationToken))
        {
            preview = await source.CaptureAsync(connection, transaction,
                new BillPreviewCaptureRequest(branchId, "READ-ONLY", TableSessionId.From(tableSessionId), TableId.From(session.TableId), asOf), cancellationToken);
            if (preview.SchemaVersion != 1) { throw new InvalidOperationException("Unsupported bill preview calculation schema."); }
        }
        using var document = preview is null ? null : JsonDocument.Parse(preview.Json);
        IReadOnlyDictionary<string, JsonElement> calculated = new Dictionary<string, JsonElement>();
        BranchOrderingBillTotals? totals = null;
        if (document is not null)
        {
            var root = document.RootElement;
            calculated = root.GetProperty("items").EnumerateArray()
                .ToDictionary(line => line.GetProperty("orderLineId").GetString()!, StringComparer.Ordinal);
            decimal Amount(string name) => root.GetProperty(name).GetDecimal();
            totals = new BranchOrderingBillTotals(
            root.GetProperty("masterDataVersion").GetString()!, root.GetProperty("calculationPolicyVersion").GetString()!,
            Amount("vatRate"), Amount("serviceChargeRate"), Amount("itemGrossAmount"), Amount("lineDiscountAmount"), Amount("itemNetAmount"),
            root.TryGetProperty("billDiscount", out var discount) ? discount.GetProperty("appliedAmount").GetDecimal() : 0m,
            Amount("serviceChargeBase"), Amount("serviceChargeAmount"), Amount("totalBeforeRounding"), Amount("roundingAdjustment"),
                Amount("netTotal"), Amount("vatInclusiveAmount"), Amount("amountBeforeVat"));
        }
        var batches = await ReadBatchesAsync(connection, transaction, branchId, tableSessionId, calculated, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(session, asOf, hasOpenCancellation, totals, batches);
    }

    private static async Task<IReadOnlyList<BranchOrderingBatch>> ReadBatchesAsync(SqliteConnection connection, SqliteTransaction transaction,
        string branchId, string tableSessionId, IReadOnlyDictionary<string, JsonElement> calculated, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.order_batch_id, b.request_id, b.branch_sequence, r.server_received_at, r.outcome,
                   l.order_line_id, l.line_sequence, l.sellable_item_id, l.display_name,
                   l.original_quantity, l.active_quantity, l.status, l.unit_price, l.fulfillment_type, l.comment,
                   o.option_id, o.option_group_id, o.display_name, o.quantity_per_item, o.unit_price
            FROM order_lines l JOIN order_batches b
              ON b.branch_id = l.branch_id AND b.order_batch_id = l.order_batch_id
            LEFT JOIN command_receipts r ON r.branch_id = b.branch_id AND r.request_id = b.request_id
            LEFT JOIN order_line_options o ON o.branch_id = l.branch_id AND o.order_line_id = l.order_line_id
            WHERE l.branch_id = $branchId AND l.table_session_id = $sessionId
            ORDER BY COALESCE(b.branch_sequence, 9223372036854775807), b.submitted_at,
                     b.order_batch_id, l.line_sequence, o.option_sequence;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$sessionId", tableSessionId);
        var batches = new List<BatchBuilder>();
        var batchMap = new Dictionary<string, BatchBuilder>(StringComparer.Ordinal);
        var lineMap = new Dictionary<string, LineBuilder>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(3) || reader.IsDBNull(4) || reader.GetString(4) != "BranchAccepted")
            {
                throw new InvalidOperationException("A stored order batch has no accepted command receipt.");
            }
            var batchId = reader.GetString(0);
            if (!batchMap.TryGetValue(batchId, out var batch))
            {
                var receivedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (receivedAt.Offset != TimeSpan.Zero) { throw new InvalidOperationException("Stored receipt time is not UTC."); }
                batch = new(batchId, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), receivedAt);
                batchMap.Add(batchId, batch); batches.Add(batch);
            }
            var lineId = reader.GetString(5);
            if (!lineMap.TryGetValue(lineId, out var line))
            {
                decimal gross = 0, net = 0;
                if (reader.GetString(11) == "Active" && reader.GetInt32(10) > 0)
                {
                    if (!calculated.TryGetValue(lineId, out var amounts)) { throw new InvalidOperationException("An active line is missing from the authoritative calculation."); }
                    gross = amounts.GetProperty("grossAmount").GetDecimal(); net = amounts.GetProperty("netAmount").GetDecimal();
                }
                line = new(new BranchOrderingLine(lineId, reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
                    reader.GetInt32(9), reader.GetInt32(10), reader.GetString(11), Price(reader.GetString(12)), reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14), [], gross, gross - net, net));
                lineMap.Add(lineId, line); batch.Lines.Add(line);
            }
            if (!reader.IsDBNull(15))
            {
                line.Options.Add(new(reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.GetString(17), reader.GetInt32(18), Price(reader.GetString(19))));
            }
        }
        return batches.Select(batch => batch.Build()).ToArray();
    }

    private static decimal Price(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price >= 0
        ? price : throw new InvalidOperationException("Stored order price is invalid.");
    private sealed class LineBuilder(BranchOrderingLine line)
    {
        public List<BranchOrderingOption> Options { get; } = [];
        public BranchOrderingLine Build() => line with { Options = Options.ToArray() };
    }
    private sealed class BatchBuilder(string id, string requestId, long? sequence, DateTimeOffset receivedAt)
    {
        public List<LineBuilder> Lines { get; } = [];
        public BranchOrderingBatch Build()
        {
            var lines = Lines.Select(line => line.Build()).ToArray();
            return new(id, requestId, sequence, receivedAt, lines, lines.Sum(line => line.ActiveGrossAmount),
                lines.Sum(line => line.ActiveDiscountAmount), lines.Sum(line => line.ActiveNetAmount));
        }
    }
}
