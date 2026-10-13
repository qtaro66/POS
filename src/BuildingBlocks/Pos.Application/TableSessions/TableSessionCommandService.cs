using Pos.Application.Printing;
using Pos.Application.Time;
using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public sealed class TableSessionCommandService
{
    private readonly ITableSessionCommandStore _store;
    private readonly IBranchClock _clock;

    public TableSessionCommandService(
        ITableSessionCommandStore store,
        IBranchClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<BranchCommandReceipt> OpenTableAsync(
        OpenTableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);
        ArgumentNullException.ThrowIfNull(command.TableId);

        return _store.ExecuteAtomicallyAsync(async (transaction, token) =>
        {
            var duplicate = await transaction.FindReceiptAsync(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                token);
            if (duplicate is not null)
            {
                return duplicate;
            }

            var receivedAt = _clock.UtcNow;
            var existing = await transaction.FindActiveSessionByTableAsync(
                command.Metadata.BranchId,
                command.TableId,
                token);
            if (existing is not null)
            {
                var existingReceipt = Accepted(command.Metadata, existing, receivedAt);
                await transaction.AddReceiptAsync(existingReceipt, token);
                return existingReceipt;
            }

            var session = TableSession.Open(
                command.Metadata.TableSessionId,
                command.TableId,
                TransitionContext(command.Metadata, receivedAt));
            await transaction.InsertSessionAsync(command.Metadata.BranchId, session, token);

            var receipt = Accepted(command.Metadata, session, receivedAt);
            await PersistAcceptedTransitionAsync(
                transaction,
                command.Metadata,
                session,
                receipt,
                token);
            return receipt;
        }, cancellationToken);
    }

    public Task<BranchCommandReceipt> RequestPaymentAsync(
        RequestPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Metadata);

        return _store.ExecuteAtomicallyAsync(async (transaction, token) =>
        {
            var duplicate = await transaction.FindReceiptAsync(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                token);
            if (duplicate is not null)
            {
                return duplicate;
            }

            var receivedAt = _clock.UtcNow;
            var session = await transaction.FindSessionAsync(
                command.Metadata.BranchId,
                command.Metadata.TableSessionId,
                token);
            if (session is null)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "TableSessionNotFound",
                    token);
            }

            if (session.Version != command.ExpectedVersion)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "TableVersionConflict",
                    token,
                    session);
            }

            var hasOpenCancellation = await transaction.HasOpenCancellationRequestAsync(
                command.Metadata.BranchId,
                command.Metadata.TableSessionId,
                token);

            if (session.Status != TableSessionStatus.Ordering)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "InvalidTableSessionState",
                    token,
                    session);
            }

            if (hasOpenCancellation)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "OpenCancellationRequest",
                    token,
                    session);
            }

            var captureRequest = new BillPreviewCaptureRequest(
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                session.Id,
                session.CurrentTableId,
                receivedAt);
            var previewContent = await transaction.CaptureBillPreviewContentAsync(captureRequest, token);

            try
            {
                session.RequestPayment(
                    hasOpenCancellation,
                    TransitionContext(command.Metadata, receivedAt));
            }
            catch (TableSessionRuleViolationException exception)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    exception.Rule,
                    token,
                    session);
            }
            catch (InvalidTableSessionTransitionException)
            {
                return await PersistRejectionAsync(
                    transaction,
                    command.Metadata,
                    receivedAt,
                    "InvalidTableSessionState",
                    token,
                    session);
            }

            await transaction.UpdateSessionAsync(
                command.Metadata.BranchId,
                session,
                command.ExpectedVersion,
                token);

            var snapshot = new BillPreviewSnapshot(
                $"{command.Metadata.BranchId}:{command.Metadata.RequestId}:bill-preview:1",
                command.Metadata.BranchId,
                command.Metadata.RequestId,
                session.Id,
                session.CurrentTableId,
                "ใบตรวจสอบรายการ / ยังไม่ใช่ใบเสร็จ",
                previewContent,
                receivedAt);
            await transaction.AddBillPreviewSnapshotAsync(snapshot, token);
            await transaction.AddPrintJobAsync(
                new PersistentPrintJob(
                    $"{command.Metadata.BranchId}:{command.Metadata.RequestId}:cashier-print:1",
                    command.Metadata.BranchId,
                    command.Metadata.RequestId,
                    session.Id,
                    snapshot.SnapshotId,
                    PrintJobPurpose.BillPreview,
                    PrintTargetStation.Cashier,
                    PersistentPrintJobStatus.PrintQueued,
                    AttemptCount: 0,
                    receivedAt),
                token);
            var receipt = Accepted(command.Metadata, session, receivedAt);
            await PersistAcceptedTransitionAsync(
                transaction,
                command.Metadata,
                session,
                receipt,
                token);
            return receipt;
        }, cancellationToken);
    }

    private static async Task PersistAcceptedTransitionAsync(
        ITableSessionCommandTransaction transaction,
        BranchCommandMetadata metadata,
        TableSession session,
        BranchCommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        var transition = session.Transitions[^1];
        await transaction.AddAuditAsync(
            new TableSessionAuditRecord(
                $"{metadata.BranchId}:{metadata.RequestId}:audit:1",
                metadata.BranchId,
                metadata.RequestId,
                transition),
            cancellationToken);
        await transaction.AddCloudOutboxAsync(
            new CloudSyncOutboxRecord(
                $"{metadata.BranchId}:{metadata.RequestId}:cloud:1",
                metadata.BranchId,
                metadata.RequestId,
                session.Id,
                transition.Action,
                session.Version,
                receipt.ServerReceivedAt),
            cancellationToken);
        await transaction.AddReceiptAsync(receipt, cancellationToken);
    }

    private static async Task<BranchCommandReceipt> PersistRejectionAsync(
        ITableSessionCommandTransaction transaction,
        BranchCommandMetadata metadata,
        DateTimeOffset receivedAt,
        string errorCode,
        CancellationToken cancellationToken,
        TableSession? session = null)
    {
        var receipt = new BranchCommandReceipt(
            metadata.RequestId,
            metadata.BranchId,
            metadata.DeviceId,
            metadata.EmployeeId,
            metadata.ClientCreatedAt,
            BranchCommandOutcome.Rejected,
            session?.Id ?? metadata.TableSessionId,
            session?.Status,
            session?.Version,
            errorCode,
            receivedAt);
        await transaction.AddReceiptAsync(receipt, cancellationToken);
        return receipt;
    }

    private static BranchCommandReceipt Accepted(
        BranchCommandMetadata metadata,
        TableSession session,
        DateTimeOffset receivedAt)
    {
        return new BranchCommandReceipt(
            metadata.RequestId,
            metadata.BranchId,
            metadata.DeviceId,
            metadata.EmployeeId,
            metadata.ClientCreatedAt,
            BranchCommandOutcome.BranchAccepted,
            session.Id,
            session.Status,
            session.Version,
            ErrorCode: null,
            receivedAt);
    }

    private static TableSessionTransitionContext TransitionContext(
        BranchCommandMetadata metadata,
        DateTimeOffset receivedAt)
    {
        return new TableSessionTransitionContext(
            metadata.EmployeeId,
            metadata.DeviceId,
            receivedAt);
    }
}
