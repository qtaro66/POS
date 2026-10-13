using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pos.Domain.TableSessions;

namespace Pos.Application.Printing;

public sealed record BillPreviewContent
{
    private BillPreviewContent(int schemaVersion, string json, string sha256)
    {
        SchemaVersion = schemaVersion;
        Json = json;
        Sha256 = sha256;
    }

    public int SchemaVersion { get; }

    public string Json { get; }

    public string Sha256 { get; }

    public static BillPreviewContent FromJson(int schemaVersion, string json)
    {
        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The Bill Preview payload cannot be empty.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The Bill Preview payload root must be a JSON object.", nameof(json));
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new BillPreviewContent(schemaVersion, json, sha256);
    }
}

public sealed record BillPreviewCaptureRequest(
    string BranchId,
    string RequestId,
    TableSessionId TableSessionId,
    TableId TableId,
    DateTimeOffset CapturedAt);

public sealed record BillPreviewSnapshot(
    string SnapshotId,
    string BranchId,
    string RequestId,
    TableSessionId TableSessionId,
    TableId TableId,
    string DocumentLabel,
    BillPreviewContent Content,
    DateTimeOffset CreatedAt);

public enum PrintJobPurpose
{
    BillPreview
}

public enum PrintTargetStation
{
    Cashier
}

public enum PersistentPrintJobStatus
{
    PrintQueued,
    Dispatching,
    PrintDispatched,
    Failed,
    Unknown,
    Completed
}

public sealed record PersistentPrintJob(
    string PrintJobId,
    string BranchId,
    string RequestId,
    TableSessionId TableSessionId,
    string SnapshotId,
    PrintJobPurpose Purpose,
    PrintTargetStation TargetStation,
    PersistentPrintJobStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    string? OriginalPrintJobId = null,
    bool IsReprint = false);
