namespace Pos.Domain.TableSessions;

public enum TableSessionStatus
{
    Ordering,
    PaymentPending,
    PaymentInProgress,
    PartiallyPaid,
    Completed,
    Cancelled,
    Closed
}
