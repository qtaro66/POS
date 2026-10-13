namespace Pos.Application.Time;

public interface IBranchClock
{
    DateTimeOffset UtcNow { get; }
}
