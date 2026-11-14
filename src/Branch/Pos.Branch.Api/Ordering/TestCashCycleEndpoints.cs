using System.Security.Claims;
using Pos.Application.Security;
using Pos.Branch.Api.Security;

namespace Pos.Branch.Api.Ordering;

public static class TestCashCycleEndpoints
{
    public sealed record SimulationRequest(bool Fail);

    public static async Task<IResult> SimulateReceiptAsync(ClaimsPrincipal principal, string receiptNumber,
        SimulationRequest request, BranchOrderingRuntime runtime, CancellationToken token)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity) ||
            !AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        var result = await runtime.TableCommands.SimulateReceiptAsync(identity.BranchId, receiptNumber, request.Fail, token);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    public static async Task<IResult> ReceiptAsync(ClaimsPrincipal principal, string receiptNumber,
        BranchOrderingRuntime runtime, CancellationToken token)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity) ||
            !AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        var text = await runtime.TableCommands.RenderTestReceiptAsync(identity.BranchId, receiptNumber, token);
        return text is null ? TypedResults.NotFound() : TypedResults.Ok(new { Status = "Simulated", Text = text });
    }

    public sealed record Request(string Operation, string ShiftId, decimal Cash, string Pin);

    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, Request request,
        BranchOrderingRuntime runtime, [Microsoft.AspNetCore.Mvc.FromServices] BranchEmployeePinAuthenticationService pins, CancellationToken token)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity) ||
            !AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos")
        {
            return TypedResults.StatusCode(403);
        }

        if (request.Operation is not ("open" or "close") || !Guid.TryParseExact(request.ShiftId, "D", out _) ||
            request.Cash < 0 || decimal.Round(request.Cash, 2) != request.Cash || string.IsNullOrEmpty(request.Pin))
        {
            return TypedResults.BadRequest();
        }

        var employee = await pins.AuthenticateAsync(identity.BranchId, request.Pin.AsMemory(), token);
        if (employee?.EmployeeId != identity.EmployeeId) { return TypedResults.Unauthorized(); }
        var now = runtime.Time.GetUtcNow();
        if (request.Operation == "open")
        {
            var opened = await runtime.TableCommands.OpenTestCashCycleAsync(identity.BranchId,
                request.ShiftId, request.ShiftId, DateOnly.FromDateTime(now.UtcDateTime.AddHours(7)), request.Cash, token);
            return opened ? TypedResults.Ok(new { request.ShiftId }) : TypedResults.Conflict();
        }

        var closed = await runtime.TableCommands.CloseTestCashCycleAsync(identity.BranchId,
            request.ShiftId, request.Cash, token, employee.EmployeeId, now);
        return closed is null ? TypedResults.Conflict() : TypedResults.Ok(closed);
    }
}
