using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusHttpHandlerTests
{
    [Fact]
    public async Task Latest_status_returns_200_with_public_contract()
    {
        var sentAt =
            new DateTimeOffset(2026, 10, 23, 12, 30, 0, TimeSpan.Zero);
        var query = new RecordingQuery(
            new BranchCloudDataSentStatus("BRANCH-01", sentAt));

        var result = await BranchCloudDataSentStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.SchemaVersion);
        Assert.Equal(sentAt, result.Value.LastSentAt);
        Assert.Equal("BRANCH-01", query.BranchId);
    }

    [Fact]
    public async Task Never_sent_returns_200_with_null_timestamp()
    {
        var query = new RecordingQuery((BranchCloudDataSentStatus?)null);

        var result = await BranchCloudDataSentStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value.LastSentAt);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_authoritative_query()
    {
        using var source = new CancellationTokenSource();
        var query = new RecordingQuery((BranchCloudDataSentStatus?)null);

        await BranchCloudDataSentStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            source.Token);

        Assert.Equal(source.Token, query.CancellationToken);
    }

    [Fact]
    public async Task Missing_request_identity_is_rejected_before_query()
    {
        var query = new RecordingQuery((BranchCloudDataSentStatus?)null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudDataSentStatusHttpHandler.HandleAsync(
                null!,
                query,
                CancellationToken.None));

        Assert.Null(query.BranchId);
    }

    [Fact]
    public async Task Missing_query_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCloudDataSentStatusHttpHandler.HandleAsync(
                Identity(),
                null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task Query_failure_is_not_recast_as_never_sent()
    {
        var query = new RecordingQuery(
            new InvalidOperationException("Synthetic query failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchCloudDataSentStatusHttpHandler.HandleAsync(
                Identity(),
                query,
                CancellationToken.None));

        Assert.Equal("Synthetic query failure.", exception.Message);
    }

    private static AuthenticatedBranchRequestIdentity Identity()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(BranchPosClaimTypes.BranchId, " BRANCH-01 "),
                new Claim(BranchPosClaimTypes.DeviceId, "DEVICE-01"),
                new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01")
            ],
            "TestAuthentication"));
        Assert.True(
            AuthenticatedBranchRequestIdentityResolver.TryResolve(
                principal,
                out var identity));
        return identity!;
    }

    private sealed class RecordingQuery : IBranchCloudDataSentStatusQuery
    {
        private readonly BranchCloudDataSentStatus? _status;
        private readonly Exception? _exception;

        public RecordingQuery(BranchCloudDataSentStatus? status)
        {
            _status = status;
        }

        public RecordingQuery(Exception exception)
        {
            _exception = exception;
        }

        public string? BranchId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BranchCloudDataSentStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_status)
                : Task.FromException<BranchCloudDataSentStatus?>(_exception);
        }
    }
}
