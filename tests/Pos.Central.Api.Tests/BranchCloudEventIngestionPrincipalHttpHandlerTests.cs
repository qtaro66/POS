using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Synchronization;
using Pos.Central.Api.Synchronization;
using Pos.Contracts.Synchronization;
using Pos.Domain.TableSessions;

namespace Pos.Central.Api.Tests;

public sealed class BranchCloudEventIngestionPrincipalHttpHandlerTests
{
    [Fact]
    public async Task Matching_authenticated_branch_returns_acknowledgment()
    {
        var store = new RecordingStore();

        var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
            Principal("BRANCH-01", authenticated: true),
            Request("BRANCH-01"),
            new BranchCloudEventIngestionService(store));

        var ok = Assert.IsType<Ok<BranchCloudEventIngestionResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal("EVENT-01", ok.Value?.EventId);
        Assert.Equal("BRANCH-01", ok.Value?.BranchId);
        Assert.Equal(1, store.CallCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Missing_or_unauthenticated_principal_returns_401_without_persistence(
        bool includePrincipal,
        bool authenticated)
    {
        var store = new RecordingStore();
        var principal = includePrincipal
            ? Principal("BRANCH-01", authenticated)
            : null;

        var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
            principal,
            Request("BRANCH-01"),
            new BranchCloudEventIngestionService(store));

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Branch_mismatch_returns_403_without_persistence()
    {
        var store = new RecordingStore();

        var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
            Principal("BRANCH-01", authenticated: true),
            Request("BRANCH-02"),
            new BranchCloudEventIngestionService(store));

        Assert.IsType<ForbidHttpResult>(result.Result);
        Assert.Equal(0, store.CallCount);
    }

    [Fact]
    public async Task Persistence_identity_conflict_returns_409()
    {
        var result = await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
            Principal("BRANCH-01", authenticated: true),
            Request("BRANCH-01"),
            new BranchCloudEventIngestionService(new ConflictingStore()));

        var conflict = Assert.IsType<StatusCodeHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_persistence()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new RecordingStore();

        await BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
            Principal("BRANCH-01", authenticated: true),
            Request("BRANCH-01"),
            new BranchCloudEventIngestionService(store),
            cancellation.Token);

        Assert.Equal(cancellation.Token, store.CancellationToken);
    }

    [Fact]
    public async Task Missing_dependencies_are_rejected_before_identity_resolution()
    {
        var service = new BranchCloudEventIngestionService(new RecordingStore());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
                null,
                null!,
                service));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudEventIngestionPrincipalHttpHandler.HandleAsync(
                null,
                Request("BRANCH-01"),
                null!));
    }

    private static ClaimsPrincipal Principal(string branchId, bool authenticated) =>
        new(new ClaimsIdentity(
            [new Claim(CentralCloudClaimTypes.BranchId, branchId)],
            authenticated ? "SyntheticAuthentication" : null));

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
            new DateTimeOffset(2026, 10, 26, 3, 30, 0, TimeSpan.Zero));

    private sealed class RecordingStore : IBranchCloudEventIngestionStore
    {
        public int CallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            CancellationToken = cancellationToken;
            return Task.FromResult(new BranchCloudEventPersistenceResult(
                BranchCloudEventPersistenceDisposition.Stored,
                envelope.EventId,
                envelope.BranchId,
                envelope.BranchSequence));
        }
    }

    private sealed class ConflictingStore : IBranchCloudEventIngestionStore
    {
        public Task<BranchCloudEventPersistenceResult> PersistAsync(
            BranchCloudOutboxEventEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromException<BranchCloudEventPersistenceResult>(
                new BranchCloudEventUniquenessConflictException(
                    envelope.EventId,
                    envelope.BranchId,
                    envelope.BranchSequence));
    }
}
