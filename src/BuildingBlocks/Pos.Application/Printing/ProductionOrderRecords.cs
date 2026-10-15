using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pos.Domain.TableSessions;

namespace Pos.Application.Printing;

public sealed record ProductionOrderContent
{
    private ProductionOrderContent(int schemaVersion, string json, string sha256)
    {
        SchemaVersion = schemaVersion;
        Json = json;
        Sha256 = sha256;
    }

    public int SchemaVersion { get; }

    public string Json { get; }

    public string Sha256 { get; }

    public static ProductionOrderContent FromJson(int schemaVersion, string json)
    {
        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("The production order payload cannot be empty.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "The production order payload root must be a JSON object.",
                nameof(json));
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new ProductionOrderContent(schemaVersion, json, sha256);
    }
}

public sealed record ProductionOrderSnapshot(
    string SnapshotId,
    string BranchId,
    string RequestId,
    string OrderBatchId,
    TableSessionId TableSessionId,
    TableId TableId,
    string ProductionStationId,
    ProductionOrderContent Content,
    DateTimeOffset CreatedAt);
