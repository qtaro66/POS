using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace Pos.Central.Persistence;

public sealed class NpgsqlPostgresClosingReportSessionFactory(PostgresCentralConnectionFactory connections)
    : IPostgresClosingReportSessionFactory
{
    public async ValueTask<IPostgresClosingReportSession> OpenTransactionAsync(CancellationToken token = default)
    {
        var connection = await connections.OpenConnectionAsync(token);
        try { return new Session(connection, await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token)); }
        catch { await connection.DisposeAsync(); throw; }
    }

    private sealed class Session(NpgsqlConnection connection, NpgsqlTransaction transaction) : IPostgresClosingReportSession
    {
        private bool _committed;
        private bool _disposed;
        public async Task<bool> TryInsertAsync(PostgresClosingReportRecord report, CancellationToken token = default)
        {
            EnsureActive();
            await using var command = Command(PostgresClosingReportSessionContract.TryInsertSql, report);
            command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, report.Operation);
            command.Parameters.AddWithValue("business_date", NpgsqlDbType.Date, report.BusinessDate);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, report.OccurredAt);
            command.Parameters.AddWithValue("payload_json", NpgsqlDbType.Text, report.PayloadJson);
            return await command.ExecuteNonQueryAsync(token) switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidOperationException("Unexpected closing report insert count.")
            };
        }
        public async Task<IReadOnlyList<PostgresClosingReportRecord>> ReadIdentityMatchesAsync(PostgresClosingReportRecord report, CancellationToken token = default)
        {
            EnsureActive();
            await using var command = Command(PostgresClosingReportSessionContract.ReadIdentityMatchesSql, report);
            await using var reader = await command.ExecuteReaderAsync(token);
            var matches = new List<PostgresClosingReportRecord>();
            while (await reader.ReadAsync(token))
            {
                matches.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateOnly>(6),
                    reader.GetFieldValue<DateTimeOffset>(7), reader.GetString(8)));
            }
            return matches.AsReadOnly();
        }
        public async Task CommitAsync(CancellationToken token = default)
        {
            EnsureActive();
            await transaction.CommitAsync(token);
            _committed = true;
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) { return; }
            _disposed = true;
            try { if (!_committed) { await transaction.RollbackAsync(); } }
            finally
            {
                try { await transaction.DisposeAsync(); }
                finally { await connection.DisposeAsync(); }
            }
        }
        private NpgsqlCommand Command(string sql, PostgresClosingReportRecord report)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("report_id", NpgsqlDbType.Text, report.ReportId);
            command.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, report.BranchId);
            command.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, report.RequestId);
            command.Parameters.AddWithValue("store_session_id", NpgsqlDbType.Text, report.StoreSessionId);
            command.Parameters.AddWithValue("shift_id", NpgsqlDbType.Text, report.ShiftId);
            return command;
        }
        private void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed) { throw new InvalidOperationException("Closing report transaction is already committed."); }
        }
    }
}
