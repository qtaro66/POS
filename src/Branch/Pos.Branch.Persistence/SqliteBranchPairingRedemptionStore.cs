using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchPairingRedemptionStore : IBranchPairingRedemptionStore
{
    private readonly string _connectionString;

    public SqliteBranchPairingRedemptionStore(string databasePath)
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

    public async Task<bool> TryRedeemAsync(
        BranchPairingRedemptionRegistration registration,
        ReadOnlyMemory<byte> presentedPairingSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (presentedPairingSecret.Length != BranchPairingTokenFormat.SecretByteLength)
        {
            return false;
        }

        var presentedSecret = presentedPairingSecret.ToArray();
        var credentialSecretHash = registration.CopyCredentialSecretHash();
        byte[]? storedPairingHash = null;
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction(deferred: false);

            string createdByEmployeeId;
            DateTimeOffset expiresAt;
            using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = """
                    SELECT created_by_employee_id, secret_hash, expires_at, lifecycle_status
                    FROM branch_pairing_tokens
                    WHERE branch_id = $branchId AND pairing_id = $pairingId;
                    """;
                find.Parameters.AddWithValue("$branchId", registration.BranchId);
                find.Parameters.AddWithValue("$pairingId", registration.PairingId);
                await using var reader = await find.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return false;
                }

                createdByEmployeeId = reader.GetString(0);
                storedPairingHash = reader.GetFieldValue<byte[]>(1);
                expiresAt = ParseUtcTimestamp(reader.GetString(2), "expires_at");
                var lifecycleStatus = reader.GetString(3);
                if (!string.Equals(lifecycleStatus, "Active", StringComparison.Ordinal) ||
                    registration.RedeemedAt >= expiresAt ||
                    !BranchPairingTokenFormat.VerifySecretHash(
                        storedPairingHash,
                        presentedSecret))
                {
                    return false;
                }
            }

            using (var insertDevice = connection.CreateCommand())
            {
                insertDevice.Transaction = transaction;
                insertDevice.CommandText = """
                    INSERT INTO branch_devices (
                        branch_id, device_id, display_name, lifecycle_status,
                        paired_by_employee_id, paired_at, last_employee_id,
                        last_seen_at, retired_by_employee_id, retired_at,
                        retirement_reason)
                    VALUES (
                        $branchId, $deviceId, $displayName, 'Active',
                        $pairedByEmployeeId, $pairedAt, NULL,
                        NULL, NULL, NULL, NULL);
                    """;
                insertDevice.Parameters.AddWithValue("$branchId", registration.BranchId);
                insertDevice.Parameters.AddWithValue("$deviceId", registration.DeviceId);
                insertDevice.Parameters.AddWithValue(
                    "$displayName",
                    registration.DeviceDisplayName);
                insertDevice.Parameters.AddWithValue(
                    "$pairedByEmployeeId",
                    createdByEmployeeId);
                insertDevice.Parameters.AddWithValue(
                    "$pairedAt",
                    FormatTimestamp(registration.RedeemedAt));
                await insertDevice.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var insertCredential = connection.CreateCommand())
            {
                insertCredential.Transaction = transaction;
                insertCredential.CommandText = """
                    INSERT INTO branch_device_credentials (
                        credential_id, branch_id, device_id, secret_hash,
                        lifecycle_status, issued_at, retired_at,
                        retirement_reason, replaced_by_credential_id)
                    VALUES (
                        $credentialId, $branchId, $deviceId, $secretHash,
                        'Active', $issuedAt, NULL, NULL, NULL);
                    """;
                insertCredential.Parameters.AddWithValue(
                    "$credentialId",
                    registration.CredentialId);
                insertCredential.Parameters.AddWithValue("$branchId", registration.BranchId);
                insertCredential.Parameters.AddWithValue("$deviceId", registration.DeviceId);
                insertCredential.Parameters.AddWithValue(
                    "$secretHash",
                    credentialSecretHash);
                insertCredential.Parameters.AddWithValue(
                    "$issuedAt",
                    FormatTimestamp(registration.RedeemedAt));
                await insertCredential.ExecuteNonQueryAsync(cancellationToken);
            }

            using (var consume = connection.CreateCommand())
            {
                consume.Transaction = transaction;
                consume.CommandText = """
                    UPDATE branch_pairing_tokens
                    SET lifecycle_status = 'Consumed',
                        terminal_at = $terminalAt,
                        terminal_reason = 'Consumed',
                        replaced_by_pairing_id = NULL
                    WHERE branch_id = $branchId
                      AND pairing_id = $pairingId
                      AND lifecycle_status = 'Active';
                    """;
                consume.Parameters.AddWithValue(
                    "$terminalAt",
                    FormatTimestamp(registration.RedeemedAt));
                consume.Parameters.AddWithValue("$branchId", registration.BranchId);
                consume.Parameters.AddWithValue("$pairingId", registration.PairingId);
                if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException(
                        "The active pairing challenge changed during redemption.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presentedSecret);
            CryptographicOperations.ZeroMemory(credentialSecretHash);
            if (storedPairingHash is not null)
            {
                CryptographicOperations.ZeroMemory(storedPairingHash);
            }
        }
    }

    private static DateTimeOffset ParseUtcTimestamp(string value, string columnName)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException(
                $"Branch pairing column '{columnName}' is not a canonical UTC timestamp.");
        }

        return parsed;
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);
}
