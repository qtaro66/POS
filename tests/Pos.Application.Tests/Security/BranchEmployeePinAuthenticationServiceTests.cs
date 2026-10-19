using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class BranchEmployeePinAuthenticationServiceTests
{
    [Fact]
    public async Task Active_employee_returns_verified_identity_and_clears_working_pin()
    {
        var validator = new RecordingValidator(
            new VerifiedBranchEmployee("BRANCH-01", "EMPLOYEE-01"));
        var service = new BranchEmployeePinAuthenticationService(validator);
        var presentedPin = " 0123 ".ToCharArray();

        var result = await service.AuthenticateAsync(
            " BRANCH-01 ",
            presentedPin,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("BRANCH-01", result.BranchId);
        Assert.Equal("EMPLOYEE-01", result.EmployeeId);
        Assert.Equal("BRANCH-01", validator.BranchId);
        Assert.NotNull(validator.CapturedPin);
        Assert.All(validator.CapturedPin.Value.ToArray(), value => Assert.Equal('\0', value));
        Assert.Equal(" 0123 ", presentedPin);
    }

    [Fact]
    public async Task Invalid_inactive_or_ambiguous_pin_returns_one_unauthenticated_result()
    {
        var validator = new RecordingValidator((VerifiedBranchEmployee?)null);
        var service = new BranchEmployeePinAuthenticationService(validator);

        var result = await service.AuthenticateAsync(
            "BRANCH-01",
            "0123".AsMemory(),
            CancellationToken.None);

        Assert.Null(result);
        Assert.NotNull(validator.CapturedPin);
        Assert.All(validator.CapturedPin.Value.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task Validator_failure_clears_working_pin_and_propagates()
    {
        var validator = new RecordingValidator(
            new InvalidOperationException("Synthetic validation failure."));
        var service = new BranchEmployeePinAuthenticationService(validator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                "0123".AsMemory(),
                CancellationToken.None));

        Assert.Equal("Synthetic validation failure.", exception.Message);
        Assert.NotNull(validator.CapturedPin);
        Assert.All(validator.CapturedPin.Value.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task Cross_branch_validator_result_is_rejected_and_working_pin_is_cleared()
    {
        var validator = new RecordingValidator(
            new VerifiedBranchEmployee("BRANCH-02", "EMPLOYEE-01"));
        var service = new BranchEmployeePinAuthenticationService(validator);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                "0123".AsMemory(),
                CancellationToken.None));

        Assert.Equal(
            "The verified employee belongs to a different Branch.",
            exception.Message);
        Assert.NotNull(validator.CapturedPin);
        Assert.All(validator.CapturedPin.Value.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded()
    {
        using var source = new CancellationTokenSource();
        var validator = new RecordingValidator((VerifiedBranchEmployee?)null);
        var service = new BranchEmployeePinAuthenticationService(validator);

        await service.AuthenticateAsync(
            "BRANCH-01",
            "0123".AsMemory(),
            source.Token);

        Assert.Equal(source.Token, validator.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_branch_id_is_rejected_before_validator(string branchId)
    {
        var validator = new RecordingValidator((VerifiedBranchEmployee?)null);
        var service = new BranchEmployeePinAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                branchId,
                "0123".AsMemory(),
                CancellationToken.None));

        Assert.Null(validator.BranchId);
    }

    [Fact]
    public async Task Empty_pin_is_rejected_before_validator()
    {
        var validator = new RecordingValidator((VerifiedBranchEmployee?)null);
        var service = new BranchEmployeePinAuthenticationService(validator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AuthenticateAsync(
                "BRANCH-01",
                ReadOnlyMemory<char>.Empty,
                CancellationToken.None));

        Assert.Null(validator.BranchId);
    }

    [Theory]
    [InlineData("", "EMPLOYEE-01")]
    [InlineData("BRANCH-01", " ")]
    public void Verified_employee_requires_both_identifiers(
        string branchId,
        string employeeId)
    {
        Assert.Throws<ArgumentException>(() =>
            new VerifiedBranchEmployee(branchId, employeeId));
    }

    private sealed class RecordingValidator : IBranchEmployeePinValidator
    {
        private readonly VerifiedBranchEmployee? _result;
        private readonly Exception? _exception;

        public RecordingValidator(VerifiedBranchEmployee? result)
        {
            _result = result;
        }

        public RecordingValidator(Exception exception)
        {
            _exception = exception;
        }

        public string? BranchId { get; private set; }

        public ReadOnlyMemory<char>? CapturedPin { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            BranchId = branchId;
            CapturedPin = pin;
            CancellationToken = cancellationToken;
            return _exception is null
                ? Task.FromResult(_result)
                : Task.FromException<VerifiedBranchEmployee?>(_exception);
        }
    }
}
