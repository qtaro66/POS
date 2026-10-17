using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqlitePrintJobStatusQuery : IPrintJobStatusQuery
{
    private readonly string _connectionString;

    public SqlitePrintJobStatusQuery(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path cannot be empty.", nameof(databasePath));
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task<PrintJobStatusSnapshot?> FindAsync(
        string branchId,
        string printJobId,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        printJobId = RequireIdentifier(printJobId, nameof(printJobId));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var job = await ReadJobAsync(
                connection,
                transaction,
                branchId,
                printJobId,
                cancellationToken);
            if (job is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var attempts = await ReadAttemptsAsync(
                connection,
                transaction,
                printJobId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return job with { Attempts = attempts };
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<PrintJobStatusSnapshot?> ReadJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        string printJobId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT print_job_id, branch_id, request_id, table_session_id,
                   snapshot_id, purpose, target_station, status, attempt_count,
                   next_retry_at, last_error, blocked_reason, created_at,
                   assigned_printer_id, lease_expires_at, original_print_job_id,
                   is_reprint
            FROM print_jobs
            WHERE branch_id = $branchId
              AND print_job_id = $printJobId;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$printJobId", printJobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PrintJobStatusSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            TableSessionId.From(reader.GetString(3)),
            reader.GetString(4),
            ParseEnum<PrintJobPurpose>(reader.GetString(5), "Print Job purpose"),
            new PrintTargetStation(reader.GetString(6)),
            ParseEnum<PersistentPrintJobStatus>(reader.GetString(7), "Print Job status"),
            reader.GetInt32(8),
            NullableTimestamp(reader, 9),
            NullableString(reader, 10),
            NullableString(reader, 11),
            ParseTimestamp(reader.GetString(12)),
            NullableString(reader, 13),
            NullableTimestamp(reader, 14),
            NullableString(reader, 15),
            reader.GetInt64(16) == 1,
            []);
    }

    private static async Task<IReadOnlyList<PrintAttemptStatusSnapshot>> ReadAttemptsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string printJobId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT print_attempt_id, printer_id, attempt_number, payload_sha256,
                   status, automatic_retry_allowed, error_code, started_at,
                   first_byte_at, finished_at
            FROM print_attempts
            WHERE print_job_id = $printJobId
            ORDER BY attempt_number;
            """;
        command.Parameters.AddWithValue("$printJobId", printJobId);
        var attempts = new List<PrintAttemptStatusSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(new PrintAttemptStatusSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                ParseEnum<PersistentPrintAttemptStatus>(
                    reader.GetString(4),
                    "Print Attempt status"),
                reader.GetInt64(5) == 1,
                NullableString(reader, 6),
                ParseTimestamp(reader.GetString(7)),
                NullableTimestamp(reader, 8),
                NullableTimestamp(reader, 9)));
        }

        return attempts;
    }

    private static T ParseEnum<T>(string value, string description)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"{description} '{value}' is not supported.");

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTimeOffset? NullableTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseTimestamp(reader.GetString(ordinal));

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The identifier cannot be empty.", parameterName)
            : value.Trim();

    private static async Task RollbackQuietlyAsync(SqliteTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original query or commit exception.
        }
    }
}
