using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchEmployeeStatusReader : IBranchEmployeeStatusReader
{
    private readonly string _connectionString;

    public SqliteBranchEmployeeStatusReader(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
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

    public async Task<bool> IsActiveAsync(
        string branchId,
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        employeeId = RequireIdentifier(employeeId, nameof(employeeId));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM branch_employee_status
                WHERE branch_id = $branchId
                  AND employee_id = $employeeId
                  AND lifecycle_status = 'Active');
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$employeeId", employeeId);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();
}
