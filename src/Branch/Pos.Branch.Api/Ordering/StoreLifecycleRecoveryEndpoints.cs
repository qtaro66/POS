using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Domain.StoreSessions;

namespace Pos.Branch.Api.Ordering;

public static partial class StoreLifecycleEndpoints
{
    public static async Task<IResult> RequestStatusAsync(ClaimsPrincipal principal, string requestId,
        string storeSessionId, string shiftId, string operation, string? cashAmount,
        BranchOrderingRuntime runtime, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        decimal? amount = null;
        if (cashAmount is not null)
        {
            if (!decimal.TryParse(cashAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return TypedResults.BadRequest();
            }
            amount = parsed;
        }
        var target = RecoveryTarget(requestId, storeSessionId, shiftId, operation, amount);
        if (target is null) { return TypedResults.BadRequest(); }
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.ReadStoreLifecycleRequestAsync(
                identity!.BranchId, identity.DeviceId, target, token));
        }
        catch (InvalidOperationException) { return TypedResults.Conflict(new { error = "StoreLifecycleConflict" }); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    public static async Task<IResult> RetireRequestAsync(ClaimsPrincipal principal, Request request,
        BranchOrderingRuntime runtime, [FromServices] BranchEmployeePinAuthenticationService pins,
        HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryIdentity(principal, out var identity)) { return TypedResults.StatusCode(403); }
        var target = RecoveryTarget(request.RequestId, request.StoreSessionId, request.ShiftId,
            request.Operation, request.CashAmount);
        if (target is null || string.IsNullOrEmpty(request.Pin) || request.Pin.Length > 128)
        {
            return TypedResults.BadRequest();
        }
        var employee = await pins.AuthenticateAsync(identity!.BranchId, request.Pin.AsMemory(), token);
        if (employee is null) { return TypedResults.Unauthorized(); }
        var now = runtime.Time.GetUtcNow();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Bangkok").DateTime);
        try
        {
            return TypedResults.Ok(await runtime.TableCommands.RetireStoreLifecycleRequestAsync(
                identity.BranchId, identity.DeviceId, target,
                new StoreOperationContext(employee.EmployeeId, identity.DeviceId, now, localDate), token));
        }
        catch (ArgumentException) { return TypedResults.BadRequest(); }
        catch (InvalidOperationException) { return TypedResults.Conflict(new { error = "StoreLifecycleConflict" }); }
        catch (SqliteException error) when (error.SqliteErrorCode == 19) { return TypedResults.Conflict(new { error = "StoreLifecycleConflict" }); }
        catch (SqliteException) { return TypedResults.StatusCode(503); }
    }

    private static StoreLifecycleRequestTarget? RecoveryTarget(string requestId, string storeSessionId,
        string shiftId, string operation, decimal? cashAmount)
    {
        if (!Guid.TryParseExact(requestId, "D", out _) || !Guid.TryParseExact(storeSessionId, "D", out _) ||
            !Guid.TryParseExact(shiftId, "D", out _) || !Enum.TryParse<StoreLifecycleAction>(operation, out var action) ||
            !Enum.IsDefined(action) || operation != action.ToString() || cashAmount < 0 ||
            (cashAmount is decimal amount && decimal.Round(amount, 2) != amount) ||
            (action is StoreLifecycleAction.OpenStore or StoreLifecycleAction.CloseStore && cashAmount is null))
        {
            return null;
        }
        return new(requestId, storeSessionId, shiftId, action, cashAmount);
    }
}
