using Pos.Application.Synchronization;

namespace Pos.Application.Tests.Synchronization;

public sealed class BranchCloudEventIngestionTests
{
    [Theory]
    [InlineData(BranchCloudEventPersistenceDisposition.Stored)]
    [InlineData(BranchCloudEventPersistenceDisposition.AlreadyStored)]
    public void Persistence_result_normalizes_durable_identity(
        BranchCloudEventPersistenceDisposition disposition)
    {
        var result = new BranchCloudEventPersistenceResult(
            disposition,
            " EVENT-01 ",
            " BRANCH-01 ",
            7);

        Assert.Equal(disposition, result.Disposition);
        Assert.Equal("EVENT-01", result.EventId);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal(7, result.BranchSequence);
    }

    [Fact]
    public void Undefined_persistence_disposition_is_rejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchCloudEventPersistenceResult(
                (BranchCloudEventPersistenceDisposition)99,
                "EVENT-01",
                "BRANCH-01",
                7));

        Assert.Equal("disposition", exception.ParamName);
    }

    [Theory]
    [InlineData("", "BRANCH-01", 7, "eventId")]
    [InlineData("EVENT-01", " ", 7, "branchId")]
    [InlineData("EVENT-01", "BRANCH-01", 0, "branchSequence")]
    public void Invalid_persisted_identity_is_rejected(
        string eventId,
        string branchId,
        long branchSequence,
        string parameterName)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new BranchCloudEventPersistenceResult(
                BranchCloudEventPersistenceDisposition.Stored,
                eventId,
                branchId,
                branchSequence));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Uniqueness_conflict_preserves_conflicting_identity()
    {
        var exception = new BranchCloudEventUniquenessConflictException(
            " EVENT-02 ",
            " BRANCH-01 ",
            7);

        Assert.Equal("EVENT-02", exception.EventId);
        Assert.Equal("BRANCH-01", exception.BranchId);
        Assert.Equal(7, exception.BranchSequence);
    }
}
