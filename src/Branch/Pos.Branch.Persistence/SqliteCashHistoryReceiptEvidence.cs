using System.Text;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<CashHistoryReceiptEvidence?> ReadCashHistoryReceiptEvidenceAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token = default) => ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).ReadCashHistoryReceiptEvidenceAsync(branchId, requestId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    internal async Task<CashHistoryReceiptEvidence?> ReadCashHistoryReceiptEvidenceAsync(string branchId, string requestId,
        DateTimeOffset now, CancellationToken token)
    {
        ValidateReportId(branchId); ValidateReportId(requestId); RequireClosingReportUtc(now);
        CashHistoryItem captured;
        using (var parent = CreateCommand($"SELECT {CashHistoryColumns} FROM {CashHistoryJoin} WHERE sale.branch_id=$branch AND sale.request_id=$request;"))
        {
            parent.Parameters.AddWithValue("$branch", branchId); parent.Parameters.AddWithValue("$request", requestId);
            await using var reader = await parent.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) { return null; }
            captured = ReadCashHistoryItem(reader);
        }

        CashHistoryReceiptSimulation? simulation = null;
        using var table = CreateCommand("SELECT type FROM sqlite_master WHERE name='test_receipt_simulation_results';");
        var objectType = await table.ExecuteScalarAsync(token) as string;
        if (objectType is not null)
        {
            if (objectType != "table") { throw InvalidReceiptEvidence(); }
            using var read = CreateCommand("""
                SELECT status,content,typeof(status),typeof(content) FROM test_receipt_simulation_results
                WHERE branch_id=$branch AND receipt_number=$receipt;
                """);
            read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$receipt", captured.ReceiptNumber);
            await using var reader = await read.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                if (reader.GetString(2) != "text" || reader.GetString(3) is not ("text" or "null")) { throw InvalidReceiptEvidence(); }
                var status = reader.GetString(0); var text = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (status == "Simulated")
                {
                    if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 128 * 1024 ||
                        text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
                    { throw InvalidReceiptEvidence(); }
                }
                else if (status != "Failed" || text is not null) { throw InvalidReceiptEvidence(); }
                simulation = new(status, text);
                if (await reader.ReadAsync(token)) { throw InvalidReceiptEvidence(); }
            }
        }
        return new(1, branchId, now, requestId, captured.TableSessionId, captured.ReceiptNumber,
            captured.CapturedAt, captured.CapturedPrintStatus, simulation);
    }

    private static InvalidDataException InvalidReceiptEvidence() => new("Saved receipt simulation evidence is unavailable.");
}
