using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqliteBranchDeviceCredentialValidator : IBranchDeviceCredentialValidator
{
    private readonly string _connectionString;

    public SqliteBranchDeviceCredentialValidator(string databasePath)
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

    public async Task<VerifiedBranchDevice?> ValidateActiveAsync(
        string credentialId,
        ReadOnlyMemory<byte> credentialSecret,
        CancellationToken cancellationToken = default)
    {
        credentialId = RequireIdentifier(credentialId);
        if (credentialSecret.Length != BranchDeviceCredentialFormat.SecretByteLength)
        {
            return null;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT credential.secret_hash, credential.branch_id, credential.device_id
            FROM branch_device_credentials AS credential
            INNER JOIN branch_devices AS device
                ON device.branch_id = credential.branch_id
               AND device.device_id = credential.device_id
            WHERE credential.credential_id = $credentialId
              AND credential.lifecycle_status = 'Active'
              AND device.lifecycle_status = 'Active'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$credentialId", credentialId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var storedSecretHash = reader.GetFieldValue<byte[]>(0);
        try
        {
            if (!BranchDeviceCredentialFormat.VerifySecretHash(
                    storedSecretHash,
                    credentialSecret.Span))
            {
                return null;
            }

            return new VerifiedBranchDevice(reader.GetString(1), reader.GetString(2));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(storedSecretHash);
        }
    }

    private static string RequireIdentifier(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "The credential identifier cannot be empty.",
                nameof(value))
            : value.Trim();
}
