using Pos.Application.MasterData;
using Pos.Domain.Billing;

namespace Pos.Application.Tests.MasterData;

public sealed class ActiveMasterDataSnapshotTests
{
    private static readonly DateTimeOffset PublishedAt =
        new(2026, 10, 13, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_snapshot_normalizes_checksum()
    {
        var snapshot = new ActiveMasterDataSnapshot(
            "MASTER-1",
            1,
            new string('a', 64),
            PublishedAt,
            PublishedAt.AddMinutes(1),
            Policy("MASTER-1"));

        Assert.Equal(new string('A', 64), snapshot.ChecksumSha256);
    }

    [Fact]
    public void Billing_policy_from_another_package_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ActiveMasterDataSnapshot(
            "MASTER-1",
            1,
            new string('A', 64),
            PublishedAt,
            PublishedAt.AddMinutes(1),
            Policy("MASTER-2")));
    }

    [Fact]
    public void Activation_before_publication_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveMasterDataSnapshot(
            "MASTER-1",
            1,
            new string('A', 64),
            PublishedAt,
            PublishedAt.AddTicks(-1),
            Policy("MASTER-1")));
    }

    [Fact]
    public void Malformed_checksum_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ActiveMasterDataSnapshot(
            "MASTER-1",
            1,
            new string('Z', 64),
            PublishedAt,
            PublishedAt.AddMinutes(1),
            Policy("MASTER-1")));
    }

    private static BillPolicySnapshot Policy(string masterDataVersion) =>
        new(
            masterDataVersion,
            "CALC-1",
            7m,
            0m,
            2,
            MonetaryRoundingMode.AwayFromZero);
}
