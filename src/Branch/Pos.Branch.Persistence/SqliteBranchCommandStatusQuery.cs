using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.TableSessions;
using Pos.Domain.TableSessions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchCommandStatusQuery : IBranchCommandStatusQuery
{
    private readonly string _connectionString;

    public SqliteBranchCommandStatusQuery(string databasePath)
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

    public async Task<BranchCommandReceipt?> FindAsync(
        string branchId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        requestId = RequireIdentifier(requestId, nameof(requestId));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT request_id, branch_id, device_id, employee_id, client_created_at,
                   outcome, table_session_id, session_status, table_version,
                   error_code, server_received_at, error_details_json, branch_sequence
            FROM command_receipts
            WHERE branch_id = $branchId
              AND request_id = $requestId;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$requestId", requestId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new BranchCommandReceipt(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseTimestamp(reader.GetString(4)),
            ParseEnum<BranchCommandOutcome>(reader.GetString(5), "Branch command outcome"),
            TableSessionId.From(reader.GetString(6)),
            NullableEnum<TableSessionStatus>(reader, 7, "Table Session status"),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            NullableString(reader, 9),
            ParseTimestamp(reader.GetString(10)),
            NullableString(reader, 11),
            reader.IsDBNull(12) ? null : reader.GetInt64(12));
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
}
