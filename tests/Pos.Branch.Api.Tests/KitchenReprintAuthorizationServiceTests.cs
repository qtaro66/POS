using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Contracts.Security;

namespace Pos.Branch.Api.Tests;

public sealed class KitchenReprintAuthorizationServiceTests
{
    private static readonly KitchenReprintAuthorizationTarget Target = new("COMMAND-1", "CASH-1", "branch:submit:production:1", "LINE-1", "branch:submit:production-print:1");
    private const string Pin = "432109";

    private static ClaimsPrincipal Principal(string? role = "PcPos", string branch = "B1", bool employee = true,
        string? scheme = BranchEmployeeSessionAuthenticationDefaults.SchemeName)
    {
        var identity = new ClaimsIdentity(scheme);
        identity.AddClaim(new(BranchPosClaimTypes.BranchId, branch));
        identity.AddClaim(new(BranchPosClaimTypes.DeviceId, "TERMINAL-1"));
        if (employee) { identity.AddClaim(new(BranchPosClaimTypes.EmployeeId, "REQUESTER")); }
        if (role is not null) { identity.AddClaim(new(BranchPosClaimTypes.DeviceRole, role)); }
        return new(identity);
    }

    public static IEnumerable<object?[]> DeniedPrincipals()
    {
        yield return [null];
        yield return [new ClaimsPrincipal()];
        yield return [Principal("Mobile")];
        yield return [Principal("Kitchen")];
        yield return [Principal("Other")];
        yield return [Principal(null)];
        yield return [Principal(employee: false)];
        yield return [Principal(scheme: null)];
        yield return [Principal(branch: "B2")];
        var unrelated = Principal(null);
        unrelated.AddIdentity(new ClaimsIdentity([new(BranchPosClaimTypes.DeviceRole, "PcPos")], "Unrelated"));
        yield return [unrelated];
        var duplicate = Principal();
        ((ClaimsIdentity)duplicate.Identity!).AddClaim(new(BranchPosClaimTypes.EmployeeId, "OTHER"));
        yield return [duplicate];
        var duplicateRole = Principal();
        ((ClaimsIdentity)duplicateRole.Identity!).AddClaim(new(BranchPosClaimTypes.DeviceRole, "PcPos"));
        yield return [duplicateRole];
    }

