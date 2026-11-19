using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Api.Ordering;

public static class StoreLifecycleEndpoints
{
    public sealed record Request(string RequestId, string StoreSessionId, string ShiftId, string Operation, decimal? CashAmount, string Pin);

    public static async Task<IResult> CurrentAsync(ClaimsPrincipal principal, BranchOrderingRuntime runtime,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        try
        {
            var result = await runtime.TableCommands.ReadCurrentStoreLifecycleAsync(identity!.BranchId, token);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> StatusAsync(ClaimsPrincipal principal, string storeSessionId,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Guid.TryParseExact(storeSessionId, "D", out _)) { return TypedResults.BadRequest(); }
        try
        {
            var result = await runtime.TableCommands.ReadStoreLifecycleAsync(identity!.BranchId, storeSessionId, token);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> HandleAsync(ClaimsPrincipal principal, Request request, BranchOrderingRuntime runtime,
        [FromServices] BranchEmployeePinAuthenticationService pins, HttpContext context, CancellationToken token,
        [FromServices] BranchEmployeePermissionCache? permissions = null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        if (!Guid.TryParseExact(request.RequestId, "D", out _) ||
            !Guid.TryParseExact(request.StoreSessionId, "D", out _) ||
            !Guid.TryParseExact(request.ShiftId, "D", out _) ||
            !Enum.TryParse<StoreLifecycleAction>(request.Operation, out var operation) ||
            !Enum.IsDefined(operation) || request.Operation != operation.ToString() ||
            request.CashAmount is < 0 || (request.CashAmount is decimal cash && decimal.Round(cash, 2) != cash) ||
            (operation is StoreLifecycleAction.OpenStore or StoreLifecycleAction.CloseStore && request.CashAmount is null) ||
            string.IsNullOrEmpty(request.Pin) || request.Pin.Length > 128)
        {
            return TypedResults.BadRequest();
        }

        var employee = await pins.AuthenticateAsync(identity!.BranchId, request.Pin.AsMemory(), token);
        if (employee is null) { return TypedResults.Unauthorized(); }
        if (operation == StoreLifecycleAction.OpenStore &&
            permissions?.CanOpenStore(identity.BranchId, employee.EmployeeId) != true)
        {
            return TypedResults.StatusCode(403);
        }

        var now = runtime.Time.GetUtcNow();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        var command = new StoreLifecycleCommand(request.RequestId, request.StoreSessionId, request.ShiftId, operation,
            request.CashAmount, new StoreOperationContext(employee.EmployeeId, identity.DeviceId, now, localDate));
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.ExecuteStoreLifecycleAsync(identity.BranchId, command, token));
        }
        catch (OverflowException) { return TypedResults.BadRequest(); }
        catch (ArgumentException) { return TypedResults.BadRequest(); }
        catch (InvalidOperationException) { return TypedResults.Conflict(); }
        catch (SqliteException error) when (error.SqliteErrorCode == 19) { return TypedResults.Conflict(); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    private static bool TryIdentity(ClaimsPrincipal principal, out AuthenticatedBranchRequestIdentity? identity) =>
        AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out identity) &&
        AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) && role == "PcPos";
}
