using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeDataSyncStatusHttpHandlerTests
{
    [Fact]
    public async Task Latest_status_returns_200_with_public_contract()
    {
        var synchronizedAt =
            new DateTimeOffset(2026, 10, 23, 6, 30, 0, TimeSpan.Zero);
        var query = new RecordingQuery(
            new BranchEmployeeDataSyncStatus("BRANCH-01", synchronizedAt));

        var result = await BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Equal(1, result.Value.SchemaVersion);
        Assert.Equal(synchronizedAt, result.Value.LastSynchronizedAt);
        Assert.Equal("BRANCH-01", query.BranchId);
    }

    [Fact]
    public async Task Never_synchronized_returns_200_with_null_timestamp()
    {
        var query = new RecordingQuery((BranchEmployeeDataSyncStatus?)null);

        var result = await BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value.LastSynchronizedAt);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_authoritative_query()
    {
        using var source = new CancellationTokenSource();
        var query = new RecordingQuery((BranchEmployeeDataSyncStatus?)null);

        await BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
            Identity(),
            query,
            source.Token);

        Assert.Equal(source.Token, query.CancellationToken);
    }

    [Fact]
    public async Task Missing_request_identity_is_rejected_before_query()
    {
        var query = new RecordingQuery((BranchEmployeeDataSyncStatus?)null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
                null!,
                query,
                CancellationToken.None));

        Assert.Null(query.BranchId);
    }

    [Fact]
    public async Task Missing_query_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
                Identity(),
                null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task Query_failure_is_not_recast_as_never_synchronized()
    {
        var query = new RecordingQuery(
            new InvalidOperationException("Synthetic query failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchEmployeeDataSyncStatusHttpHandler.HandleAsync(
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

    private sealed class RecordingQuery : IBranchEmployeeDataSyncStatusQuery
    {
        private readonly BranchEmployeeDataSyncStatus? _status;
        private readonly Exception? _exception;

        public RecordingQuery(BranchEmployeeDataSyncStatus? status)
        {
            _status = status;
        }

        public RecordingQuery(Exception exception)
        {
            _exception = exception;
        }

        public string? BranchId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_status)
                : Task.FromException<BranchEmployeeDataSyncStatus?>(_exception);
        }
    }
}
