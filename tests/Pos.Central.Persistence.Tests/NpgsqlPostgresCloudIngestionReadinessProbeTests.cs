using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class NpgsqlPostgresCloudIngestionReadinessProbeTests
{
    private const string OfflineConnectionString =
        "Host=central.invalid;Database=pos;Username=pos_app;Timeout=1";

    [Fact]
    public async Task Construction_does_not_open_a_database_connection()
    {
        await using var connectionFactory =
            new PostgresCentralConnectionFactory(OfflineConnectionString);

        var probe = new NpgsqlPostgresCloudIngestionReadinessProbe(
            connectionFactory);

        Assert.Equal(
            PostgresCentralMigrationCatalog.CurrentVersion,
            probe.ExpectedSchemaVersion);
    }

    [Fact]
    public void Missing_connection_factory_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NpgsqlPostgresCloudIngestionReadinessProbe(null!));
    }

    [Fact]
    public async Task Pre_cancelled_check_does_not_contact_postgres()
    {
        await using var connectionFactory =
            new PostgresCentralConnectionFactory(OfflineConnectionString);
        var probe = new NpgsqlPostgresCloudIngestionReadinessProbe(
            connectionFactory);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await probe.CheckAsync(cancellationSource.Token));
    }

    [Fact]
    public void Readiness_sql_is_read_only_and_schema_qualified()
    {
        var sql = string.Join(
            '\n',
            PostgresCloudIngestionReadinessContract.InspectTablesSql,
            PostgresCloudIngestionReadinessContract.ReadSchemaVersionSql,
            PostgresCloudIngestionReadinessContract.ReadEventColumnsSql);

        Assert.Contains("public.central_schema_migrations", sql,
            StringComparison.Ordinal);
        Assert.Contains("public.branch_cloud_events", sql,
            StringComparison.Ordinal);
        Assert.Contains("information_schema.columns", sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Required_columns_match_version_one_migration()
    {
        Assert.Equal(
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
        ],
            PostgresCloudIngestionReadinessContract.RequiredEventColumns);
    }
}
