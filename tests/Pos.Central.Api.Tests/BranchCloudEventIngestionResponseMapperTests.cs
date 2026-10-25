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
            BranchCloudEventIngestionResponseMapper.Map(null!));
    }
}
