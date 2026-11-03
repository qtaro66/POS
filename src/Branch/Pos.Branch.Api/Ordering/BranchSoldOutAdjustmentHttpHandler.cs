using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public sealed record BranchSoldOutAdjustmentRequest(Guid OperationId, string? SellableItemId, bool? ExpectedSoldOut, bool? SoldOut);
public sealed record BranchSoldOutAdjustmentResponse(int SchemaVersion, string Outcome);

public static class BranchSoldOutAdjustmentHttpHandler
{
    public static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal, BranchSoldOutAdjustmentRequest request, BranchOrderingRuntime runtime, CancellationToken cancellationToken,
        [FromServices] BranchEmployeePermissionCache? permissions = null)
    {
        if (!AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var identity))
        {
            return TypedResults.Unauthorized();
        }

        if (!BranchSoldOutAuthorization.CanAdjust(principal, identity, permissions))
        {
            return TypedResults.StatusCode(403);
        }

        if (request.OperationId == Guid.Empty || request.SellableItemId is not { Length: > 0 and <= 128 } itemId
            || itemId != itemId.Trim() || itemId.Any(char.IsControl)
            || request.ExpectedSoldOut is not { } expected || request.SoldOut is not { } soldOut || expected == soldOut)
        {
            return Response(400, "InvalidAdjustment");
        }

        try
        {
            var result = await runtime.SoldOutAdjustments.ApplyAsync(new(
                request.OperationId.ToString("D"), identity.BranchId, itemId, identity.EmployeeId,
                identity.DeviceId, expected, soldOut, runtime.Time.GetUtcNow()), cancellationToken);
            return result switch
            {
                SoldOutAdjustmentResult.Applied => Response(200, "Applied"),
                SoldOutAdjustmentResult.Replayed => Response(200, "Replayed"),
                SoldOutAdjustmentResult.Conflict => Response(409, "Conflict"),
                SoldOutAdjustmentResult.ItemNotFound => Response(404, "ItemNotFound"),
                _ => Response(503, "Unavailable")
            };
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return Response(503, "Unavailable");
        }
    }

    private static IResult Response(int status, string outcome) =>
        TypedResults.Json(new BranchSoldOutAdjustmentResponse(1, outcome), statusCode: status);
}
