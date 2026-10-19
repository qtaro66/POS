using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pos.Application.TableSessions;
using Pos.Branch.Api.Commands;
using Pos.Branch.Api.Security;
using Pos.Contracts.Commands;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCommandStatusHttpHandlerTests
{
    [Theory]
    [InlineData(BranchCommandOutcome.BranchAccepted, "BranchAccepted")]
    [InlineData(BranchCommandOutcome.Rejected, "Rejected")]
    public async Task Durable_result_returns_200_with_public_contract(
        BranchCommandOutcome outcome,
        string expectedStatus)
    {
        var query = new RecordingQuery(Receipt(outcome));

        var result = await BranchCommandStatusHttpHandler.HandleAsync(
            Identity(),
            " REQ-01 ",
            query,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<BranchCommandStatusResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.NotNull(ok.Value);
        Assert.Equal(expectedStatus, ok.Value.Status);
        Assert.Equal("BRANCH-01", query.BranchId);
        Assert.Equal("REQ-01", query.RequestId);
    }

    [Fact]
    public async Task Missing_receipt_returns_404_no_durable_result()
    {
        var query = new RecordingQuery((BranchCommandReceipt?)null);

        var result = await BranchCommandStatusHttpHandler.HandleAsync(
            Identity(),
            "REQ-MISSING",
            query,
            CancellationToken.None);

        var notFound = Assert.IsType<NotFound<BranchCommandStatusResponse>>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.NotNull(notFound.Value);
        Assert.Equal(BranchCommandStatusValues.NoDurableResult, notFound.Value.Status);
        Assert.Equal("REQ-MISSING", notFound.Value.RequestId);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_authoritative_query()
    {
        using var source = new CancellationTokenSource();
        var query = new RecordingQuery((BranchCommandReceipt?)null);

        await BranchCommandStatusHttpHandler.HandleAsync(
            Identity(),
            "REQ-01",
            query,
            source.Token);

        Assert.Equal(source.Token, query.CancellationToken);
    }

    [Fact]
    public async Task Missing_request_identity_is_rejected_before_query()
    {
        var query = new RecordingQuery((BranchCommandReceipt?)null);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BranchCommandStatusHttpHandler.HandleAsync(
                null!,
                "REQ-01",
                query,
                CancellationToken.None));

        Assert.Null(query.BranchId);
        Assert.Null(query.RequestId);
    }

    [Fact]
    public async Task Missing_request_id_is_rejected_before_query()
    {
        var query = new RecordingQuery((BranchCommandReceipt?)null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            BranchCommandStatusHttpHandler.HandleAsync(
                Identity(),
                " ",
                query,
                CancellationToken.None));

        Assert.Null(query.BranchId);
        Assert.Null(query.RequestId);
    }

    [Fact]
    public async Task Query_failure_is_not_recast_as_missing_result()
    {
        var query = new RecordingQuery(
            new InvalidOperationException("Synthetic query failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BranchCommandStatusHttpHandler.HandleAsync(
                Identity(),
                "REQ-01",
                query,
                CancellationToken.None));

        Assert.Equal("Synthetic query failure.", exception.Message);
    }

    private static BranchCommandReceipt Receipt(BranchCommandOutcome outcome) =>
        new(
            "REQ-01",
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            DateTimeOffset.Parse("2026-10-19T01:00:00Z"),
            outcome,
            TableSessionId.From("SESSION-01"),
            TableSessionStatus.Ordering,
            2,
            outcome is BranchCommandOutcome.Rejected ? "RejectedForTest" : null,
            DateTimeOffset.Parse("2026-10-19T01:00:01Z"),
            null,
            9);

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

    private sealed class RecordingQuery : IBranchCommandStatusQuery
    {
        private readonly BranchCommandReceipt? _receipt;
        private readonly Exception? _exception;

        public RecordingQuery(BranchCommandReceipt? receipt)
        {
            _receipt = receipt;
        }

        public RecordingQuery(Exception exception)
        {
            _exception = exception;
        }

        public string? BranchId { get; private set; }

        public string? RequestId { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BranchCommandReceipt?> FindAsync(
            string branchId,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            RequestId = requestId;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_receipt)
                : Task.FromException<BranchCommandReceipt?>(_exception);
        }
    }
}
