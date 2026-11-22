using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Pos.Central.Persistence;

public sealed class NpgsqlPostgresClosingReportContextSessionFactory(PostgresCentralConnectionFactory connections)
    : IPostgresClosingReportContextSessionFactory
{
    public async ValueTask<IPostgresClosingReportContextSession> OpenTransactionAsync(CancellationToken token = default)
    {
        var connection = await connections.OpenConnectionAsync(token);
        try { return new Session(connection, await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token)); }
        catch { await connection.DisposeAsync(); throw; }
    }
    private sealed class Session(NpgsqlConnection connection, NpgsqlTransaction transaction) : IPostgresClosingReportContextSession
    {
        private bool _committed, _disposed;
        public async Task<string?> ReadParentAsync(string reportId, string branchId, CancellationToken token = default)
        {
            Active();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction; command.CommandText = PostgresClosingReportContextSessionContract.ReadParentSql;
            command.Parameters.AddWithValue("report_id", NpgsqlDbType.Text, reportId);
            command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
            return await command.ExecuteScalarAsync(token) as string;
        }
        public async Task<bool> TryInsertAsync(PostgresClosingReportContextRecord context, CancellationToken token = default)
        {
            Active();
            await using var command = Command(PostgresClosingReportContextSessionContract.TryInsertSql, context);
            command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, context.BranchId);
            command.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, context.RequestId);
            command.Parameters.AddWithValue("payload_json", NpgsqlDbType.Text, context.PayloadJson);
            return await command.ExecuteNonQueryAsync(token) switch { 0 => false, 1 => true, _ => throw new InvalidOperationException("Unexpected lifecycle context insert count.") };
        }
        public async Task<IReadOnlyList<PostgresClosingReportContextRecord>> ReadIdentityMatchesAsync(PostgresClosingReportContextRecord context, CancellationToken token = default)
        {
            Active();
            await using var command = Command(PostgresClosingReportContextSessionContract.ReadIdentityMatchesSql, context);
            await using var reader = await command.ExecuteReaderAsync(token);
            var records = new List<PostgresClosingReportContextRecord>();
            while (await reader.ReadAsync(token)) { records.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))); }
            return records.AsReadOnly();
        }
        public async Task CommitAsync(CancellationToken token = default) { Active(); await transaction.CommitAsync(token); _committed = true; }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) { return; }
            _disposed = true;
            try { if (!_committed) { await transaction.RollbackAsync(); } }
            finally { try { await transaction.DisposeAsync(); } finally { await connection.DisposeAsync(); } }
        }
        private NpgsqlCommand Command(string sql, PostgresClosingReportContextRecord context)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            command.Parameters.AddWithValue("context_id", NpgsqlDbType.Text, context.ContextId);
            command.Parameters.AddWithValue("report_id", NpgsqlDbType.Text, context.ReportId);
            return command;
        }
        private void Active() { ObjectDisposedException.ThrowIf(_disposed, this); if (_committed) { throw new InvalidOperationException("Lifecycle context transaction is already committed."); } }
    }
}
