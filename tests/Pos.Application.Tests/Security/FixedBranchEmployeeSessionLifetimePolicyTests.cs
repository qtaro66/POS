using Pos.Application.Security;

namespace Pos.Application.Tests.Security;

public sealed class FixedBranchEmployeeSessionLifetimePolicyTests
{
    private static readonly DateTimeOffset IssuedAt =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Returns_expiry_using_explicit_duration()
    {
        var policy = new FixedBranchEmployeeSessionLifetimePolicy(
            TimeSpan.FromHours(8));

        var expiresAt = policy.GetExpiresAt(IssuedAt);

        Assert.Equal(IssuedAt.AddHours(8), expiresAt);
        Assert.Equal(TimeSpan.Zero, expiresAt.Offset);
    }

    [Theory]
    [MemberData(nameof(InvalidDurations))]
    public void Rejects_non_positive_duration(TimeSpan duration)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FixedBranchEmployeeSessionLifetimePolicy(duration));

        Assert.Equal("lifetime", exception.ParamName);
    }

    [Fact]
    public void Rejects_non_utc_issuance_timestamp()
    {
        var policy = new FixedBranchEmployeeSessionLifetimePolicy(
            TimeSpan.FromHours(8));

        var exception = Assert.Throws<ArgumentException>(() =>
            policy.GetExpiresAt(IssuedAt.ToOffset(TimeSpan.FromHours(7))));

        Assert.Equal("issuedAt", exception.ParamName);
    }

    [Fact]
    public void Reports_timestamp_overflow_as_invalid_policy_configuration()
    {
        var policy = new FixedBranchEmployeeSessionLifetimePolicy(
            TimeSpan.FromTicks(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            policy.GetExpiresAt(DateTimeOffset.MaxValue));

        Assert.Equal(
            "The configured employee session lifetime exceeds the supported timestamp range.",
            exception.Message);
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    public static TheoryData<TimeSpan> InvalidDurations => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromTicks(-1)
    };
}
