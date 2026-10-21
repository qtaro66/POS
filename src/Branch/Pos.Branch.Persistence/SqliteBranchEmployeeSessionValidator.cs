using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchEmployeeSessionValidator :
    IBranchEmployeeSessionValidator
{
    private readonly string _connectionString;
    private readonly IBranchEmployeeStatusReader _employeeStatus;
    private readonly TimeProvider _timeProvider;

    public SqliteBranchEmployeeSessionValidator(
        string databasePath,
        IBranchEmployeeStatusReader employeeStatus,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The database path cannot be empty.",
                nameof(databasePath));
        }

        _employeeStatus = employeeStatus ??
            throw new ArgumentNullException(nameof(employeeStatus));
        _timeProvider = timeProvider ?? TimeProvider.System;
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

    public async Task<VerifiedBranchEmployeeSession?> ValidateActiveAsync(
        string branchId,
        string deviceId,
        string sessionId,
        ReadOnlyMemory<byte> presentedSecret,
        CancellationToken cancellationToken = default)
    {
        branchId = RequireIdentifier(branchId, nameof(branchId));
        deviceId = RequireIdentifier(deviceId, nameof(deviceId));
        if (!BranchEmployeeSessionTokenFormat.IsCanonicalSessionId(sessionId) ||
            presentedSecret.Length != BranchEmployeeSessionTokenFormat.SecretByteLength)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session.secret_hash, session.employee_id, session.expires_at
            FROM branch_employee_sessions AS session
            INNER JOIN branch_devices AS device
                ON device.branch_id = session.branch_id
               AND device.device_id = session.device_id
            WHERE session.session_id = $sessionId
              AND session.branch_id = $branchId
              AND session.device_id = $deviceId
              AND session.lifecycle_status = 'Active'
              AND device.lifecycle_status = 'Active'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sessionId", sessionId);
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$deviceId", deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var secretHash = reader.GetFieldValue<byte[]>(0);
        try
        {
            var employeeId = reader.GetString(1);
            var expiresAt = ParseUtcExpiry(reader.GetString(2));
            if (expiresAt <= _timeProvider.GetUtcNow() ||
                !BranchEmployeeSessionTokenFormat.VerifySecretHash(
                    secretHash,
                    presentedSecret.Span))
            {
                return null;
            }

            if (!await _employeeStatus.IsActiveAsync(
                    branchId,
                    employeeId,
                    cancellationToken))
            {
                return null;
            }

            return new VerifiedBranchEmployeeSession(
                branchId,
                deviceId,
                employeeId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretHash);
        }
    }

    private static DateTimeOffset ParseUtcExpiry(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var expiresAt) ||
            expiresAt.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "The stored employee session expiry is not a valid UTC timestamp.");
        }

        return expiresAt;
    }

    private static string RequireIdentifier(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The identifier cannot be empty.",
                parameterName)
            : value.Trim();
}
