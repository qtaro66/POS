using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed partial class SqliteTableSessionCommandStore
{
    public Task<KitchenReprintReceipt?> WriteKitchenReprintAsync(string branchId, KitchenReprintCommand command,
        CancellationToken token = default) => ExecuteAtomicallyAsync((transaction, cancellation) =>
            ((SqliteTableSessionCommandTransaction)transaction).WriteKitchenReprintAsync(branchId, command, cancellation), token);
    public Task<KitchenReprintStatus?> ReadKitchenReprintStatusAsync(string branchId, string requesterEmployeeId,
        string deviceId, string requestId, DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAtomicallyAsync((transaction, cancellation) => ((SqliteTableSessionCommandTransaction)transaction)
            .ReadKitchenReprintStatusAsync(branchId, requesterEmployeeId, deviceId, requestId, now, cancellation), token);
}

internal sealed partial class SqliteTableSessionCommandTransaction
{
    private static readonly JsonSerializerOptions ReprintJson = new() { RespectRequiredConstructorParameters = true };
    internal async Task<KitchenReprintReceipt?> WriteKitchenReprintAsync(string branchId, KitchenReprintCommand command, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateReportId(branchId); RequireClosingReportUtc(command.QueuedAt);
        foreach (var id in new[] { command.RequestId, command.CashRequestId, command.OrderLineId, command.DeviceId,
                     command.RequesterEmployeeId, command.ApproverEmployeeId }) { ValidateReportId(id); }
        ProductionIdentifier(command.SnapshotId, 1024); ProductionIdentifier(command.OriginalPrintJobId, 1024);
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 4096 ||
            command.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
        { throw new ArgumentException("Invalid reprint reason."); }
        var recorded = await ReadRecordedReprintAsync(branchId, command.RequestId, token);
        if (recorded is { } prior)
        {
            if (prior.Command with { QueuedAt = command.QueuedAt } != command) { throw new KitchenReprintConflictException(); }
            return prior.Receipt;
        }
        using (var collision = CreateCommand("""
            SELECT 1 FROM command_receipts WHERE branch_id=$branch AND request_id=$request
            UNION ALL SELECT 1 FROM cash_sales WHERE branch_id=$branch AND request_id=$request
            UNION ALL SELECT 1 FROM store_lifecycle_commands WHERE branch_id=$branch AND request_id=$request LIMIT 1;
            """))
        {
            collision.Parameters.AddWithValue("$branch", branchId); collision.Parameters.AddWithValue("$request", command.RequestId);
            if (await collision.ExecuteScalarAsync(token) is not null) { throw new KitchenReprintConflictException(); }
        }
        var production = await ReadCashHistoryProductionOrderDetailAsync(branchId, command.CashRequestId, command.SnapshotId, command.QueuedAt, token);
        if (production is null) { return null; }
        if (!production.Lines.Any(line => line.OrderLineId == command.OrderLineId)) { throw new KitchenReprintLineNotFoundException(); }
        var original = await FindHistoryPrintJob(branchId, production, command.OriginalPrintJobId, token)
            ?? throw new CashHistoryPrintJobNotFoundException();
        using var source = CreateCommand("SELECT content_json FROM production_order_snapshots WHERE snapshot_id=$snapshot AND branch_id=$branch;");
        source.Parameters.AddWithValue("$snapshot", command.SnapshotId); source.Parameters.AddWithValue("$branch", branchId);
        using var document = JsonDocument.Parse((string)(await source.ExecuteScalarAsync(token))!);
        var line = document.RootElement.GetProperty("lines").EnumerateArray().Single(item => item.GetProperty("orderLineId").GetString() == command.OrderLineId);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("lines"))
                {
                    writer.WritePropertyName("lines"); writer.WriteStartArray(); line.WriteTo(writer); writer.WriteEndArray();
                }
                else { property.WriteTo(writer); }
            }
            writer.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(output.ToArray());
        var snapshotId = $"{branchId}:{command.RequestId}:kitchen-reprint-snapshot:1";
        var printJobId = $"{branchId}:{command.RequestId}:kitchen-reprint-print:1";
        ProductionIdentifier(snapshotId, 1024); ProductionIdentifier(printJobId, 1024);
        var receipt = new KitchenReprintReceipt(1, branchId, command.RequestId, command.CashRequestId,
            production.TableSessionId, production.Snapshot.OrderBatchId, command.OrderLineId, command.SnapshotId,
            original.PrintJobId, snapshotId, printJobId, original.ProductionStationId, command.QueuedAt);
        using var write = CreateCommand("""
            INSERT INTO kitchen_reprint_snapshots(snapshot_id,branch_id,request_id,table_session_id,order_batch_id,order_line_id,
                production_station_id,original_snapshot_id,original_print_job_id,content_schema_version,content_json,content_sha256,created_at)
            VALUES($snapshot,$branch,$request,$session,$batch,$line,$station,$originalSnapshot,$originalJob,1,$json,$hash,$time);
            INSERT INTO print_jobs(print_job_id,branch_id,request_id,table_session_id,snapshot_id,purpose,target_station,status,
                attempt_count,created_at,original_print_job_id,is_reprint)
            VALUES($job,$branch,$request,$session,$snapshot,'KitchenOrder',$station,'PrintQueued',0,$time,$originalJob,1);
            INSERT INTO kitchen_reprint_requests(branch_id,request_id,device_id,requester_employee_id,approver_employee_id,
                cash_request_id,original_snapshot_id,order_line_id,original_print_job_id,reason,command_json,receipt_json,created_at)
            VALUES($branch,$request,$device,$requester,$approver,$cash,$originalSnapshot,$line,$originalJob,$reason,$command,$receipt,$time);
            INSERT INTO kitchen_reprint_audit(branch_id,request_id,reprint_print_job_id,device_id,requester_employee_id,
                approver_employee_id,reason,outcome,created_at)
            VALUES($branch,$request,$job,$device,$requester,$approver,$reason,'Queued',$time);
            """);
        foreach (var (name, value) in new (string, object)[] {
            ("$snapshot", snapshotId), ("$branch", branchId), ("$request", command.RequestId), ("$session", receipt.TableSessionId),
            ("$batch", receipt.OrderBatchId), ("$line", command.OrderLineId), ("$station", receipt.ProductionStationId),
            ("$originalSnapshot", command.SnapshotId), ("$originalJob", command.OriginalPrintJobId), ("$json", json),
            ("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))), ("$time", command.QueuedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("$job", printJobId), ("$device", command.DeviceId), ("$requester", command.RequesterEmployeeId),
            ("$approver", command.ApproverEmployeeId), ("$cash", command.CashRequestId), ("$reason", command.Reason),
            ("$command", JsonSerializer.Serialize(command)), ("$receipt", JsonSerializer.Serialize(receipt)) })
        { write.Parameters.AddWithValue(name, value); }
        await write.ExecuteNonQueryAsync(token);
        return receipt;
    }

    internal async Task<KitchenReprintStatus?> ReadKitchenReprintStatusAsync(string branchId, string requester,
        string device, string requestId, DateTimeOffset now, CancellationToken token)
    {
        foreach (var id in new[] { branchId, requester, device, requestId }) { ValidateReportId(id); }
        RequireClosingReportUtc(now);
        using var read = CreateCommand("""
            SELECT request.receipt_json,job.status,job.attempt_count,job.snapshot_id,job.table_session_id,job.target_station,
                job.original_print_job_id,job.is_reprint,job.purpose,job.request_id FROM kitchen_reprint_requests request
            LEFT JOIN print_jobs job ON job.branch_id=request.branch_id AND job.print_job_id=json_extract(request.receipt_json,'$.ReprintPrintJobId')
            WHERE request.branch_id=$branch AND request.request_id=$request
                AND request.requester_employee_id=$requester AND request.device_id=$device;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$request", requestId);
        read.Parameters.AddWithValue("$requester", requester); read.Parameters.AddWithValue("$device", device);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }
        if (reader.GetValue(0) is not string || reader.GetValue(2) is not long || reader.GetValue(7) is not long ||
            new[] { 1, 3, 4, 5, 6, 8, 9 }.Any(index => reader.GetValue(index) is not string))
        { throw new InvalidDataException("Recorded reprint job unavailable."); }
        var receipt = ReadReprintReceipt(reader.GetString(0), branchId, requestId);
        if (Enumerable.Range(1, 9).Any(reader.IsDBNull) || reader.GetString(3) != receipt.ReprintSnapshotId ||
            reader.GetString(4) != receipt.TableSessionId || reader.GetString(5) != receipt.ProductionStationId ||
            reader.GetString(6) != receipt.OriginalPrintJobId || reader.GetInt64(7) != 1 ||
            reader.GetString(8) != "KitchenOrder" || reader.GetString(9) != requestId)
        { throw new InvalidDataException("Recorded reprint job unavailable."); }
        var status = reader.GetString(1); var count = reader.GetInt64(2);
        if (status is not ("PrintQueued" or "Dispatching" or "PrintDispatched" or "Failed" or "Unknown" or "Completed") || count is < 0 or > int.MaxValue)
        { throw new InvalidDataException("Recorded reprint status unavailable."); }
        await reader.DisposeAsync();
        var recorded = await ReadRecordedReprintAsync(branchId, requestId, token)
            ?? throw new InvalidDataException("Recorded reprint unavailable.");
        if (recorded.Receipt != receipt || recorded.Command.RequesterEmployeeId != requester || recorded.Command.DeviceId != device)
        { throw new InvalidDataException("Recorded reprint owner unavailable."); }
        return new(1, branchId, now, receipt, status, (int)count);
    }

    private async Task<(KitchenReprintCommand Command, KitchenReprintReceipt Receipt)?> ReadRecordedReprintAsync(
        string branchId, string requestId, CancellationToken token)
    {
        using var read = CreateCommand("""
            SELECT r.command_json,r.receipt_json,r.device_id,r.requester_employee_id,r.approver_employee_id,
                r.cash_request_id,r.original_snapshot_id,r.order_line_id,r.original_print_job_id,r.reason,r.created_at,
                s.snapshot_id,s.table_session_id,s.order_batch_id,s.production_station_id,s.original_snapshot_id,
                s.order_line_id,s.original_print_job_id,s.created_at,a.reprint_print_job_id,a.device_id,
                a.requester_employee_id,a.approver_employee_id,a.reason,a.created_at,a.outcome
            FROM kitchen_reprint_requests r
            LEFT JOIN kitchen_reprint_snapshots s ON s.branch_id=r.branch_id AND s.request_id=r.request_id
            LEFT JOIN kitchen_reprint_audit a ON a.branch_id=r.branch_id AND a.request_id=r.request_id
            WHERE r.branch_id=$branch AND r.request_id=$request;
            """);
        read.Parameters.AddWithValue("$branch", branchId); read.Parameters.AddWithValue("$request", requestId);
        await using var reader = await read.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) { return null; }
        if (Enumerable.Range(0, 26).Any(index => reader.GetValue(index) is not string))
        { throw new InvalidDataException("Recorded reprint metadata unavailable."); }
        try
        {
            var json = reader.GetString(0);
            using var document = JsonDocument.Parse(json); CashEvidenceObject(document.RootElement);
            var command = JsonSerializer.Deserialize<KitchenReprintCommand>(json, ReprintJson)
                ?? throw new InvalidDataException("Recorded reprint command unavailable.");
            var receipt = ReadReprintReceipt(reader.GetString(1), branchId, requestId);
            var expected = new[] { command.DeviceId, command.RequesterEmployeeId, command.ApproverEmployeeId,
                command.CashRequestId, command.SnapshotId, command.OrderLineId, command.OriginalPrintJobId, command.Reason };
            if (command.RequestId != requestId || expected.Where((value, index) => value != reader.GetString(index + 2)).Any() ||
                command.CashRequestId != receipt.CashRequestId || command.SnapshotId != receipt.OriginalSnapshotId ||
                command.OrderLineId != receipt.OrderLineId || command.OriginalPrintJobId != receipt.OriginalPrintJobId ||
                command.QueuedAt != receipt.QueuedAt || ProductionTimestamp(reader.GetString(10)) != receipt.QueuedAt ||
                reader.GetString(11) != receipt.ReprintSnapshotId || reader.GetString(12) != receipt.TableSessionId ||
                reader.GetString(13) != receipt.OrderBatchId || reader.GetString(14) != receipt.ProductionStationId ||
                reader.GetString(15) != receipt.OriginalSnapshotId || reader.GetString(16) != receipt.OrderLineId ||
                reader.GetString(17) != receipt.OriginalPrintJobId || ProductionTimestamp(reader.GetString(18)) != receipt.QueuedAt ||
                reader.GetString(19) != receipt.ReprintPrintJobId || reader.GetString(20) != command.DeviceId ||
                reader.GetString(21) != command.RequesterEmployeeId || reader.GetString(22) != command.ApproverEmployeeId ||
                reader.GetString(23) != command.Reason || ProductionTimestamp(reader.GetString(24)) != receipt.QueuedAt ||
                reader.GetString(25) != "Queued")
            { throw new InvalidDataException("Recorded reprint metadata does not agree."); }
            foreach (var id in new[] { command.DeviceId, command.RequesterEmployeeId, command.ApproverEmployeeId }) { ValidateReportId(id); }
            if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 4096 ||
                command.Reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            { throw new InvalidDataException("Recorded reprint reason unavailable."); }
            ProductionTimestamp(document.RootElement.GetProperty("QueuedAt").GetString()!);
            RequireClosingReportUtc(command.QueuedAt);
            return (command, receipt);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Recorded reprint metadata unavailable.", error); }
    }
    private static KitchenReprintReceipt ReadReprintReceipt(string json, string branchId, string requestId)
    {
        try
        {
            using var document = JsonDocument.Parse(json); CashEvidenceObject(document.RootElement);
            var receipt = JsonSerializer.Deserialize<KitchenReprintReceipt>(json, ReprintJson) ?? throw new InvalidDataException("Recorded reprint unavailable.");
            if (receipt.SchemaVersion != 1 || receipt.BranchId != branchId || receipt.RequestId != requestId ||
                receipt.ReprintSnapshotId != $"{branchId}:{requestId}:kitchen-reprint-snapshot:1" ||
                receipt.ReprintPrintJobId != $"{branchId}:{requestId}:kitchen-reprint-print:1")
            { throw new InvalidDataException("Recorded reprint identity unavailable."); }
            foreach (var id in new[] { receipt.CashRequestId, receipt.TableSessionId, receipt.OrderBatchId, receipt.OrderLineId, receipt.ProductionStationId })
            { ValidateReportId(id); }
            foreach (var id in new[] { receipt.OriginalSnapshotId, receipt.OriginalPrintJobId, receipt.ReprintSnapshotId, receipt.ReprintPrintJobId })
            { ProductionIdentifier(id, 1024); }
            ProductionTimestamp(document.RootElement.GetProperty("QueuedAt").GetString()!);
            RequireClosingReportUtc(receipt.QueuedAt);
            return receipt;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Recorded reprint receipt unavailable.", error); }
    }
}
