namespace Pos.Domain.StoreSessions;

public sealed record StoreOperationContext
{
    public StoreOperationContext(string employeeId, string deviceId, DateTimeOffset occurredAt, DateOnly branchLocalDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        EmployeeId = employeeId;
        DeviceId = deviceId;
        OccurredAt = occurredAt.ToUniversalTime();
        BranchLocalDate = branchLocalDate;
    }

    public string EmployeeId { get; }
    public string DeviceId { get; }
    public DateTimeOffset OccurredAt { get; }
    public DateOnly BranchLocalDate { get; }
}

public sealed record StoreOperation(string Action, string StoreSessionId, string ShiftId, decimal? CashAmount, StoreOperationContext Context);

public sealed record ShiftClosing(string StoreSessionId, string ShiftId, bool ClosesStore, decimal ExpectedCashAmount,
    decimal? ActualCashAmount, decimal? CashDifference, StoreOperationContext Context)
{
    public string ReconciliationStatus => ActualCashAmount.HasValue ? "Reconciled" : "NotReconciled";
}

public sealed class StoreSession
{
    private readonly List<StoreOperation> _operations = [];
    private readonly List<ShiftClosing> _closings = [];
    private readonly HashSet<string> _shiftIds = new(StringComparer.Ordinal);

    private StoreSession(string id, decimal openingCashAmount, StoreOperationContext context)
    {
        Id = id;
        OpeningCashAmount = openingCashAmount;
        BusinessDate = context.BranchLocalDate;
    }

    public string Id { get; }
    public DateOnly BusinessDate { get; }
    public decimal OpeningCashAmount { get; }
    public string? ActiveShiftId { get; private set; }
    public bool IsClosed { get; private set; }
    public IReadOnlyList<StoreOperation> Operations => _operations.AsReadOnly();
    public IReadOnlyList<ShiftClosing> Closings => _closings.AsReadOnly();

    public static StoreSession OpenStore(string id, string firstShiftId, decimal openingCashAmount, StoreOperationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstShiftId);
        ArgumentNullException.ThrowIfNull(context);
        EnsureNonnegative(openingCashAmount);
        var session = new StoreSession(id, openingCashAmount, context);
        session.OpenShiftCore(firstShiftId, openingCashAmount, "OpenStore", context);
        return session;
    }

    public void OpenShift(string shiftId, decimal? openingCashAmount, StoreOperationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shiftId);
        EnsureOpen(context);
        EnsureNonnegative(openingCashAmount);
        if (ActiveShiftId is not null || _shiftIds.Contains(shiftId))
        {
            throw new InvalidOperationException("A shift is already active or the shift identifier was already used.");
        }
        OpenShiftCore(shiftId, openingCashAmount, "OpenShift", context);
    }

    public void EnsureCanCompletePayment(StoreOperationContext context)
    {
        EnsureOpen(context);
        if (ActiveShiftId is null)
        {
            throw new InvalidOperationException("Completing payment requires an active shift.");
        }
    }

    public ShiftClosing CloseShift(decimal committedNetAppliedCash, decimal? actualCashAmount,
        bool hasPartiallyPaidTables, StoreOperationContext context) =>
        Close(false, committedNetAppliedCash, actualCashAmount, hasPartiallyPaidTables, context);

    public ShiftClosing CloseStore(decimal committedNetAppliedCash, decimal actualCashAmount,
        bool hasPartiallyPaidTables, StoreOperationContext context) =>
        Close(true, committedNetAppliedCash, actualCashAmount, hasPartiallyPaidTables, context);

    private ShiftClosing Close(bool closesStore, decimal committedNetAppliedCash, decimal? actualCashAmount,
        bool hasPartiallyPaidTables, StoreOperationContext context)
    {
        EnsureOpen(context);
        EnsureNonnegative(committedNetAppliedCash);
        EnsureNonnegative(actualCashAmount);
        if (ActiveShiftId is null || hasPartiallyPaidTables)
        {
            throw new InvalidOperationException("Closing requires an active shift and resolution of partially paid tables.");
        }
        var expected = OpeningCashAmount + committedNetAppliedCash;
        var closing = new ShiftClosing(Id, ActiveShiftId, closesStore, expected, actualCashAmount,
            actualCashAmount - expected, context);
        _operations.Add(new(closesStore ? "CloseStore" : "CloseShift", Id, ActiveShiftId, actualCashAmount, context));
        _closings.Add(closing);
        ActiveShiftId = null;
        IsClosed = closesStore;
        return closing;
    }

    private void OpenShiftCore(string shiftId, decimal? amount, string action, StoreOperationContext context)
    {
        _shiftIds.Add(shiftId);
        ActiveShiftId = shiftId;
        _operations.Add(new(action, Id, shiftId, amount, context));
    }

    private void EnsureOpen(StoreOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsClosed || context.BranchLocalDate != BusinessDate)
        {
            throw new InvalidOperationException("The store session must be open on its original Branch business date.");
        }
    }

    private static void EnsureNonnegative(decimal? amount)
    {
        if (amount < 0) { throw new ArgumentOutOfRangeException(nameof(amount)); }
    }
}
