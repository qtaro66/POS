using Microsoft.Data.Sqlite;
using Pos.Application.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteTableSessionCommandStore : ITableSessionCommandStore
{
    private readonly SqliteConnectionFactory _connections;
    private readonly ISqliteBillPreviewContentSource _billPreviewContentSource;

    public SqliteTableSessionCommandStore(string databasePath)
        : this(databasePath, new SqliteBillPreviewContentSource())
    {
    }

    public SqliteTableSessionCommandStore(
        string databasePath,
        ISqliteBillPreviewContentSource billPreviewContentSource)
    {
        _connections = new SqliteConnectionFactory(databasePath);
        _billPreviewContentSource = billPreviewContentSource ??
            throw new ArgumentNullException(nameof(billPreviewContentSource));
    }

    public string DatabasePath => _connections.DatabasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        await SqliteBranchSchema.InitializeAsync(connection, cancellationToken);
    }

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
            _billPreviewContentSource);

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
