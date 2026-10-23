using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudDataSentStatusTests
{
    [Fact]
    public void Constructor_normalizes_branch_and_timestamp()
    {
        var localTimestamp = new DateTimeOffset(
            2026,
            10,
            23,
            18,
            30,
            0,
            TimeSpan.FromHours(7));

        var status = new BranchCloudDataSentStatus(
            " BRANCH-01 ",
            localTimestamp);

        Assert.Equal("BRANCH-01", status.BranchId);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 23, 11, 30, 0, TimeSpan.Zero),
            status.LastSuccessfullySentAt);
        Assert.Equal(TimeSpan.Zero, status.LastSuccessfullySentAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_rejects_empty_branch_identifier(string branchId)
    {
        Assert.Throws<ArgumentException>(() =>
            new BranchCloudDataSentStatus(
                branchId,
                DateTimeOffset.UtcNow));
    }
}
