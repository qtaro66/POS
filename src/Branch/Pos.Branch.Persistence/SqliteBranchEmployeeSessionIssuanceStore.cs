using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchEmployeeSessionIssuanceStore :
    IBranchEmployeeSessionIssuanceStore
{
    private readonly string _connectionString;

    public SqliteBranchEmployeeSessionIssuanceStore(string databasePath)
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
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task CreateAsync(
        BranchEmployeeSessionRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var secretHash = registration.CopySecretHash();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO branch_employee_sessions (
                    session_id, branch_id, device_id, employee_id,
                    secret_hash, issued_at, expires_at, lifecycle_status,
                    revoked_at, revocation_reason)
                VALUES (
                    $sessionId, $branchId, $deviceId, $employeeId,
                    $secretHash, $issuedAt, $expiresAt, 'Active',
                    NULL, NULL);
                """;
            command.Parameters.AddWithValue("$sessionId", registration.SessionId);
            command.Parameters.AddWithValue("$branchId", registration.BranchId);
            command.Parameters.AddWithValue("$deviceId", registration.DeviceId);
            command.Parameters.AddWithValue("$employeeId", registration.EmployeeId);
            command.Parameters.AddWithValue("$secretHash", secretHash);
            command.Parameters.AddWithValue(
                "$issuedAt",
                registration.IssuedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue(
                "$expiresAt",
                registration.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretHash);
        }
    }
}
