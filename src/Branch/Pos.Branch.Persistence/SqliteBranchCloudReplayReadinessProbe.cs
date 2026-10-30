using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed record BranchCloudReplayReadinessResult(
    bool IsReady,
    int? ActualSchemaVersion,
    IReadOnlyList<string> MissingTables,
    IReadOnlyDictionary<string, IReadOnlyList<string>> MissingColumns,
    string? ErrorCode);

public interface IBranchCloudReplayReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    BranchCloudReplayReadinessResult Check(string databasePath);
}

public sealed class SqliteBranchCloudReplayReadinessProbe : IBranchCloudReplayReadinessProbe
{
    private static readonly IReadOnlyDictionary<string, string[]> Required =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["cloud_sync_sequence_replay_requests"] =
            ["recovery_id", "branch_id", "expected_branch_sequence", "received_branch_sequence",
                "expected_outbox_id", "requested_at", "completed_at"],
            ["cloud_sync_sequence_replay_leases"] =
            ["recovery_id", "retry_count", "next_retry_at", "lease_owner", "claimed_at", "lease_expires_at"],
            ["cloud_sync_sequence_replay_failures"] =
            ["failure_id", "recovery_id", "branch_id", "outbox_id", "branch_sequence",
                "lease_owner", "claimed_at", "lease_expires_at", "failed_at", "next_retry_at",
                "error_code", "retry_count_after", "expected_branch_sequence"]
        };

    public int ExpectedSchemaVersion => SqliteBranchSchema.CurrentVersion;

    public BranchCloudReplayReadinessResult Check(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(databasePath),
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            using var version = connection.CreateCommand();
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version;";
            var actual = Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture);
            var missingTables = new List<string>();
            var missingColumns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (table, columns) in Required)
            {
                using var exists = connection.CreateCommand();
                exists.Transaction = transaction;
                exists.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = $name;";
                exists.Parameters.AddWithValue("$name", table);
                if (Convert.ToInt32(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                {
                    missingTables.Add(table);
                    missingColumns[table] = columns.ToArray();
                    continue;
                }
                using var info = connection.CreateCommand();
                info.Transaction = transaction;
                info.CommandText = $"PRAGMA table_info({table});";
                using var reader = info.ExecuteReader();
                var found = new HashSet<string>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    found.Add(reader.GetString(1));
                }
                var missing = columns.Where(column => !found.Contains(column)).ToArray();
                if (missing.Length > 0)
                {
                    missingColumns[table] = missing;
                }
            }
            var error = actual != ExpectedSchemaVersion ? "SchemaVersionMismatch"
                : missingTables.Count > 0 ? "ReplayTablesMissing"
                : missingColumns.Count > 0 ? "ReplayColumnsMissing" : null;
            return new(error is null, actual, missingTables, missingColumns, error);
        }
        catch (Exception exception) when (exception is SqliteException or IOException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, null, Required.Keys.ToArray(),
                Required.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.ToArray(), StringComparer.Ordinal),
                "DatabaseUnreadable");
        }
    }
}
