using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchPairingTokenIssuanceStore :
    IBranchPairingTokenIssuanceStore
{
    private readonly string _connectionString;

    public SqliteBranchPairingTokenIssuanceStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path cannot be empty.", nameof(databasePath));
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

    public async Task ReplaceActiveForCreatorAsync(
        BranchPairingTokenRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var secretHash = registration.CopySecretHash();
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);

            using (var replace = connection.CreateCommand())
            {
                replace.Transaction = transaction;
                replace.CommandText = """
                    UPDATE branch_pairing_tokens
                    SET lifecycle_status = 'Replaced',
                        terminal_at = $terminalAt,
                        terminal_reason = 'ReplacedByNewChallenge',
                        replaced_by_pairing_id = $replacementPairingId
                    WHERE branch_id = $branchId
                      AND created_by_employee_id = $createdByEmployeeId
                      AND lifecycle_status = 'Active';
                    """;
                replace.Parameters.AddWithValue(
                    "$terminalAt",
                    FormatTimestamp(registration.IssuedAt));
                replace.Parameters.AddWithValue(
                    "$replacementPairingId",
                    registration.PairingId);
                replace.Parameters.AddWithValue("$branchId", registration.BranchId);
                replace.Parameters.AddWithValue(
                    "$createdByEmployeeId",
                    registration.CreatedByEmployeeId);
                await replace.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO branch_pairing_tokens (
                        pairing_id, branch_id, created_by_employee_id, secret_hash,
                        issued_at, expires_at, lifecycle_status, terminal_at,
                        terminal_reason, replaced_by_pairing_id)
                    VALUES (
                        $pairingId, $branchId, $createdByEmployeeId, $secretHash,
                        $issuedAt, $expiresAt, 'Active', NULL, NULL, NULL);
                    """;
                insert.Parameters.AddWithValue("$pairingId", registration.PairingId);
                insert.Parameters.AddWithValue("$branchId", registration.BranchId);
                insert.Parameters.AddWithValue(
                    "$createdByEmployeeId",
                    registration.CreatedByEmployeeId);
                insert.Parameters.AddWithValue("$secretHash", secretHash);
                insert.Parameters.AddWithValue(
                    "$issuedAt",
                    FormatTimestamp(registration.IssuedAt));
                insert.Parameters.AddWithValue(
                    "$expiresAt",
                    FormatTimestamp(registration.ExpiresAt));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretHash);
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
