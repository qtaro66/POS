namespace Pos.Application.Synchronization;

public interface IBranchCloudOutboxRetryDelayPolicy
{
    /// <summary>
    /// Calculates the delay for the next attempt from the number of failures
    /// already persisted before the current delivery attempt.
    /// </summary>
    TimeSpan Calculate(
        TimeSpan initialDelay,
        TimeSpan maximumDelay,
        int retryCount);
}

public sealed class ExponentialBranchCloudOutboxRetryDelayPolicy :
    IBranchCloudOutboxRetryDelayPolicy
{
    public TimeSpan Calculate(
        TimeSpan initialDelay,
        TimeSpan maximumDelay,
        int retryCount)
    {
        if (initialDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay));
        }

        if (maximumDelay < initialDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelay),
                "The maximum retry delay must not be shorter than the initial delay.");
        }

        if (retryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount));
        }

        var delay = initialDelay;
        for (var attempt = 0; attempt < retryCount && delay < maximumDelay; attempt++)
        {
            if (delay.Ticks > maximumDelay.Ticks / 2)
            {
                return maximumDelay;
            }

            delay = TimeSpan.FromTicks(delay.Ticks * 2);
        }

        return delay > maximumDelay ? maximumDelay : delay;
    }
}