    [Theory]
    [MemberData(nameof(DeniedPrincipals))]
    public async Task Rejects_untrusted_or_wrong_branch_identity_before_reading_pin(ClaimsPrincipal? principal)
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var decision = await fixture.Service.AuthorizeAsync(principal, Pin.AsMemory(), Target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden, decision.Outcome);
        Assert.Null(decision.Operator); Assert.Equal(0, fixture.Validator.Calls);
    }

    [Fact]
    public async Task Missing_cache_fails_closed_before_pin_validation()
    {
        var validator = new Validator();
        var service = new KitchenReprintAuthorizationService(new(validator), null);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden,
            (await service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target)).Outcome);
        Assert.Equal(0, validator.Calls);
    }

    public static IEnumerable<object?[]> InvalidTargets()
    {
        yield return [null];
        yield return [Target with { RequestId = "" }];
        yield return [Target with { RequestId = " COMMAND-1" }];
        yield return [Target with { CashRequestId = "CASH-1 " }];
        yield return [Target with { OrderLineId = "bad\u0085line" }];
        yield return [Target with { SnapshotId = "bad\nsource" }];
        yield return [Target with { OriginalPrintJobId = "\tjob" }];
        yield return [Target with { RequestId = new string('x', 257) }];
        yield return [Target with { CashRequestId = new string('x', 257) }];
        yield return [Target with { OrderLineId = new string('x', 257) }];
        yield return [Target with { SnapshotId = new string('x', 1025) }];
        yield return [Target with { OriginalPrintJobId = new string('x', 1025) }];
    }

    [Theory]
    [MemberData(nameof(InvalidTargets))]
    public async Task Rejects_invalid_exact_target_before_pin_validation(KitchenReprintAuthorizationTarget? target)
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var decision = await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.InvalidTarget, decision.Outcome);
        Assert.Null(decision.Operator); Assert.Equal(0, fixture.Validator.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public async Task Rejects_invalid_pin_size_before_authentication(int length)
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var decision = await fixture.Service.AuthorizeAsync(Principal(), new string('1', length).AsMemory(), Target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.InvalidPin, decision.Outcome);
        Assert.Null(decision.Operator); Assert.Equal(0, fixture.Validator.Calls);
    }

    [Fact]
    public async Task Active_pin_approver_is_separate_from_requester_and_cannot_change_session_or_target()
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var principal = Principal(); var claims = principal.Claims.Select(claim => (claim.Type, claim.Value)).ToArray();
        var target = Target with { RequestId = new string('c', 256), SnapshotId = new string('s', 1024), OriginalPrintJobId = new string('j', 1024) };
        var decision = await fixture.Service.AuthorizeAsync(principal, Pin.AsMemory(), target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Authorized, decision.Outcome);
        var context = Assert.IsType<KitchenReprintOperatorContext>(decision.Operator);
        Assert.Equal("B1", context.Requester.BranchId); Assert.Equal("TERMINAL-1", context.Requester.DeviceId);
        Assert.Equal("REQUESTER", context.Requester.EmployeeId); Assert.Equal("APPROVER", context.ApproverEmployeeId);
        Assert.Same(target, context.Target); Assert.Equal(claims, principal.Claims.Select(claim => (claim.Type, claim.Value)).ToArray());
        Assert.Equal(1, fixture.Validator.Calls); Assert.Equal("B1", fixture.Validator.LastBranch);
        Assert.DoesNotContain(Pin, JsonSerializer.Serialize(decision));
        Assert.All(fixture.Validator.WorkingPin.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task No_cached_permission_or_unrelated_flags_can_grant_reprint()
    {
        using var fixture = new Fixture();
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden,
            (await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target)).Outcome);
        await fixture.Apply(false);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden,
            (await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target)).Outcome);
        Assert.Equal(2, fixture.Validator.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_or_inactive_pin_never_uses_a_cached_grant(bool inactive)
    {
        using var fixture = new Fixture(); await fixture.Apply(true, active: !inactive);
        if (!inactive) { fixture.Validator.Validate = (_, _, _) => Task.FromResult<VerifiedBranchEmployee?>(null); }
        var decision = await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target);
        Assert.Equal(inactive ? KitchenReprintAuthorizationOutcome.Forbidden : KitchenReprintAuthorizationOutcome.PinRejected, decision.Outcome);
        Assert.Null(decision.Operator); Assert.Equal(1, fixture.Validator.Calls);
        Assert.All(fixture.Validator.WorkingPin.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task Every_operation_validates_exact_pin_again_and_revocation_does_not_carry_to_new_target()
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var first = await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Authorized, first.Outcome);
        await fixture.Apply(false, revision: 2);
        var secondTarget = Target with { RequestId = "COMMAND-2", OrderLineId = "LINE-2" };
        var second = await fixture.Service.AuthorizeAsync(Principal(), " 432109 ".AsMemory(), secondTarget);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden, second.Outcome);
        Assert.Equal(2, fixture.Validator.Calls); Assert.Equal(" 432109 ", fixture.Validator.Presented);
        Assert.Null(second.Operator); Assert.Equal("COMMAND-1", first.Operator!.Target.RequestId);
    }

    [Fact]
    public async Task A_previous_success_does_not_substitute_for_a_later_rejected_pin()
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Authorized,
            (await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target)).Outcome);
        fixture.Validator.Validate = (_, _, _) => Task.FromResult<VerifiedBranchEmployee?>(null);
        var denied = await fixture.Service.AuthorizeAsync(Principal(), "wrong-pin".AsMemory(), Target with { RequestId = "COMMAND-2" });
        Assert.Equal(KitchenReprintAuthorizationOutcome.PinRejected, denied.Outcome);
        Assert.Null(denied.Operator); Assert.Equal(2, fixture.Validator.Calls);
    }

    [Fact]
    public async Task Revocation_while_pin_is_pending_is_observed_before_authorizing()
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Validator.Validate = async (_, _, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new("B1", "APPROVER"); };
        var pending = fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await fixture.Apply(false, revision: 2);
        release.SetResult(); var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(KitchenReprintAuthorizationOutcome.Forbidden, result.Outcome); Assert.Null(result.Operator);
    }

    [Fact]
    public async Task A_new_permission_revision_while_pin_is_pending_is_used_for_that_exact_target()
    {
        using var fixture = new Fixture(); await fixture.Apply(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Validator.Validate = async (_, _, token) => { entered.SetResult(); await release.Task.WaitAsync(token); return new("B1", "APPROVER"); };
        var pending = fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await fixture.Apply(true, revision: 2);
        release.SetResult(); var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(KitchenReprintAuthorizationOutcome.Authorized, result.Outcome);
        Assert.Same(Target, result.Operator!.Target);
    }

    [Theory]
    [InlineData("cross-branch")]
    [InlineData("io")]
    [InlineData("sqlite")]
    [InlineData("invalid-operation")]
    [InlineData("cryptographic")]
    [InlineData("json")]
    [InlineData("format")]
    public async Task Validator_failures_do_not_authorize_or_expose_pin(string fault)
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        fixture.Validator.Validate = (_, _, _) => fault switch
        {
            "cross-branch" => Task.FromResult<VerifiedBranchEmployee?>(new("B2", "APPROVER")),
            "io" => throw new IOException(Pin),
            "sqlite" => throw new SqliteException(Pin, 10),
            "cryptographic" => throw new System.Security.Cryptography.CryptographicException(Pin),
            "json" => throw new JsonException(Pin),
            "format" => throw new FormatException(Pin),
            _ => throw new InvalidOperationException(Pin)
        };
        var result = await fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target);
        Assert.Equal(KitchenReprintAuthorizationOutcome.Unavailable, result.Outcome); Assert.Null(result.Operator);
        Assert.DoesNotContain(Pin, JsonSerializer.Serialize(result));
        Assert.All(fixture.Validator.WorkingPin.ToArray(), value => Assert.Equal('\0', value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_propagates_without_returning_an_authorized_operator(bool pending)
    {
        using var fixture = new Fixture(); await fixture.Apply(true);
        using var stop = new CancellationTokenSource();
        if (pending)
        {
            fixture.Validator.Validate = (_, _, _) => { stop.Cancel(); return Task.FromResult<VerifiedBranchEmployee?>(new("B1", "APPROVER")); };
        }
        else { stop.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.AuthorizeAsync(Principal(), Pin.AsMemory(), Target, stop.Token));
        Assert.Equal(pending ? 1 : 0, fixture.Validator.Calls);
        if (pending) { Assert.All(fixture.Validator.WorkingPin.ToArray(), value => Assert.Equal('\0', value)); }
    }

    private sealed class Validator : IBranchEmployeePinValidator
    {
        public int Calls { get; private set; }
        public string? LastBranch { get; private set; }
        public string? Presented { get; private set; }
        public ReadOnlyMemory<char> WorkingPin { get; private set; }
        public Func<string, ReadOnlyMemory<char>, CancellationToken, Task<VerifiedBranchEmployee?>> Validate { get; set; } =
            (_, _, _) => Task.FromResult<VerifiedBranchEmployee?>(new("B1", "APPROVER"));
        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(string branchId, ReadOnlyMemory<char> pin, CancellationToken token = default)
        {
            Calls++; LastBranch = branchId; Presented = pin.ToString(); WorkingPin = pin;
            return Validate(branchId, pin, token);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"kitchen-reprint-auth-{Guid.NewGuid():N}.json");
        public Validator Validator { get; } = new();
        public BranchEmployeePermissionCache Cache { get; }
        public KitchenReprintAuthorizationService Service { get; }
        public Fixture() { Cache = new("B1", _path); Service = new(new(Validator), Cache); }
        public async Task Apply(bool reprint, bool active = true, long revision = 1)
        {
            var snapshot = new BranchEmployeePermissionSnapshot(1, "B1", revision,
                [new("APPROVER", active, true, true, reprint), new("REQUESTER", true, true, true, false)]);
            using var content = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.True(await Cache.ApplyAsync(content, CancellationToken.None));
        }
        public void Dispose() => File.Delete(_path);
    }
}
