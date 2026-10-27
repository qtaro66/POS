namespace Pos.Central.Persistence;

public static class PostgresCloudIngestionReadinessErrorCodes
{
    public const string DatabaseUnavailable = "database_unavailable";

    public const string MigrationTableMissing = "migration_table_missing";

    public const string SchemaVersionMismatch = "schema_version_mismatch";

    public const string EventTableMissing = "event_table_missing";

    public const string EventColumnsMissing = "event_columns_missing";

    public const string ProbeFailed = "probe_failed";
}

public sealed record PostgresCloudIngestionReadinessResult(
    bool IsReady,
    int ExpectedSchemaVersion,
    int? ActualSchemaVersion,
    bool MigrationTablePresent,
    bool BranchCloudEventsTablePresent,
    IReadOnlyList<string> MissingBranchCloudEventColumns,
    string? ErrorCode);

public interface IPostgresCloudIngestionReadinessProbe
{
    int ExpectedSchemaVersion { get; }

    ValueTask<PostgresCloudIngestionReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default);
}
