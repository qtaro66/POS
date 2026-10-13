using Pos.Domain.Billing;

namespace Pos.Application.MasterData;

public sealed record ActiveMasterDataSnapshot
{
    public ActiveMasterDataSnapshot(
        string masterDataVersion,
        int packageSchemaVersion,
        string checksumSha256,
        DateTimeOffset publishedAt,
        DateTimeOffset activatedAt,
        BillPolicySnapshot billingPolicy)
    {
        MasterDataVersion = Require(masterDataVersion, nameof(masterDataVersion));
        if (packageSchemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(packageSchemaVersion));
        }

        ChecksumSha256 = RequireSha256(checksumSha256, nameof(checksumSha256));
        if (activatedAt < publishedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activatedAt),
                "The activation timestamp cannot precede publication.");
        }

        BillingPolicy = billingPolicy ?? throw new ArgumentNullException(nameof(billingPolicy));
        if (!string.Equals(
                MasterDataVersion,
                BillingPolicy.MasterDataVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The billing policy must belong to the active Master Data version.",
                nameof(billingPolicy));
        }

        PackageSchemaVersion = packageSchemaVersion;
        PublishedAt = publishedAt;
        ActivatedAt = activatedAt;
    }

    public string MasterDataVersion { get; }

    public int PackageSchemaVersion { get; }

    public string ChecksumSha256 { get; }

    public DateTimeOffset PublishedAt { get; }

    public DateTimeOffset ActivatedAt { get; }

    public BillPolicySnapshot BillingPolicy { get; }

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();

    private static string RequireSha256(string value, string name)
    {
        var normalized = Require(value, name).ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The package checksum must be a 64-character SHA-256 hexadecimal value.",
                name);
        }

        return normalized;
    }
}
