using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionIdentityHandlerTests
{
    [Fact]
    public async Task Matching_authenticated_branch_is_ingested()
    {
        var store = new RecordingStore();
        var response = await BranchCloudEventIngestionIdentityHandler.HandleAsync(
            Request("BRANCH-01"),
            AuthenticatedCentralBranchIdentity.Create(" BRANCH-01 "),
            new BranchCloudEventIngestionService(store));

        Assert.Equal("BRANCH-01", response.BranchId);
        Assert.Equal(1, store.CallCount);
    }

    [Fact]
    public async Task Different_authenticated_branch_is_rejected_before_persistence()
    {
        var store = new RecordingStore();
        var exception = await Assert.ThrowsAsync<
            BranchCloudEventBranchMismatchException>(() =>
            BranchCloudEventIngestionIdentityHandler.HandleAsync(
                Request("BRANCH-02"),
                AuthenticatedCentralBranchIdentity.Create("BRANCH-01"),
                new BranchCloudEventIngestionService(store)));

        Assert.Equal("BRANCH-01", exception.AuthenticatedBranchId);
        Assert.Equal("BRANCH-02", exception.RequestedBranchId);
        Assert.Equal(0, store.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_authenticated_branch_is_rejected(string branchId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            AuthenticatedCentralBranchIdentity.Create(branchId));

        Assert.Equal("branchId", exception.ParamName);
    }

    [Fact]
    public async Task Missing_arguments_are_rejected()
    {
        var identity = AuthenticatedCentralBranchIdentity.Create("BRANCH-01");
        var service = new BranchCloudEventIngestionService(new RecordingStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionIdentityHandler.HandleAsync(
                null!,
                identity,
                service));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionIdentityHandler.HandleAsync(
                Request("BRANCH-01"),
                null!,
                service));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionIdentityHandler.HandleAsync(
                Request("BRANCH-01"),
                identity,
                null!));
    }

    private static BranchCloudEventIngestionRequest Request(string branchId) =>
        new(
            1,
            "EVENT-01",
            branchId,
            7,
            "REQUEST-01",
            "SESSION-01",
            nameof(TableSessionAction.SubmitOrder),
            3,
            new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));

    private sealed class RecordingStore : IBranchCloudEventIngestionStore
    {
        public int CallCount { get; private set; }

        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new BranchCloudEventPersistenceResult(
                BranchCloudEventPersistenceDisposition.Stored,
                envelope.EventId,
                envelope.BranchId,
                envelope.BranchSequence));
        }
    }
}
