using Pos.Application.Synchronization;

namespace Pos.Application.Tests;

public sealed class BranchCloudEventPersistenceUnavailableExceptionTests
{
    [Fact]
    public void Preserves_cause_behind_stable_non_sensitive_message()
    {
        var cause = new IOException("Synthetic persistence failure.");

        var exception =
            new BranchCloudEventPersistenceUnavailableException(cause);

        Assert.Equal(
            "The Cloud event persistence service is unavailable.",
            exception.Message);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public void Missing_cause_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BranchCloudEventPersistenceUnavailableException(null!));
    }
}
