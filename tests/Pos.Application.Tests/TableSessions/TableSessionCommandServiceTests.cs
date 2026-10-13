using Pos.Application.MasterData;
using Pos.Application.Printing;
using Pos.Application.TableSessions;
using Pos.Application.Time;
using Pos.Domain.Billing;
using Pos.Domain.TableSessions;

namespace Pos.Application.Tests.TableSessions;

public sealed class TableSessionCommandServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Open_table_persists_session_receipt_audit_and_outbox_atomically()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);

        var result = await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));

        Assert.Equal(BranchCommandOutcome.BranchAccepted, result.Outcome);
        Assert.Equal(TableSessionStatus.Ordering, result.SessionStatus);
        Assert.Equal(1, result.TableVersion);
        Assert.Equal("DEVICE-01", result.DeviceId);
        Assert.Equal("EMPLOYEE-01", result.EmployeeId);
        Assert.Equal(Now.AddMinutes(-1), result.ClientCreatedAt);
        Assert.Single(store.Sessions);
        Assert.Single(store.Receipts);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
        var policy = Assert.Single(store.SessionPolicies);
        Assert.Equal("MASTER-1", policy.MasterData.MasterDataVersion);
        Assert.Equal("CALC-1", policy.MasterData.BillingPolicy.CalculationPolicyVersion);
        Assert.Equal(0, store.BillPreviewCaptureCount);
        Assert.Equal(TableSessionAction.OpenTable, store.Audits[0].Transition.Action);
    }

    [Fact]
    public async Task Repeating_request_id_returns_original_result_without_duplicate_records()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        var command = OpenCommand("REQ-1", "TS-1", "A04");

        var first = await service.OpenTableAsync(command);
        var duplicate = await service.OpenTableAsync(command);

        Assert.Equal(first, duplicate);
        Assert.Single(store.Sessions);
        Assert.Single(store.Receipts);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
        Assert.Single(store.SessionPolicies);
        Assert.Equal(0, store.BillPreviewCaptureCount);
    }

    [Fact]
    public async Task Concurrent_open_table_commands_return_same_active_session()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);

        var results = await Task.WhenAll(
            service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04")),
            service.OpenTableAsync(OpenCommand("REQ-2", "TS-2", "A04")));

        Assert.All(results, result => Assert.Equal("TS-1", result.TableSessionId.Value));
        Assert.Single(store.Sessions);
        Assert.Equal(2, store.Receipts.Count);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
        Assert.Single(store.SessionPolicies);
        Assert.Equal(0, store.BillPreviewCaptureCount);
    }

    [Fact]
    public async Task Open_table_without_active_master_data_is_idempotently_rejected()
    {
        var store = new InMemoryTableSessionCommandStore { ActiveMasterData = null };
        var service = Service(store);
        var command = OpenCommand("REQ-1", "TS-1", "A04");

        var first = await service.OpenTableAsync(command);
        var duplicate = await service.OpenTableAsync(command);

        Assert.Equal(BranchCommandOutcome.Rejected, first.Outcome);
        Assert.Equal("ActiveMasterDataUnavailable", first.ErrorCode);
        Assert.Equal(first, duplicate);
        Assert.Empty(store.Sessions);
        Assert.Empty(store.SessionPolicies);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Outbox);
        Assert.Single(store.Receipts);
    }

    [Fact]
    public async Task Existing_session_keeps_original_master_data_when_active_package_changes()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        store.ActiveMasterData = MasterData("MASTER-2", "CALC-2");

        var result = await service.OpenTableAsync(OpenCommand("REQ-2", "TS-2", "A04"));

        Assert.Equal("TS-1", result.TableSessionId.Value);
        var policy = Assert.Single(store.SessionPolicies);
        Assert.Equal("MASTER-1", policy.MasterData.MasterDataVersion);
        Assert.Equal("CALC-1", policy.MasterData.BillingPolicy.CalculationPolicyVersion);
    }

    [Fact]
    public async Task Request_payment_changes_state_and_persists_audit_and_outbox()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));

        var result = await service.RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", 1));

        Assert.Equal(BranchCommandOutcome.BranchAccepted, result.Outcome);
        Assert.Equal(TableSessionStatus.PaymentPending, result.SessionStatus);
        Assert.Equal(2, result.TableVersion);
        Assert.Equal(2, store.Receipts.Count);
        Assert.Equal(2, store.Audits.Count);
        Assert.Equal(2, store.Outbox.Count);
        Assert.Equal(TableSessionAction.RequestPayment, store.Audits[^1].Transition.Action);
        var snapshot = Assert.Single(store.BillPreviewSnapshots);
        Assert.Equal("ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ", snapshot.DocumentLabel);
        Assert.Equal(64, snapshot.Content.Sha256.Length);
        var printJob = Assert.Single(store.PrintJobs);
        Assert.Equal(PrintTargetStation.Cashier, printJob.TargetStation);
        Assert.Equal(PersistentPrintJobStatus.PrintQueued, printJob.Status);
        Assert.Equal(0, printJob.AttemptCount);
    }

    [Fact]
    public async Task Duplicate_request_payment_does_not_transition_or_enqueue_twice()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        var command = PaymentCommand("REQ-2", "TS-1", 1);

        var first = await service.RequestPaymentAsync(command);
        var duplicate = await service.RequestPaymentAsync(command);

        Assert.Equal(first, duplicate);
        Assert.Equal(2, store.Receipts.Count);
        Assert.Equal(2, store.Audits.Count);
        Assert.Equal(2, store.Outbox.Count);
        Assert.Equal(2, store.Sessions.Single().Version);
        Assert.Single(store.BillPreviewSnapshots);
        Assert.Single(store.PrintJobs);
        Assert.Equal(1, store.BillPreviewCaptureCount);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_without_state_change_or_outbox()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));

        var result = await service.RequestPaymentAsync(PaymentCommand("REQ-2", "TS-1", 99));

        Assert.Equal(BranchCommandOutcome.Rejected, result.Outcome);
        Assert.Equal("TableVersionConflict", result.ErrorCode);
        Assert.Equal(TableSessionStatus.Ordering, store.Sessions.Single().Status);
        Assert.Equal(1, store.Sessions.Single().Version);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
        Assert.Equal(0, store.BillPreviewCaptureCount);
    }

    [Fact]
    public async Task Open_cancellation_request_blocks_payment_and_is_idempotently_rejected()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        store.OpenCancellationSessionIds.Add("TS-1");
        var command = PaymentCommand("REQ-2", "TS-1", 1);

        var first = await service.RequestPaymentAsync(command);
        var duplicate = await service.RequestPaymentAsync(command);

        Assert.Equal(BranchCommandOutcome.Rejected, first.Outcome);
        Assert.Equal("OpenCancellationRequest", first.ErrorCode);
        Assert.Equal(first, duplicate);
        Assert.Equal(TableSessionStatus.Ordering, store.Sessions.Single().Status);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
        Assert.Equal(0, store.BillPreviewCaptureCount);
    }

    [Fact]
    public async Task Session_without_active_order_lines_is_idempotently_rejected()
    {
        var store = new InMemoryTableSessionCommandStore { HasActiveOrderLines = false };
        var service = Service(store);
        await service.OpenTableAsync(OpenCommand("REQ-1", "TS-1", "A04"));
        var command = PaymentCommand("REQ-2", "TS-1", 1);

        var first = await service.RequestPaymentAsync(command);
        var duplicate = await service.RequestPaymentAsync(command);

        Assert.Equal(BranchCommandOutcome.Rejected, first.Outcome);
        Assert.Equal("NoActiveOrderLines", first.ErrorCode);
        Assert.Equal(first, duplicate);
        Assert.Equal(TableSessionStatus.Ordering, store.Sessions.Single().Status);
        Assert.Equal(0, store.BillPreviewCaptureCount);
        Assert.Empty(store.BillPreviewSnapshots);
        Assert.Empty(store.PrintJobs);
    }

    [Fact]
    public async Task Missing_session_rejection_is_saved_for_idempotent_replay()
    {
        var store = new InMemoryTableSessionCommandStore();
        var service = Service(store);
        var command = PaymentCommand("REQ-404", "TS-404", 1);

        var first = await service.RequestPaymentAsync(command);
        var duplicate = await service.RequestPaymentAsync(command);

        Assert.Equal(BranchCommandOutcome.Rejected, first.Outcome);
        Assert.Equal("TableSessionNotFound", first.ErrorCode);
        Assert.Equal(first, duplicate);
        Assert.Single(store.Receipts);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Outbox);
        Assert.Equal(0, store.BillPreviewCaptureCount);
    }

    private static TableSessionCommandService Service(InMemoryTableSessionCommandStore store)
    {
        return new TableSessionCommandService(store, new FixedClock(Now));
    }

    private static OpenTableCommand OpenCommand(
        string requestId,
        string sessionId,
        string tableId)
    {
        return new OpenTableCommand(
            Metadata(requestId, sessionId),
            TableId.From(tableId));
    }

    private static RequestPaymentCommand PaymentCommand(
        string requestId,
        string sessionId,
        long expectedVersion)
    {
        return new RequestPaymentCommand(
            Metadata(requestId, sessionId),
            expectedVersion);
    }

    private static BranchCommandMetadata Metadata(string requestId, string sessionId)
    {
        return new BranchCommandMetadata(
            requestId,
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01",
            TableSessionId.From(sessionId),
            Now.AddMinutes(-1));
    }

    private static ActiveMasterDataSnapshot MasterData(
        string masterDataVersion = "MASTER-1",
        string calculationPolicyVersion = "CALC-1") =>
        new(
            masterDataVersion,
            1,
            new string('A', 64),
            Now.AddHours(-2),
            Now.AddHours(-1),
            new BillPolicySnapshot(
                masterDataVersion,
                calculationPolicyVersion,
                7m,
                0m,
                2,
                MonetaryRoundingMode.AwayFromZero));

    private sealed class FixedClock(DateTimeOffset utcNow) : IBranchClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryTableSessionCommandStore :
        ITableSessionCommandStore,
        ITableSessionCommandTransaction
    {
        private readonly SemaphoreSlim _transactionLock = new(1, 1);
        private readonly Dictionary<(string BranchId, string RequestId), BranchCommandReceipt> _receipts = [];
        private readonly Dictionary<(string BranchId, string SessionId), TableSession> _sessions = [];

        public IReadOnlyCollection<TableSession> Sessions => _sessions.Values;

        public IReadOnlyCollection<BranchCommandReceipt> Receipts => _receipts.Values;

        public List<TableSessionAuditRecord> Audits { get; } = [];

        public List<CloudSyncOutboxRecord> Outbox { get; } = [];

        public List<BillPreviewSnapshot> BillPreviewSnapshots { get; } = [];

        public List<PersistentPrintJob> PrintJobs { get; } = [];

        public List<(string BranchId, TableSessionId TableSessionId, ActiveMasterDataSnapshot MasterData, DateTimeOffset CreatedAt)>
            SessionPolicies
        { get; } = [];

        public HashSet<string> OpenCancellationSessionIds { get; } = [];

        public bool HasActiveOrderLines { get; set; } = true;

        public ActiveMasterDataSnapshot? ActiveMasterData { get; set; } = MasterData();

        public int BillPreviewCaptureCount { get; private set; }

        public async Task<TResult> ExecuteAtomicallyAsync<TResult>(
            Func<ITableSessionCommandTransaction, CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            await _transactionLock.WaitAsync(cancellationToken);
            try
            {
                return await operation(this, cancellationToken);
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        public Task<BranchCommandReceipt?> FindReceiptAsync(
            string branchId,
            string requestId,
            CancellationToken cancellationToken)
        {
            _receipts.TryGetValue((branchId, requestId), out var receipt);
            return Task.FromResult(receipt);
        }

        public Task<TableSession?> FindActiveSessionByTableAsync(
            string branchId,
            TableId tableId,
            CancellationToken cancellationToken)
        {
            var session = _sessions
                .Where(pair => pair.Key.BranchId == branchId)
                .Select(pair => pair.Value)
                .SingleOrDefault(candidate =>
                    candidate.CurrentTableId == tableId &&
                    candidate.Status is not (
                        TableSessionStatus.Completed or
                        TableSessionStatus.Cancelled or
                        TableSessionStatus.Closed));
            return Task.FromResult(session);
        }

        public Task<TableSession?> FindSessionAsync(
            string branchId,
            TableSessionId tableSessionId,
            CancellationToken cancellationToken)
        {
            _sessions.TryGetValue((branchId, tableSessionId.Value), out var session);
            return Task.FromResult(session);
        }

        public Task<ActiveMasterDataSnapshot?> FindActiveMasterDataSnapshotAsync(
            string branchId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ActiveMasterData);
        }

        public Task<bool> HasOpenCancellationRequestAsync(
            string branchId,
            TableSessionId tableSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OpenCancellationSessionIds.Contains(tableSessionId.Value));
        }

        public Task<bool> HasActiveOrderLinesAsync(
            string branchId,
            TableSessionId tableSessionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(HasActiveOrderLines);
        }

        public Task<BillPreviewContent> CaptureBillPreviewContentAsync(
            BillPreviewCaptureRequest request,
            CancellationToken cancellationToken)
        {
            BillPreviewCaptureCount++;
            return Task.FromResult(BillPreviewContent.FromJson(
                1,
                """
                {"items":[],"discounts":[],"charges":[],"vat":0,"roundingAdjustment":0,"netTotal":0}
                """));
        }

        public Task InsertSessionAsync(
            string branchId,
            TableSession session,
            CancellationToken cancellationToken)
        {
            _sessions.Add((branchId, session.Id.Value), session);
            return Task.CompletedTask;
        }

        public Task AddTableSessionBillingPolicyAsync(
            string branchId,
            TableSessionId tableSessionId,
            ActiveMasterDataSnapshot masterData,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken)
        {
            SessionPolicies.Add((branchId, tableSessionId, masterData, createdAt));
            return Task.CompletedTask;
        }

        public Task UpdateSessionAsync(
            string branchId,
            TableSession session,
            long expectedVersion,
            CancellationToken cancellationToken)
        {
            if (!_sessions.TryGetValue((branchId, session.Id.Value), out var stored) ||
                stored.Version != expectedVersion + 1)
            {
                throw new InvalidOperationException("The transactional expected-version contract was violated.");
            }

            return Task.CompletedTask;
        }

        public Task AddReceiptAsync(
            BranchCommandReceipt receipt,
            CancellationToken cancellationToken)
        {
            _receipts.Add((receipt.BranchId, receipt.RequestId), receipt);
            return Task.CompletedTask;
        }

        public Task AddAuditAsync(
            TableSessionAuditRecord auditRecord,
            CancellationToken cancellationToken)
        {
            Audits.Add(auditRecord);
            return Task.CompletedTask;
        }

        public Task AddCloudOutboxAsync(
            CloudSyncOutboxRecord outboxRecord,
            CancellationToken cancellationToken)
        {
            Outbox.Add(outboxRecord);
            return Task.CompletedTask;
        }

        public Task AddBillPreviewSnapshotAsync(
            BillPreviewSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            BillPreviewSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task AddPrintJobAsync(
            PersistentPrintJob printJob,
            CancellationToken cancellationToken)
        {
            PrintJobs.Add(printJob);
            return Task.CompletedTask;
        }
    }
}
