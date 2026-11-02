using System.Globalization;
using Microsoft.Data.Sqlite;
using Pos.Application.Printing;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchPrinterInventoryQuery : IBranchPrinterInventoryQuery
{
    private readonly string _connectionString;
    public SqliteBranchPrinterInventoryQuery(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
    }
    public async Task<BranchPrinterInventoryPage> ListAsync(string branchId, int maximumResults, string? afterPrinterId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        if (maximumResults is < 1 or > 200) { throw new ArgumentOutOfRangeException(nameof(maximumResults)); }
        if (afterPrinterId is not null && (string.IsNullOrWhiteSpace(afterPrinterId) || afterPrinterId.Length > 128 || afterPrinterId != afterPrinterId.Trim() || afterPrinterId.Any(char.IsControl))) { throw new ArgumentException("Invalid printer cursor.", nameof(afterPrinterId)); }
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        long total, active;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*), COALESCE(SUM(is_active = 1),0) FROM printer_profiles WHERE branch_id = $branch;";
            count.Parameters.AddWithValue("$branch", branchId.Trim());
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken); total = reader.GetInt64(0); active = reader.GetInt64(1);
        }
        var items = new List<BranchPrinterInventoryItem>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT printer_id, display_name, production_station_id, host, port, protocol, compatibility_profile_id,
                       manufacturer, model, product_code, firmware_version, paper_width_millimeters, status_mode,
                       supports_cash_drawer, is_active, updated_at
                FROM printer_profiles WHERE branch_id = $branch AND ($after IS NULL OR printer_id > $after COLLATE BINARY)
                ORDER BY printer_id COLLATE BINARY LIMIT $limit;
                """;
            read.Parameters.AddWithValue("$branch", branchId.Trim()); read.Parameters.AddWithValue("$after", (object?)afterPrinterId ?? DBNull.Value);
            read.Parameters.AddWithValue("$limit", maximumResults + 1);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8), Optional(reader, 9), Optional(reader, 10),
                    reader.GetInt32(11), reader.GetString(12), reader.GetBoolean(13), reader.GetBoolean(14),
                    DateTimeOffset.ParseExact(reader.GetString(15), ["O", "yyyy-MM-dd'T'HH:mm:ss'Z'"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
            }
        }
        await transaction.CommitAsync(cancellationToken);
        string? next = null;
        if (items.Count > maximumResults) { items.RemoveAt(items.Count - 1); next = items[^1].PrinterId; }
        return new(total, active, items, next);
    }
    private static string? Optional(SqliteDataReader reader, int ordinal)
    {
        var value = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
