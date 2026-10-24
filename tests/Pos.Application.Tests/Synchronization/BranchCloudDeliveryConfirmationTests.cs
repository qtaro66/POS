using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudDeliveryConfirmationTests
{
    [Fact]
    public void Captures_trimmed_identity_and_utc_acknowledgment()
    {
        var acknowledgedAt = new DateTimeOffset(
            2026,
            10,
            24,
            4,
            30,
            0,
            TimeSpan.Zero);

        var confirmation = new BranchCloudDeliveryConfirmation(
            " BRANCH-01 ",
            " OUTBOX-001 ",
            acknowledgedAt);

        Assert.Equal("BRANCH-01", confirmation.BranchId);
        Assert.Equal("OUTBOX-001", confirmation.OutboxId);
        Assert.Equal(acknowledgedAt, confirmation.AcknowledgedAt);
        Assert.Equal(TimeSpan.Zero, confirmation.AcknowledgedAt.Offset);
    }

    [Theory]
    [InlineData(null, "OUTBOX-001", "branchId")]
    [InlineData("", "OUTBOX-001", "branchId")]
    [InlineData("   ", "OUTBOX-001", "branchId")]
    [InlineData("BRANCH-01", null, "outboxId")]
    [InlineData("BRANCH-01", "", "outboxId")]
    [InlineData("BRANCH-01", "   ", "outboxId")]
    public void Empty_identity_is_rejected(
        string? branchId,
        string? outboxId,
        string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudDeliveryConfirmation(
                branchId!,
                outboxId!,
                DateTimeOffset.UnixEpoch));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void Non_utc_acknowledgment_is_rejected()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BranchCloudDeliveryConfirmation(
                "BRANCH-01",
                "OUTBOX-001",
                new DateTimeOffset(
                    2026,
                    10,
                    24,
                    11,
                    30,
                    0,
                    TimeSpan.FromHours(7))));

        Assert.Equal("acknowledgedAt", exception.ParamName);
    }

    [Fact]
    public void Result_values_are_stable_and_distinct()
    {
        Assert.Equal(0, (int)BranchCloudDeliveryConfirmationResult.Confirmed);
        Assert.Equal(1, (int)BranchCloudDeliveryConfirmationResult.AlreadyConfirmed);
        Assert.Equal(2, (int)BranchCloudDeliveryConfirmationResult.NotFound);
    }
}
