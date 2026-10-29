using Pos.Application.Synchronization;

namespace Pos.Application.Tests;

public sealed class BranchCloudOutboxRetryDelayPolicyTests
{
    private readonly ExponentialBranchCloudOutboxRetryDelayPolicy _policy = new();

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    public void Delay_doubles_from_persisted_retry_count(
        int retryCount,
        int expectedSeconds)
    {
        var delay = _policy.Calculate(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(5),
            retryCount);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(30)]
    [InlineData(int.MaxValue)]
    public void Delay_is_capped_without_overflow(int retryCount)
    {
        var delay = _policy.Calculate(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(5),
            retryCount);

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
    }

    [Fact]
    public void Non_power_of_two_ceiling_is_respected()
    {
        var delay = _policy.Calculate(
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(15),
            retryCount: 2);

        Assert.Equal(TimeSpan.FromSeconds(15), delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Initial_delay_must_be_positive(int seconds)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _policy.Calculate(
                TimeSpan.FromSeconds(seconds),
                TimeSpan.FromSeconds(5),
                retryCount: 0));

        Assert.Equal("initialDelay", exception.ParamName);
    }

    [Fact]
    public void Maximum_delay_must_not_be_shorter_than_initial_delay()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _policy.Calculate(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(4),
                retryCount: 0));

        Assert.Equal("maximumDelay", exception.ParamName);
    }

    [Fact]
    public void Retry_count_cannot_be_negative()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _policy.Calculate(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(5),
                retryCount: -1));

        Assert.Equal("retryCount", exception.ParamName);
    }
}
