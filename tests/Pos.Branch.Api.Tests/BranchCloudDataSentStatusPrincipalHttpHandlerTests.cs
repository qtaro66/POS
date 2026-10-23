using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Synchronization;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusPrincipalHttpHandlerTests
{
    [Fact]
    public async Task Complete_authenticated_identity_queries_its_branch()
    {
        var sentAt =
            new DateTimeOffset(2026, 10, 23, 12, 45, 0, TimeSpan.Zero);
        var query = new RecordingQuery(
            new BranchCloudDataSentStatus("BRANCH-01", sentAt));
        var httpResponse = new DefaultHttpContext().Response;

        var result =
            await BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync(
                Principal(includeEmployee: true, authenticated: true),
                query,
                httpResponse,
                CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchCloudDataSentStatusResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(sentAt, ok.Value?.LastSentAt);
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
            await BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync(
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
            await BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync(
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
            BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync(
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
            BranchCloudDataSentStatusPrincipalHttpHandler.HandleAsync(
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

    private sealed class RecordingQuery : IBranchCloudDataSentStatusQuery
    {
        private readonly BranchCloudDataSentStatus? _status;

        public RecordingQuery(BranchCloudDataSentStatus? status)
        {
            _status = status;
        }

        public string? BranchId { get; private set; }

        public Task<BranchCloudDataSentStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            return Task.FromResult(_status);
        }
    }
}
