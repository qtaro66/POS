using System.Security.Cryptography;
using System.Text.Json;

namespace Pos.Contracts.Ordering;

public sealed record PublishedTableRegistry(string Payload, string Sha256);

public sealed record TableRegistry(int SchemaVersion, string BranchId, string MasterDataVersion, RegistryTable[] Tables)
{
    public static TableRegistry Read(PublishedTableRegistry publication, string branchId)
    {
        var payload = Convert.FromBase64String(publication.Payload);
        if (payload.Length > 1_000_000 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), Convert.FromHexString(publication.Sha256)))
        {
            throw new InvalidDataException("Invalid table registry checksum or size.");
        }

        var registry = JsonSerializer.Deserialize<TableRegistry>(payload, JsonSerializerOptions.Web);
        if (registry is null || registry.SchemaVersion != 1 || registry.BranchId != branchId ||
            !ValidText(registry.MasterDataVersion) || registry.Tables is null || registry.Tables.Length > 5000)
        {
            throw new InvalidDataException("Invalid table registry.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in registry.Tables)
        {
            if (table is null || !ValidText(table.TableId) || !ValidText(table.DisplayName) ||
                (table.Zone is not null && !ValidText(table.Zone)) || table.DisplayOrder < 0 || !ids.Add(table.TableId))
            {
                throw new InvalidDataException("Invalid table registry entry.");
            }
        }

        return registry;
    }

    private static bool ValidText(string? text) =>
        text is { Length: > 0 and <= 128 } && text == text.Trim() && !text.Any(char.IsControl);
}

public sealed record RegistryTable(string TableId, string DisplayName, string? Zone, int DisplayOrder);
