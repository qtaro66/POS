using Pos.Domain.TableSessions;

namespace Pos.Domain.Tests.TableSessions;

public sealed class TableSessionTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 13, 8, 30, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Rehydrate_restores_persisted_state_without_creating_a_transition()
    {
        var session = TableSession.Rehydrate(
            TableSessionId.From("TS-1"),
            TableId.From("A04"),
            TableSessionStatus.PaymentPending,
            version: 7);

        Assert.Equal(TableSessionStatus.PaymentPending, session.Status);
        Assert.Equal(7, session.Version);
        Assert.Empty(session.Transitions);
    }

    [Fact]
    public void Rehydrate_rejects_non_positive_version()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TableSession.Rehydrate(
            TableSessionId.From("TS-1"),
            TableId.From("A04"),
            TableSessionStatus.Ordering,
            version: 0));
    }

    [Fact]
    public void Open_starts_ordering_and_records_auditable_transition()
    {
        var session = OpenSession();

        Assert.Equal(TableSessionStatus.Ordering, session.Status);
        Assert.True(session.CanAcceptOrders);
        Assert.Equal(1, session.Version);
        var transition = Assert.Single(session.Transitions);
        Assert.Equal(TableSessionAction.OpenTable, transition.Action);
        Assert.Null(transition.PreviousStatus);
        Assert.Equal("EMP023", transition.EmployeeId);
        Assert.Equal("POS01", transition.DeviceId);
        Assert.Equal(OccurredAt, transition.OccurredAt);
    }

    [Fact]
    public void Request_payment_locks_ordering_until_pc_cancels_request()
    {
        var session = OpenSession();

        session.RequestPayment(false, Context());

        Assert.Equal(TableSessionStatus.PaymentPending, session.Status);
        Assert.False(session.CanAcceptOrders);
        Assert.Throws<InvalidTableSessionTransitionException>(session.EnsureCanSubmitOrder);

        session.CancelPaymentRequest(Context());

        Assert.Equal(TableSessionStatus.Ordering, session.Status);
        Assert.True(session.CanAcceptOrders);
        session.EnsureCanSubmitOrder();
    }

    [Fact]
    public void Request_payment_rejects_open_cancellation_request_without_changing_state()
    {
        var session = OpenSession();

        var exception = Assert.Throws<TableSessionRuleViolationException>(
            () => session.RequestPayment(true, Context()));

        Assert.Equal("OpenCancellationRequest", exception.Rule);
        Assert.Equal(TableSessionStatus.Ordering, session.Status);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Full_payment_path_completes_session()
    {
        var session = OpenSession();
        session.RequestPayment(false, Context());
        session.StartPayment(Context());

        session.CompletePayment(true, Context());

        Assert.Equal(TableSessionStatus.Completed, session.Status);
        Assert.False(session.CanAcceptOrders);
        Assert.Throws<InvalidTableSessionTransitionException>(
            () => session.CancelPaymentRequest(Context()));
    }

    [Fact]
    public void Split_payment_path_requires_full_application_before_completion()
    {
        var session = OpenSession();
        session.RequestPayment(false, Context());
        session.StartPayment(Context());
        session.ConfirmPartialPayment(Context());

        var exception = Assert.Throws<TableSessionRuleViolationException>(
            () => session.CompletePayment(false, Context()));

        Assert.Equal("PaymentMustBeFullyApplied", exception.Rule);
        Assert.Equal(TableSessionStatus.PartiallyPaid, session.Status);

        session.CompletePayment(true, Context());
        Assert.Equal(TableSessionStatus.Completed, session.Status);
    }

    [Fact]
    public void Partial_payment_returns_to_pending_only_after_all_lines_are_reversed()
    {
        var session = OpenSession();
        session.RequestPayment(false, Context());
        session.StartPayment(Context());
        session.ConfirmPartialPayment(Context());

        Assert.Throws<TableSessionRuleViolationException>(
            () => session.ReverseAllConfirmedPaymentLines(false, Context()));
        Assert.Equal(TableSessionStatus.PartiallyPaid, session.Status);

        session.ReverseAllConfirmedPaymentLines(true, Context());
        Assert.Equal(TableSessionStatus.PaymentPending, session.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_or_cancelled_payment_returns_to_pending(bool paymentFailed)
    {
        var session = OpenSession();
        session.RequestPayment(false, Context());
        session.StartPayment(Context());

        if (paymentFailed)
        {
            session.PaymentFailed(Context());
        }
        else
        {
            session.CancelPayment(Context());
        }

        Assert.Equal(TableSessionStatus.PaymentPending, session.Status);
    }

    [Theory]
    [InlineData(TableSessionStatus.Ordering)]
    [InlineData(TableSessionStatus.PaymentPending)]
    public void Cancel_bill_is_allowed_only_before_payment_starts(TableSessionStatus status)
    {
        var session = OpenSession();
        if (status == TableSessionStatus.PaymentPending)
        {
            session.RequestPayment(false, Context());
        }

        session.CancelBill(Context("customer cancelled"));

        Assert.Equal(TableSessionStatus.Cancelled, session.Status);
        Assert.Equal("customer cancelled", session.Transitions[^1].Reason);
    }

    [Fact]
    public void Change_table_keeps_session_identity_and_records_both_tables()
    {
        var session = OpenSession();
        var originalSessionId = session.Id;

        session.ChangeTable(TableId.From("B12"), true, Context("customer moved"));

        Assert.Same(originalSessionId, session.Id);
        Assert.Equal("B12", session.CurrentTableId.Value);
        Assert.Equal(TableSessionStatus.Ordering, session.Status);
        var transition = session.Transitions[^1];
        Assert.Equal("A04", transition.PreviousTableId.Value);
        Assert.Equal("B12", transition.CurrentTableId.Value);
    }

    [Fact]
    public void Change_table_rejects_unavailable_destination()
    {
        var session = OpenSession();

        var exception = Assert.Throws<TableSessionRuleViolationException>(
            () => session.ChangeTable(TableId.From("B12"), false, Context()));

        Assert.Equal("DestinationTableMustBeAvailable", exception.Rule);
        Assert.Equal("A04", session.CurrentTableId.Value);
        Assert.Equal(1, session.Version);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Close_table_rejects_remaining_items_or_balance(
        bool hasRemainingItems,
        bool hasOutstandingBalance)
    {
        var session = OpenSession();

        Assert.Throws<TableSessionRuleViolationException>(
            () => session.CloseTable(hasRemainingItems, hasOutstandingBalance, Context()));
        Assert.Equal(TableSessionStatus.Ordering, session.Status);
    }

    [Fact]
    public void Close_empty_settled_table_marks_session_closed()
    {
        var session = OpenSession();

        session.CloseTable(false, false, Context());

        Assert.Equal(TableSessionStatus.Closed, session.Status);
        Assert.False(session.CanAcceptOrders);
    }

    [Fact]
    public void Invalid_transition_reports_current_status_and_action()
    {
        var session = OpenSession();

        var exception = Assert.Throws<InvalidTableSessionTransitionException>(
            () => session.StartPayment(Context()));

        Assert.Equal(TableSessionStatus.Ordering, exception.CurrentStatus);
        Assert.Equal(TableSessionAction.StartPayment, exception.AttemptedAction);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Missing_transition_context_does_not_mutate_session()
    {
        var session = OpenSession();

        Assert.Throws<ArgumentNullException>(() => session.RequestPayment(false, null!));

        Assert.Equal(TableSessionStatus.Ordering, session.Status);
        Assert.Equal(1, session.Version);
    }

    private static TableSession OpenSession()
    {
        return TableSession.Open(
            TableSessionId.From("TS-0001"),
            TableId.From("A04"),
            Context());
    }

    private static TableSessionTransitionContext Context(string? reason = null)
    {
        return new TableSessionTransitionContext("EMP023", "POS01", OccurredAt, reason);
    }
}
