using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionResponseMapperTests
{
    [Theory]
    [InlineData(BranchCloudEventPersistenceDisposition.Stored)]
    [InlineData(BranchCloudEventPersistenceDisposition.AlreadyStored)]
    public void Durable_persistence_maps_to_versioned_identity_response(
        BranchCloudEventPersistenceDisposition disposition)
    {
        var result = new BranchCloudEventPersistenceResult(
            disposition,
            "EVENT-01",
            "BRANCH-01",
            7);

        var response = BranchCloudEventIngestionResponseMapper.Map(result);

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("EVENT-01", response.EventId);
        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(7, response.BranchSequence);
    }

    [Fact]
    public void Missing_persistence_result_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudEventIngestionResponseMapper.Map(
                (BranchCloudEventPersistenceResult)null!));
    }

    [Fact]
    public void Sequence_gap_maps_to_versioned_machine_readable_response()
    {
        var response = BranchCloudEventIngestionResponseMapper.Map(
            new BranchCloudEventSequenceGapException("BRANCH-01", 6, 8));

        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal("BranchSequenceGap", response.ErrorCode);
        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(6, response.ExpectedBranchSequence);
        Assert.Equal(8, response.ReceivedBranchSequence);
    }

    [Fact]
    public void Missing_sequence_gap_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudEventIngestionResponseMapper.Map(
                (BranchCloudEventSequenceGapException)null!));
    }
}
