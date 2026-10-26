using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralMigrationCatalogTests
{
    [Fact]
    public void Version_one_defines_durable_branch_event_identity()
    {
        var migration = Assert.Single(
            PostgresCentralMigrationCatalog.GetMigrations());

        Assert.Equal(PostgresCentralMigrationCatalog.CurrentVersion, migration.Version);
        Assert.Equal("0001_branch_cloud_events", migration.Name);
        Assert.Contains(
            "event_id text PRIMARY KEY",
            migration.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "UNIQUE (branch_id, branch_sequence)",
            migration.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Version_one_persists_the_complete_normalized_envelope()
    {
        var sql = Assert.Single(
            PostgresCentralMigrationCatalog.GetMigrations()).Sql;
        var requiredColumns = new[]
        {
            "event_id",
            "branch_id",
            "branch_sequence",
            "request_id",
            "table_session_id",
            "action",
            "table_version",
            "occurred_at",
            "received_at"
        };

        Assert.All(requiredColumns, column =>
            Assert.Contains(column, sql, StringComparison.Ordinal));
        Assert.Contains(
            "CHECK (branch_sequence >= 1)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (table_version >= 1)",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Version_one_records_schema_version_after_table_definition()
    {
        var sql = Assert.Single(
            PostgresCentralMigrationCatalog.GetMigrations()).Sql;
        var tablePosition = sql.IndexOf(
            "CREATE TABLE branch_cloud_events",
            StringComparison.Ordinal);
        var versionPosition = sql.IndexOf(
            "INSERT INTO central_schema_migrations",
            StringComparison.Ordinal);

        Assert.True(tablePosition >= 0);
        Assert.True(versionPosition > tablePosition);
        Assert.Contains(
            "VALUES (1, '0001_branch_cloud_events')",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_is_ordered_and_has_no_duplicate_versions_or_names()
    {
        var migrations = PostgresCentralMigrationCatalog.GetMigrations();

        Assert.Equal(
            migrations.Select(migration => migration.Version).Order(),
            migrations.Select(migration => migration.Version));
        Assert.Equal(
            Enumerable.Range(1, migrations.Count),
            migrations.Select(migration => migration.Version));
        Assert.Equal(
            migrations.Count,
            migrations.Select(migration => migration.Version).Distinct().Count());
        Assert.Equal(
            migrations.Count,
            migrations.Select(migration => migration.Name).Distinct(
                StringComparer.Ordinal).Count());
        Assert.Equal(
            PostgresCentralMigrationCatalog.CurrentVersion,
            migrations[^1].Version);
    }
}
