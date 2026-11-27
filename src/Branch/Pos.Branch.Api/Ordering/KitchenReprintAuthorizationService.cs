using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;

namespace Pos.Branch.Api.Ordering;

public enum KitchenReprintAuthorizationOutcome
{
    Authorized, Forbidden, InvalidTarget, InvalidPin, PinRejected, Unavailable
}

public sealed record KitchenReprintAuthorizationTarget(string RequestId, string CashRequestId,
    string SnapshotId, string OrderLineId, string OriginalPrintJobId);

public sealed record KitchenReprintOperatorContext(AuthenticatedBranchRequestIdentity Requester,
    string ApproverEmployeeId, KitchenReprintAuthorizationTarget Target);

public sealed record KitchenReprintAuthorizationDecision(KitchenReprintAuthorizationOutcome Outcome,
    KitchenReprintOperatorContext? Operator = null);

// A command-local check, not a grant/token or a print command. A future writer must validate the source and commit its own invariants.
public sealed class KitchenReprintAuthorizationService(
    BranchEmployeePinAuthenticationService pins, BranchEmployeePermissionCache? permissions)
{
    private readonly BranchEmployeePinAuthenticationService _pins = pins ?? throw new ArgumentNullException(nameof(pins));

    public async Task<KitchenReprintAuthorizationDecision> AuthorizeAsync(ClaimsPrincipal? principal,
        ReadOnlyMemory<char> pin, KitchenReprintAuthorizationTarget? target, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (principal is null || !AuthenticatedBranchRequestIdentityResolver.TryResolve(principal, out var requester) ||
            !AuthenticatedBranchRequestIdentityResolver.TryGetProvisionedDeviceRole(principal, out var role) || role != "PcPos" ||
            permissions is null || !permissions.IsForBranch(requester.BranchId))
        { return new(KitchenReprintAuthorizationOutcome.Forbidden); }
        if (target is null || !Identifier(target.RequestId, 256) || !Identifier(target.CashRequestId, 256) ||
            !Identifier(target.SnapshotId, 1024) || !Identifier(target.OrderLineId, 256) || !Identifier(target.OriginalPrintJobId, 1024))
        { return new(KitchenReprintAuthorizationOutcome.InvalidTarget); }
        if (pin.IsEmpty || pin.Length > 128) { return new(KitchenReprintAuthorizationOutcome.InvalidPin); }

        VerifiedBranchEmployee? approver;
        try { approver = await _pins.AuthenticateAsync(requester.BranchId, pin, token); }
        catch (Exception error) when (error is SqliteException or IOException or InvalidOperationException or
            CryptographicException or JsonException or FormatException)
        { return new(KitchenReprintAuthorizationOutcome.Unavailable); }
        token.ThrowIfCancellationRequested();
        if (approver is null) { return new(KitchenReprintAuthorizationOutcome.PinRejected); }
        // Read permission after PIN verification, so a revision applied while it was pending is respected.
        if (!permissions.CanReprintKitchenOrder(requester.BranchId, approver.EmployeeId))
        { return new(KitchenReprintAuthorizationOutcome.Forbidden); }
        return new(KitchenReprintAuthorizationOutcome.Authorized, new(requester, approver.EmployeeId, target));
    }

    private static bool Identifier(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && value == value.Trim() && !value.Any(char.IsControl);
}
