using System.Security.Claims;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Tests;

public sealed partial class BranchOrderingHttpApiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mobile_cancellation_can_be_resolved_by_cashier_then_paid(bool confirm)
    {
        await using var fixture = await Fixture.CreateAsync(cancellationEnabled: true);
        ClaimsPrincipal WithRole(string role)
        {
            var principal = Principal();
            principal.AddIdentity(new ClaimsIdentity([new Claim(BranchPosClaimTypes.DeviceRole, role)],
                BranchEmployeeSessionAuthenticationDefaults.SchemeName));
            return principal;
        }

        var mobile = WithRole("Mobile");
        var cashier = WithRole("PcPos");
        Assert.Equal(200, (await fixture.Call("OpenBranchTable", Open("OPEN-1", "SESSION-1", "A01"))).Status);
        Assert.Equal(200, (await fixture.Call("SubmitBranchOrder", Submit("ORDER-1"))).Status);
        var lineId = await fixture.Scalar("SELECT order_line_id FROM order_lines ORDER BY line_sequence LIMIT 1;");
        var request = new
        {
            schemaVersion = 1,
            context = Context("CANCEL-1", "SESSION-1"),
            reason = "Customer changed their mind",
            lines = new[] { new { orderLineId = lineId, quantity = 1 } }
        };
        Assert.Equal(403, (await fixture.Call("RequestBranchCancellation", request, principal: cashier)).Status);
        Assert.Equal(200, (await fixture.Call("RequestBranchCancellation", request, principal: mobile)).Status);
        Assert.Equal(200, (await fixture.Call("RequestBranchCancellation", request, principal: mobile)).Status);
        Assert.Equal("1", await fixture.Scalar("SELECT COUNT(*) FROM cancellation_request_lines;"));
        Assert.Equal(200, (await fixture.Call("GetOrderingCommandStatus", routeValue: "CANCEL-1", principal: mobile)).Status);
        var queue = await fixture.Call("ListBranchCancellationQueue", principal: cashier);
        Assert.Equal("Customer changed their mind", queue.Body.GetProperty("requests")[0].GetProperty("reason").GetString());

        async Task Payment(string requestId, int expectedStatus)
        {
            var detail = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1", principal: mobile);
            var version = detail.Body.GetProperty("session").GetProperty("tableVersion").GetInt64();
            var payment = await fixture.Call("RequestBranchTablePayment",
                new { schemaVersion = 1, context = Context(requestId, "SESSION-1"), expectedVersion = version }, principal: mobile);
            Assert.Equal(expectedStatus, payment.Status);
            Assert.Equal(expectedStatus == 200 ? "PaymentPending" : "Ordering",
                payment.Body.GetProperty("result").GetProperty("tableSessionStatus").GetString());
        }

        await Payment("PAY-BLOCKED", 409);
        var inspection = await fixture.Call("QueueBranchCancellationInspection", routeValue: "CANCEL-1", principal: cashier);
        Assert.Equal(200, inspection.Status);
        var quantities = new
        {
            schemaVersion = 1,
            lines = new[] { new { orderLineId = lineId, notStarted = 1, inProgress = 0, completed = 0, notApplicable = 0 } }
        };
        Assert.Equal(200, (await fixture.Call("RecordBranchCancellationProduction", quantities, "CANCEL-1", cashier)).Status);
        var resolution = confirm
            ? await fixture.Call("ConfirmBranchCancellation", quantities, "CANCEL-1", cashier)
            : await fixture.Call("RejectBranchCancellation", routeValue: "CANCEL-1", principal: cashier);
        Assert.Equal(200, resolution.Status);
        var latest = await fixture.Call("GetBranchOrderingSessionDetail", routeValue: "SESSION-1", principal: mobile);
        Assert.Equal(confirm ? "Confirmed" : "Rejected", latest.Body.GetProperty("cancellationRequests")[0].GetProperty("status").GetString());
        Assert.Equal(confirm ? 110m : 220m, latest.Body.GetProperty("billPreviewTotals").GetProperty("netTotal").GetDecimal());
        await Payment("PAY-AFTER", 200);
    }
}
