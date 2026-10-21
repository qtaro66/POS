namespace Pos.Application.Security;

public sealed class FixedBranchEmployeeSessionLifetimePolicy :
    IBranchEmployeeSessionLifetimePolicy
{
    private readonly TimeSpan _lifetime;

    public FixedBranchEmployeeSessionLifetimePolicy(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "The employee session lifetime must be positive.");
        }

        _lifetime = lifetime;
    }

    public DateTimeOffset GetExpiresAt(DateTimeOffset issuedAt)
    {
        if (issuedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "The issuance timestamp must be UTC.",
                nameof(issuedAt));
        }

        try
        {
            return issuedAt.Add(_lifetime);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException(
                "The configured employee session lifetime exceeds the supported timestamp range.",
                exception);
        }
    }
}
