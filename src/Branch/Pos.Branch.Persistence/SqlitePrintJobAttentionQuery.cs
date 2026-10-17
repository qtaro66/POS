using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqlitePrintJobAttentionQuery : IPrintJobAttentionQuery
{
    public const int MaximumPageSize = 200;

    private const string AttentionPredicate = """
        j.branch_id = $branchId
        AND (
            j.status IN ('PrintQueued', 'Failed', 'Unknown')
            OR (
                j.status = 'Dispatching'
                AND (j.lease_expires_at IS NULL OR j.lease_expires_at <= $observedAt)
            )
        )
        """;

    private readonly string _connectionString;

    public SqlitePrintJobAttentionQuery(string databasePath)
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

    public async Task<PrintJobAttentionPage> ListAsync(
        string branchId,
        DateTimeOffset observedAt,
        int maximumResults,
        PrintJobAttentionCursor? cursor = null,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        if (maximumResults is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResults),
                $"The page size must be between 1 and {MaximumPageSize}.");
        }

        if (cursor is not null)
        {
            _ = RequireIdentifier(cursor.PrintJobId, nameof(cursor));
        }

        observedAt = observedAt.ToUniversalTime();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        try
        {
            var totalCount = await CountAsync(
                connection,
                transaction,
                branchId,
                observedAt,
                cancellationToken);
            var items = await ReadPageAsync(
                connection,
                transaction,
                branchId,
                observedAt,
                maximumResults,
                cursor,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            PrintJobAttentionCursor? nextCursor = null;
            if (items.Count > maximumResults)
            {
                items.RemoveAt(items.Count - 1);
                var last = items[^1];
                nextCursor = new PrintJobAttentionCursor(last.CreatedAt, last.PrintJobId);
            }

            return new PrintJobAttentionPage(totalCount, items, nextCursor);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private static async Task<long> CountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM print_jobs AS j WHERE {AttentionPredicate};";
        AddCommonParameters(command, branchId, observedAt);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<List<PrintJobAttentionItem>> ReadPageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string branchId,
        DateTimeOffset observedAt,
        int maximumResults,
        PrintJobAttentionCursor? cursor,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var cursorPredicate = cursor is null
            ? string.Empty
            : """
                AND (
                    j.created_at < $cursorCreatedAt
                    OR (j.created_at = $cursorCreatedAt AND j.print_job_id < $cursorPrintJobId)
                )
                """;
        command.CommandText = $$"""
            SELECT j.print_job_id, j.request_id, j.table_session_id, j.snapshot_id,
                   j.purpose, j.target_station, j.status, j.created_at,
                   j.assigned_printer_id, j.next_retry_at, j.blocked_reason, j.last_error,
                   a.status, a.error_code, a.finished_at
            FROM print_jobs AS j
            LEFT JOIN print_attempts AS a
              ON a.print_attempt_id = (
                  SELECT latest.print_attempt_id
                  FROM print_attempts AS latest
                  WHERE latest.print_job_id = j.print_job_id
                  ORDER BY latest.attempt_number DESC
                  LIMIT 1)
            WHERE {{AttentionPredicate}}
              {{cursorPredicate}}
            ORDER BY j.created_at DESC, j.print_job_id DESC
            LIMIT $limit;
            """;
        AddCommonParameters(command, branchId, observedAt);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue(
                "$cursorCreatedAt",
                Timestamp(cursor.CreatedAt));
            command.Parameters.AddWithValue("$cursorPrintJobId", cursor.PrintJobId.Trim());
        }

        command.Parameters.AddWithValue("$limit", checked(maximumResults + 1));
        var items = new List<PrintJobAttentionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PrintJobAttentionItem(
                reader.GetString(0),
                reader.GetString(1),
                TableSessionId.From(reader.GetString(2)),
                reader.GetString(3),
                ParseEnum<PrintJobPurpose>(reader.GetString(4), "Print Job purpose"),
                new PrintTargetStation(reader.GetString(5)),
                ParseEnum<PersistentPrintJobStatus>(reader.GetString(6), "Print Job status"),
                ParseTimestamp(reader.GetString(7)),
                NullableString(reader, 8),
                NullableTimestamp(reader, 9),
                NullableString(reader, 10),
                NullableString(reader, 11),
                NullableEnum<PersistentPrintAttemptStatus>(
                    reader,
                    12,
                    "Print Attempt status"),
                NullableString(reader, 13),
                NullableTimestamp(reader, 14)));
        }

        return items;
    }

    private static void AddCommonParameters(
        SqliteCommand command,
        string branchId,
        DateTimeOffset observedAt)
    {
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$observedAt", Timestamp(observedAt));
    }

    private static T ParseEnum<T>(string value, string description)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"{description} '{value}' is not supported.");

    private static T? NullableEnum<T>(
        SqliteDataReader reader,
        int ordinal,
        string description)
        where T : struct, Enum =>
        reader.IsDBNull(ordinal)
            ? null
            : ParseEnum<T>(reader.GetString(ordinal), description);

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

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

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
