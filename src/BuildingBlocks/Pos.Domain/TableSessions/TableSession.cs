namespace Pos.Domain.TableSessions;

public sealed class TableSession
{
    private readonly List<TableSessionTransitioned> _transitions = [];

    private TableSession(TableSessionId id, TableId currentTableId)
    {
        Id = id;
        CurrentTableId = currentTableId;
        Status = TableSessionStatus.Ordering;
    }

    public TableSessionId Id { get; }

    public TableId CurrentTableId { get; private set; }

    public TableSessionStatus Status { get; private set; }

    public long Version { get; private set; }

    public bool CanAcceptOrders => Status == TableSessionStatus.Ordering;

    public IReadOnlyList<TableSessionTransitioned> Transitions => _transitions.AsReadOnly();

    public static TableSession Open(
        TableSessionId id,
        TableId tableId,
        TableSessionTransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(tableId);
        ArgumentNullException.ThrowIfNull(context);

        var session = new TableSession(id, tableId);
        session.RecordTransition(
            TableSessionAction.OpenTable,
            previousStatus: null,
            previousTableId: tableId,
            context);
        return session;
    }

    public static TableSession Rehydrate(
        TableSessionId id,
        TableId currentTableId,
        TableSessionStatus status,
        long version)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(currentTableId);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A persisted session version must be positive.");
        }

        return new TableSession(id, currentTableId)
        {
            Status = status,
            Version = version
        };
    }

    public void EnsureCanSubmitOrder()
    {
        EnsureStatus(TableSessionAction.SubmitOrder, TableSessionStatus.Ordering);
    }

    public void ChangeTable(
        TableId destinationTableId,
        bool destinationIsAvailable,
        TableSessionTransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(destinationTableId);
        ArgumentNullException.ThrowIfNull(context);
        EnsureStatus(TableSessionAction.ChangeTable, TableSessionStatus.Ordering);

        if (!destinationIsAvailable)
        {
            throw new TableSessionRuleViolationException(
                "DestinationTableMustBeAvailable",
                "ChangeTable requires an available destination table.");
        }

        if (destinationTableId == CurrentTableId)
        {
            throw new TableSessionRuleViolationException(
                "DestinationTableMustDiffer",
                "The destination table must differ from the current table.");
        }

        var previousTableId = CurrentTableId;
        CurrentTableId = destinationTableId;
        RecordTransition(TableSessionAction.ChangeTable, Status, previousTableId, context);
    }

    public void RequestPayment(
        bool hasOpenCancellationRequest,
        TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.RequestPayment, TableSessionStatus.Ordering);

        if (hasOpenCancellationRequest)
        {
            throw new TableSessionRuleViolationException(
                "OpenCancellationRequest",
                "RequestPayment is not allowed while a cancellation request is open.");
        }

        TransitionTo(TableSessionStatus.PaymentPending, TableSessionAction.RequestPayment, context);
    }

    public void CancelPaymentRequest(TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.CancelPaymentRequest, TableSessionStatus.PaymentPending);
        TransitionTo(TableSessionStatus.Ordering, TableSessionAction.CancelPaymentRequest, context);
    }

    public void StartPayment(TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.StartPayment, TableSessionStatus.PaymentPending);
        TransitionTo(TableSessionStatus.PaymentInProgress, TableSessionAction.StartPayment, context);
    }

    public void PaymentFailed(TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.PaymentFailed, TableSessionStatus.PaymentInProgress);
        TransitionTo(TableSessionStatus.PaymentPending, TableSessionAction.PaymentFailed, context);
    }

    public void CancelPayment(TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.CancelPayment, TableSessionStatus.PaymentInProgress);
        TransitionTo(TableSessionStatus.PaymentPending, TableSessionAction.CancelPayment, context);
    }

    public void ConfirmPartialPayment(TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.ConfirmPartialPayment, TableSessionStatus.PaymentInProgress);
        TransitionTo(TableSessionStatus.PartiallyPaid, TableSessionAction.ConfirmPartialPayment, context);
    }

    public void CompletePayment(bool fullyApplied, TableSessionTransitionContext context)
    {
        EnsureStatus(
            TableSessionAction.CompletePayment,
            TableSessionStatus.PaymentInProgress,
            TableSessionStatus.PartiallyPaid);

        if (!fullyApplied)
        {
            throw new TableSessionRuleViolationException(
                "PaymentMustBeFullyApplied",
                "CompletePayment requires the applied payment total to cover the amount due.");
        }

        TransitionTo(TableSessionStatus.Completed, TableSessionAction.CompletePayment, context);
    }

    public void ReverseAllConfirmedPaymentLines(
        bool allConfirmedPaymentLinesReversed,
        TableSessionTransitionContext context)
    {
        EnsureStatus(
            TableSessionAction.ReverseAllConfirmedPaymentLines,
            TableSessionStatus.PartiallyPaid);

        if (!allConfirmedPaymentLinesReversed)
        {
            throw new TableSessionRuleViolationException(
                "AllConfirmedPaymentLinesMustBeReversed",
                "The session can return to PaymentPending only after every confirmed payment line is reversed.");
        }

        TransitionTo(
            TableSessionStatus.PaymentPending,
            TableSessionAction.ReverseAllConfirmedPaymentLines,
            context);
    }

    public void CancelBill(TableSessionTransitionContext context)
    {
        EnsureStatus(
            TableSessionAction.CancelBill,
            TableSessionStatus.Ordering,
            TableSessionStatus.PaymentPending);
        TransitionTo(TableSessionStatus.Cancelled, TableSessionAction.CancelBill, context);
    }

    public void CloseTable(
        bool hasRemainingItems,
        bool hasOutstandingBalance,
        TableSessionTransitionContext context)
    {
        EnsureStatus(TableSessionAction.CloseTable, TableSessionStatus.Ordering);

        if (hasRemainingItems || hasOutstandingBalance)
        {
            throw new TableSessionRuleViolationException(
                "TableMustBeEmptyAndSettled",
                "CloseTable requires no remaining items and no outstanding balance.");
        }

        TransitionTo(TableSessionStatus.Closed, TableSessionAction.CloseTable, context);
    }

    private void TransitionTo(
        TableSessionStatus nextStatus,
        TableSessionAction action,
        TableSessionTransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previousStatus = Status;
        Status = nextStatus;
        RecordTransition(action, previousStatus, CurrentTableId, context);
    }

    private void RecordTransition(
        TableSessionAction action,
        TableSessionStatus? previousStatus,
        TableId previousTableId,
        TableSessionTransitionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Version++;
        _transitions.Add(new TableSessionTransitioned(
            Id,
            action,
            previousStatus,
            Status,
            previousTableId,
            CurrentTableId,
            Version,
            context.EmployeeId,
            context.DeviceId,
            context.OccurredAt,
            context.Reason));
    }

    private void EnsureStatus(
        TableSessionAction action,
        params TableSessionStatus[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(Status))
        {
            throw new InvalidTableSessionTransitionException(Status, action);
        }
    }
}
