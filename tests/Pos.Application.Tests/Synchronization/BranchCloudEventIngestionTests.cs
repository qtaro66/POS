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

    [Fact]
    public void Sequence_gap_preserves_branch_and_missing_sequence_boundary()
    {
        var exception = new BranchCloudEventSequenceGapException(
            " BRANCH-01 ",
            6,
            8);

        Assert.Equal("BRANCH-01", exception.BranchId);
        Assert.Equal(6, exception.ExpectedBranchSequence);
        Assert.Equal(8, exception.ReceivedBranchSequence);
    }

    [Theory]
    [InlineData("", 6, 8, "branchId")]
    [InlineData("BRANCH-01", 0, 8, "expectedBranchSequence")]
    [InlineData("BRANCH-01", 6, 6, "receivedBranchSequence")]
    [InlineData("BRANCH-01", 6, 5, "receivedBranchSequence")]
    public void Invalid_sequence_gap_is_rejected(
        string branchId,
        long expectedBranchSequence,
        long receivedBranchSequence,
        string parameterName)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() =>
            new BranchCloudEventSequenceGapException(
                branchId,
                expectedBranchSequence,
                receivedBranchSequence));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
