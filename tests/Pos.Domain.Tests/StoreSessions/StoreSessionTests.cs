using Pos.Domain.StoreSessions;

namespace Pos.Domain.Tests.StoreSessions;

public sealed class StoreSessionTests
{
    private static StoreOperationContext Context(int day = 19) => new("employee", "terminal",
        new DateTimeOffset(2026, 11, day, 9, 0, 0, TimeSpan.FromHours(7)), new DateOnly(2026, 11, day));

    [Fact]
    public void Store_open_creates_first_shift_and_audit_without_separate_shift_open()
    {
        var session = StoreSession.OpenStore("store", "first", 0, Context());

        Assert.Equal("first", session.ActiveShiftId);
        Assert.Equal(0, session.OpeningCashAmount);
        Assert.Equal("OpenStore", Assert.Single(session.Operations).Action);
        Assert.Equal(TimeSpan.Zero, session.Operations[0].Context.OccurredAt.Offset);
        session.EnsureCanCompletePayment(Context());
    }

    [Fact]
    public void Second_shift_float_is_informational_and_store_closing_uses_cumulative_applied_cash()
    {
        var session = StoreSession.OpenStore("store", "first", 500, Context());
        var first = session.CloseShift(160, null, false, Context());
        Assert.Equal(660, first.ExpectedCashAmount);
        Assert.Null(first.ActualCashAmount);
        Assert.Null(first.CashDifference);
        Assert.Equal("NotReconciled", first.ReconciliationStatus);
        Assert.False(session.IsClosed);
        Assert.Throws<InvalidOperationException>(() => session.EnsureCanCompletePayment(Context()));

        session.OpenShift("second", 999, Context());
        var last = session.CloseStore(360, 850, false, Context());

        Assert.Equal(860, last.ExpectedCashAmount);
        Assert.Equal(-10, last.CashDifference);
        Assert.True(last.ClosesStore);
        Assert.True(session.IsClosed);
        Assert.Null(session.ActiveShiftId);
        Assert.Equal(660, first.ExpectedCashAmount);
        Assert.Null(first.ActualCashAmount);
        Assert.Equal(new[] { "OpenStore", "CloseShift", "OpenShift", "CloseStore" }, session.Operations.Select(x => x.Action));
    }

    [Fact]
    public void Optional_shift_float_stays_null_and_zero_actual_is_reconciled()
    {
        var session = StoreSession.OpenStore("store", "first", 0, Context());
        session.CloseShift(0, null, false, Context());
        session.OpenShift("second", null, Context());
        Assert.Null(session.Operations[2].CashAmount);

        var closing = session.CloseStore(0, 0, false, Context());
        Assert.Equal(0, closing.CashDifference);
        Assert.Equal("Reconciled", closing.ReconciliationStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partially_paid_tables_prevent_close_without_mutating_the_session(bool closeStore)
    {
        var session = StoreSession.OpenStore("store", "first", 500, Context());
        Assert.Throws<InvalidOperationException>(() => closeStore
            ? session.CloseStore(100, 600, true, Context())
            : session.CloseShift(100, null, true, Context()));

        Assert.Equal("first", session.ActiveShiftId);
        Assert.False(session.IsClosed);
        Assert.Empty(session.Closings);
        Assert.Single(session.Operations);
    }

    [Fact]
    public void Reopening_and_recounting_are_rejected_and_closing_snapshot_is_preserved()
    {
        var session = StoreSession.OpenStore("store", "first", 500, Context());
        Assert.Throws<InvalidOperationException>(() => session.OpenShift("other", null, Context()));
        var closing = session.CloseShift(100, 610, false, Context());
        Assert.Throws<InvalidOperationException>(() => session.CloseShift(100, 600, false, Context()));
        Assert.Throws<InvalidOperationException>(() => session.OpenShift("first", null, Context()));
        session.OpenShift("second", null, Context());
        session.CloseStore(100, 600, false, Context());
        Assert.Throws<InvalidOperationException>(() => session.OpenShift("third", null, Context()));
        Assert.Throws<InvalidOperationException>(() => session.CloseStore(100, 590, false, Context()));
        Assert.Equal(10, closing.CashDifference);
        Assert.Equal(2, session.Closings.Count);
    }

    [Fact]
    public void Commands_on_another_business_date_are_rejected()
    {
        var session = StoreSession.OpenStore("store", "first", 0, Context());
        Assert.Throws<InvalidOperationException>(() => session.EnsureCanCompletePayment(Context(20)));
        Assert.Throws<InvalidOperationException>(() => session.CloseStore(0, 0, false, Context(20)));
        session.CloseShift(0, null, false, Context());
        Assert.Throws<InvalidOperationException>(() => session.OpenShift("second", null, Context(20)));
        Assert.Null(session.ActiveShiftId);
    }

    [Fact]
    public void Empty_identifiers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => StoreSession.OpenStore("", "first", 0, Context()));
        Assert.Throws<ArgumentException>(() => StoreSession.OpenStore("store", " ", 0, Context()));
        Assert.Throws<ArgumentException>(() => new StoreOperationContext("", "terminal", DateTimeOffset.UtcNow, new DateOnly(2026, 11, 19)));
        Assert.Throws<ArgumentException>(() => new StoreOperationContext("employee", "", DateTimeOffset.UtcNow, new DateOnly(2026, 11, 19)));
        var session = StoreSession.OpenStore("store", "first", 0, Context());
        session.CloseShift(0, null, false, Context());
        Assert.Throws<ArgumentException>(() => session.OpenShift("", null, Context()));
        Assert.Null(session.ActiveShiftId);
    }

    [Fact]
    public void Negative_counts_fail_before_mutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StoreSession.OpenStore("store", "first", -1, Context()));
        var session = StoreSession.OpenStore("store", "first", 0, Context());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.CloseShift(0, -1, false, Context()));
        Assert.Equal("first", session.ActiveShiftId);
        Assert.Empty(session.Closings);
    }
}
