using Npgsql;

namespace Pos.Central.Persistence;

public static class PostgresCloudIngestionReadinessContract
{
    public const string InspectTablesSql =
        """
        SELECT
            to_regclass('public.central_schema_migrations') IS NOT NULL,
            to_regclass('public.branch_cloud_events') IS NOT NULL;
        """;

    public const string ReadSchemaVersionSql =
        """
        SELECT COALESCE(MAX(version), 0)
        FROM public.central_schema_migrations;
        """;

    public const string ReadEventColumnsSql =
        """
        SELECT column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'branch_cloud_events';
        """;

    public static IReadOnlyList<string> RequiredEventColumns { get; } =
        Array.AsReadOnly(
        [
            "event_id",
            "branch_id",
            "branch_sequence",
            "request_id",
            "table_session_id",
            "action",
            "table_version",
            "occurred_at",
            "received_at"
        ]);
}

public sealed class NpgsqlPostgresCloudIngestionReadinessProbe :
    IPostgresCloudIngestionReadinessProbe
{
    private readonly PostgresCentralConnectionFactory _connectionFactory;

    public NpgsqlPostgresCloudIngestionReadinessProbe(
        PostgresCentralConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory));
    }

    public int ExpectedSchemaVersion =>
        PostgresCentralMigrationCatalog.CurrentVersion;

    public async ValueTask<PostgresCloudIngestionReadinessResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var connection =
                await _connectionFactory.OpenConnectionAsync(cancellationToken);
            var (migrationTablePresent, eventTablePresent) =
                await InspectTablesAsync(connection, cancellationToken);
            if (!migrationTablePresent)
            {
                return NotReady(
                    actualSchemaVersion: null,
                    migrationTablePresent: false,
                    eventTablePresent,
                    missingColumns: eventTablePresent ? [] : RequiredColumns(),
                    PostgresCloudIngestionReadinessErrorCodes
                        .MigrationTableMissing);
            }

            var actualSchemaVersion = await ReadSchemaVersionAsync(
                connection,
                cancellationToken);
            if (actualSchemaVersion != ExpectedSchemaVersion)
            {
                return NotReady(
                    actualSchemaVersion,
                    migrationTablePresent: true,
                    eventTablePresent,
                    missingColumns: eventTablePresent ? [] : RequiredColumns(),
                    PostgresCloudIngestionReadinessErrorCodes
                        .SchemaVersionMismatch);
            }

            if (!eventTablePresent)
            {
                return NotReady(
                    actualSchemaVersion,
                    migrationTablePresent: true,
                    eventTablePresent: false,
                    missingColumns: RequiredColumns(),
                    PostgresCloudIngestionReadinessErrorCodes.EventTableMissing);
            }

            var presentColumns = await ReadEventColumnsAsync(
                connection,
                cancellationToken);
            var missingColumns = PostgresCloudIngestionReadinessContract
                .RequiredEventColumns
                .Where(column => !presentColumns.Contains(column))
                .ToArray();
            if (missingColumns.Length > 0)
            {
                return NotReady(
                    actualSchemaVersion,
                    migrationTablePresent: true,
                    eventTablePresent: true,
                    missingColumns,
                    PostgresCloudIngestionReadinessErrorCodes
                        .EventColumnsMissing);
            }

            return new PostgresCloudIngestionReadinessResult(
                IsReady: true,
                ExpectedSchemaVersion,
                ActualSchemaVersion: actualSchemaVersion,
                MigrationTablePresent: true,
                BranchCloudEventsTablePresent: true,
                MissingBranchCloudEventColumns: [],
                ErrorCode: null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException)
        {
            return Unavailable(
                PostgresCloudIngestionReadinessErrorCodes.ProbeFailed);
        }
        catch (NpgsqlException)
        {
            return Unavailable(
                PostgresCloudIngestionReadinessErrorCodes.DatabaseUnavailable);
        }
        catch (InvalidOperationException)
        {
            return Unavailable(
                PostgresCloudIngestionReadinessErrorCodes.ProbeFailed);
        }
    }

    private static async Task<(bool MigrationTable, bool EventTable)>
        InspectTablesAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            PostgresCloudIngestionReadinessContract.InspectTablesSql;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "PostgreSQL returned no readiness table metadata.");
        }

        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<int> ReadSchemaVersionAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            PostgresCloudIngestionReadinessContract.ReadSchemaVersionSql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int version
            ? version
            : throw new InvalidOperationException(
                "PostgreSQL returned an invalid schema version.");
    }

    private static async Task<HashSet<string>> ReadEventColumnsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            PostgresCloudIngestionReadinessContract.ReadEventColumnsSql;
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private PostgresCloudIngestionReadinessResult NotReady(
        int? actualSchemaVersion,
        bool migrationTablePresent,
        bool eventTablePresent,
        IReadOnlyList<string> missingColumns,
        string errorCode) =>
        new(
            IsReady: false,
            ExpectedSchemaVersion,
            ActualSchemaVersion: actualSchemaVersion,
            MigrationTablePresent: migrationTablePresent,
            BranchCloudEventsTablePresent: eventTablePresent,
            MissingBranchCloudEventColumns: missingColumns,
            ErrorCode: errorCode);

    private PostgresCloudIngestionReadinessResult Unavailable(
        string errorCode) =>
        NotReady(
            actualSchemaVersion: null,
            migrationTablePresent: false,
            eventTablePresent: false,
            missingColumns: RequiredColumns(),
            errorCode);

    private static string[] RequiredColumns() =>
        [.. PostgresCloudIngestionReadinessContract.RequiredEventColumns];
}
