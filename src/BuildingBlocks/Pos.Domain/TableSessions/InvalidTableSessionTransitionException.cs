namespace Pos.Domain.TableSessions;

public sealed class InvalidTableSessionTransitionException : InvalidOperationException
{
    public InvalidTableSessionTransitionException(
        TableSessionStatus currentStatus,
        TableSessionAction attemptedAction)
        : base($"Cannot perform {attemptedAction} while the table session is {currentStatus}.")
    {
        CurrentStatus = currentStatus;
        AttemptedAction = attemptedAction;
    }

    public TableSessionStatus CurrentStatus { get; }

    public TableSessionAction AttemptedAction { get; }
}
