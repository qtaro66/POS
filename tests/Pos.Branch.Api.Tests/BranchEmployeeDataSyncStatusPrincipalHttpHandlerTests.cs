using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class BranchEmployeeDataSyncStatusPrincipalHttpHandlerTests
{
    [Fact]
    public async Task Complete_authenticated_identity_queries_its_branch()
    {
        var synchronizedAt =
            new DateTimeOffset(2026, 10, 23, 6, 45, 0, TimeSpan.Zero);
        var query = new RecordingQuery(
            new BranchEmployeeDataSyncStatus("BRANCH-01", synchronizedAt));
        var httpResponse = new DefaultHttpContext().Response;

        var result =
            await BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync(
                Principal(includeEmployee: true, authenticated: true),
                query,
                httpResponse,
                CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchEmployeeDataSyncStatusResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(synchronizedAt, ok.Value?.LastSynchronizedAt);
        Assert.Equal("BRANCH-01", query.BranchId);
        Assert.Equal("no-store", httpResponse.Headers.CacheControl);
        Assert.Equal("no-cache", httpResponse.Headers.Pragma);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Incomplete_or_unauthenticated_identity_returns_401_without_query(
        bool includeEmployee,
        bool authenticated)
    {
        var query = new RecordingQuery(null);
        var httpResponse = new DefaultHttpContext().Response;

        var result =
            await BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync(
                Principal(includeEmployee, authenticated),
                query,
                httpResponse,
                CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Null(query.BranchId);
        Assert.Equal("no-store", httpResponse.Headers.CacheControl);
        Assert.Equal("no-cache", httpResponse.Headers.Pragma);
    }

    [Fact]
    public async Task Missing_principal_returns_same_401_without_query()
    {
        var query = new RecordingQuery(null);

        var result =
            await BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync(
                null,
                query,
                new DefaultHttpContext().Response,
                CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result.Result);
        Assert.Null(query.BranchId);
    }

    [Fact]
    public async Task Missing_query_is_rejected_before_identity_resolution()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync(
                null,
                null!,
                new DefaultHttpContext().Response,
                CancellationToken.None));
    }

    [Fact]
    public async Task Missing_http_response_is_rejected_before_query()
    {
        var query = new RecordingQuery(null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchEmployeeDataSyncStatusPrincipalHttpHandler.HandleAsync(
                Principal(includeEmployee: true, authenticated: true),
                query,
                null!,
                CancellationToken.None));

        Assert.Null(query.BranchId);
    }

    private static ClaimsPrincipal Principal(bool includeEmployee, bool authenticated)
    {
        var claims = new List<Claim>
        {
            new(BranchPosClaimTypes.BranchId, "BRANCH-01"),
            new(BranchPosClaimTypes.DeviceId, "DEVICE-01")
        };
        if (includeEmployee)
        {
            claims.Add(new Claim(BranchPosClaimTypes.EmployeeId, "EMPLOYEE-01"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticated ? "SyntheticAuthentication" : null));
    }

    private sealed class RecordingQuery : IBranchEmployeeDataSyncStatusQuery
    {
        private readonly BranchEmployeeDataSyncStatus? _status;

        public RecordingQuery(BranchEmployeeDataSyncStatus? status)
        {
            _status = status;
        }

        public string? BranchId { get; private set; }

        public Task<BranchEmployeeDataSyncStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            return Task.FromResult(_status);
        }
    }
}
