using Pos.Application.MasterData;
using Pos.Application.Printing;
using Pos.Domain.TableSessions;

namespace Pos.Application.TableSessions;

public interface ITableSessionCommandStore
{
    /// <summary>
    /// Executes the operation under a serialized, durable transaction. The implementation must
    /// roll back every write when the callback fails and must not return before commit succeeds.
    /// </summary>
    Task<TResult> ExecuteAtomicallyAsync<TResult>(
        Func<ITableSessionCommandTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}

public interface ITableSessionCommandTransaction
{
    Task<BranchCommandReceipt?> FindReceiptAsync(
        string branchId,
        string requestId,
        CancellationToken cancellationToken);

    Task<TableSession?> FindActiveSessionByTableAsync(
        string branchId,
        TableId tableId,
        CancellationToken cancellationToken);

    Task<TableSession?> FindSessionAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken);

    Task<ActiveMasterDataSnapshot?> FindActiveMasterDataSnapshotAsync(
        string branchId,
        CancellationToken cancellationToken);

    Task<bool> HasOpenCancellationRequestAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken);

    Task<bool> HasActiveOrderLinesAsync(
        string branchId,
        TableSessionId tableSessionId,
        CancellationToken cancellationToken);

    Task<BillPreviewContent> CaptureBillPreviewContentAsync(
        BillPreviewCaptureRequest request,
        CancellationToken cancellationToken);

    Task InsertSessionAsync(
        string branchId,
        TableSession session,
        CancellationToken cancellationToken);

    Task AddTableSessionBillingPolicyAsync(
        string branchId,
        TableSessionId tableSessionId,
        ActiveMasterDataSnapshot masterData,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task UpdateSessionAsync(
        string branchId,
        TableSession session,
        long expectedVersion,
        CancellationToken cancellationToken);

    Task AddReceiptAsync(
        BranchCommandReceipt receipt,
        CancellationToken cancellationToken);

    Task AddAuditAsync(
        TableSessionAuditRecord auditRecord,
        CancellationToken cancellationToken);

    Task AddCloudOutboxAsync(
        CloudSyncOutboxRecord outboxRecord,
        CancellationToken cancellationToken);

    Task AddBillPreviewSnapshotAsync(
        BillPreviewSnapshot snapshot,
        CancellationToken cancellationToken);

    Task AddPrintJobAsync(
        PersistentPrintJob printJob,
        CancellationToken cancellationToken);
}
