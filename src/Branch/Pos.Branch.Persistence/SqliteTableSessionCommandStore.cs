using Microsoft.Data.Sqlite;
using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteTableSessionCommandStore : ITableSessionCommandStore
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ISqliteBillPreviewContentSource _billPreviewContentSource;
    private readonly ISqliteCatalogSnapshotResolver _catalogSnapshotResolver;

    public SqliteTableSessionCommandStore(string databasePath)
        : this(
            databasePath,
            new SqliteBillPreviewContentSource(),
            new SqliteCatalogSnapshotResolver())
    {
    }

    public SqliteTableSessionCommandStore(
        string databasePath,
        ISqliteBillPreviewContentSource billPreviewContentSource)
        : this(databasePath, billPreviewContentSource, new SqliteCatalogSnapshotResolver())
    {
    }

    public SqliteTableSessionCommandStore(
        string databasePath,
        ISqliteBillPreviewContentSource billPreviewContentSource,
        ISqliteCatalogSnapshotResolver catalogSnapshotResolver)
    {
        _connections = new SqliteConnectionFactory(databasePath);
        _billPreviewContentSource = billPreviewContentSource ??
            throw new ArgumentNullException(nameof(billPreviewContentSource));
        _catalogSnapshotResolver = catalogSnapshotResolver ??
            throw new ArgumentNullException(nameof(catalogSnapshotResolver));
    }

    public string DatabasePath => _connections.DatabasePath;

    public async Task EndEmployeeSessionAsync(string branchId, string deviceId, string employeeId,
        string sessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE branch_employee_sessions SET lifecycle_status = 'Revoked', revoked_at = $now,
                revocation_reason = 'TerminalLocked'
            WHERE branch_id = $branch AND device_id = $device AND employee_id = $employee
                AND session_id = $session AND lifecycle_status = 'Active';
            """;
        command.Parameters.AddWithValue("$branch", branchId);
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$employee", employeeId);
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$now", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await SqliteBranchSchema.InitializeAsync(connection, cancellationToken);
    }

    public Task<string?> ConfirmCancellationAsync(string branchId, string requestId, string employeeId,
        DateTimeOffset now, IReadOnlyList<CancellationProductionCheck> quantities, IReadOnlySet<string> untrackedItems, CancellationToken cancellationToken) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).ConfirmCancellationAsync(branchId, requestId, employeeId, now, quantities, untrackedItems, token),
            cancellationToken);

    public Task<bool> RejectCancellationAsync(string branchId, string requestId, CancellationToken cancellationToken) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).RejectCancellationAsync(branchId, requestId, token),
            cancellationToken);

    public Task<bool> RecordCancellationProductionAsync(string branchId, string requestId, string employeeId,
        DateTimeOffset now, IReadOnlyList<CancellationProductionCheck> checks, CancellationToken cancellationToken) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).RecordCancellationProductionAsync(branchId, requestId, employeeId, now, checks, token),
            cancellationToken);

    public Task<string?> QueueCancellationInspectionAsync(string branchId, string cancellationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).QueueCancellationInspectionAsync(branchId, cancellationId, now, token),
            cancellationToken);

    public Task<CashSaleResult?> CompleteCashSaleAsync(
        string branchId, string sessionId, string requestId, string employeeId, string deviceId,
        DateTimeOffset now, long expectedVersion, decimal tendered, CancellationToken cancellationToken) =>
        ExecuteAtomicallyAsync((transaction, token) =>
            ((SqliteTableSessionCommandTransaction)transaction).CompleteCashSaleAsync(
                branchId, sessionId, requestId, employeeId, deviceId, now, expectedVersion, tendered, token),
            cancellationToken);

    public async Task<TResult> ExecuteAtomicallyAsync<TResult>(
        Func<ITableSessionCommandTransaction, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var commandTransaction = new SqliteTableSessionCommandTransaction(
            connection,
            transaction,
            _billPreviewContentSource,
            _catalogSnapshotResolver);

        try
        {
            var result = await operation(commandTransaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the operation or commit exception. Disposal still releases the connection.
            }

            throw;
        }
    }
}
