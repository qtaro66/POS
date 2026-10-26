using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCentralMigrationPlannerTests
{
    [Fact]
    public void Empty_database_requires_every_catalog_migration()
    {
        var pending = PostgresCentralMigrationPlanner.Plan([]);

        var migration = Assert.Single(pending);
        Assert.Equal(1, migration.Version);
        Assert.Equal("0001_branch_cloud_events", migration.Name);
    }

    [Fact]
    public void Current_database_requires_no_migration()
    {
        var pending = PostgresCentralMigrationPlanner.Plan(
        [
            new PostgresAppliedMigration(1, "0001_branch_cloud_events")
        ]);

        Assert.Empty(pending);
    }

    [Theory]
    [InlineData(0, "0001_branch_cloud_events")]
    [InlineData(-1, "0001_branch_cloud_events")]
    [InlineData(1, "")]
    [InlineData(1, " ")]
    public void Invalid_applied_identity_is_rejected(int version, string name)
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                new PostgresAppliedMigration(version, name)
            ]));
    }

    [Theory]
    [InlineData(1, "0001-renamed")]
    [InlineData(2, "0002_unknown")]
    public void Catalog_mismatch_is_rejected(int version, string name)
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                new PostgresAppliedMigration(version, name)
            ]));
    }

    [Fact]
    public void Duplicate_applied_versions_are_rejected()
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                new PostgresAppliedMigration(1, "0001_branch_cloud_events"),
                new PostgresAppliedMigration(1, "0001_duplicate")
            ]));
    }

    [Fact]
    public void Duplicate_applied_names_are_rejected_ordinally()
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                new PostgresAppliedMigration(1, "0001_branch_cloud_events"),
                new PostgresAppliedMigration(2, "0001_branch_cloud_events")
            ]));
    }

    [Fact]
    public void Case_changed_name_is_rejected()
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                new PostgresAppliedMigration(1, "0001_BRANCH_CLOUD_EVENTS")
            ]));
    }

    [Fact]
    public void Null_applied_history_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PostgresCentralMigrationPlanner.Plan(null!));
    }

    [Fact]
    public void Null_applied_entry_is_rejected_as_drift()
    {
        Assert.Throws<PostgresCentralMigrationDriftException>(() =>
            PostgresCentralMigrationPlanner.Plan(
            [
                null!
            ]));
    }
}
