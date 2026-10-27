using Pos.Central.Persistence;

namespace Pos.Central.Persistence.Tests;

public sealed class PostgresCloudIngestionReadinessBoundaryTests
{
    [Fact]
    public async Task Probe_contract_exposes_schema_evidence_and_cancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var expected = new PostgresCloudIngestionReadinessResult(
            IsReady: false,
            ExpectedSchemaVersion: PostgresCentralMigrationCatalog.CurrentVersion,
            ActualSchemaVersion: null,
            MigrationTablePresent: false,
            BranchCloudEventsTablePresent: false,
            MissingBranchCloudEventColumns: ["event_id"],
            ErrorCode:
                PostgresCloudIngestionReadinessErrorCodes.DatabaseUnavailable);
        var probe = new StubProbe(expected);

        var result = await probe.CheckAsync(cancellationSource.Token);

        Assert.Equal(PostgresCentralMigrationCatalog.CurrentVersion,
            probe.ExpectedSchemaVersion);
        Assert.Same(expected, result);
        Assert.Equal(cancellationSource.Token, probe.ObservedCancellationToken);
    }

    [Fact]
    public void Ready_result_can_report_complete_current_schema()
    {
        var result = new PostgresCloudIngestionReadinessResult(
            IsReady: true,
            ExpectedSchemaVersion: PostgresCentralMigrationCatalog.CurrentVersion,
            ActualSchemaVersion: PostgresCentralMigrationCatalog.CurrentVersion,
            MigrationTablePresent: true,
            BranchCloudEventsTablePresent: true,
            MissingBranchCloudEventColumns: [],
            ErrorCode: null);

        Assert.True(result.IsReady);
        Assert.Equal(result.ExpectedSchemaVersion, result.ActualSchemaVersion);
        Assert.Empty(result.MissingBranchCloudEventColumns);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void Failure_codes_are_stable_machine_readable_values()
    {
        Assert.Equal("database_unavailable",
            PostgresCloudIngestionReadinessErrorCodes.DatabaseUnavailable);
        Assert.Equal("migration_table_missing",
            PostgresCloudIngestionReadinessErrorCodes.MigrationTableMissing);
        Assert.Equal("schema_version_mismatch",
            PostgresCloudIngestionReadinessErrorCodes.SchemaVersionMismatch);
        Assert.Equal("event_table_missing",
            PostgresCloudIngestionReadinessErrorCodes.EventTableMissing);
        Assert.Equal("event_columns_missing",
            PostgresCloudIngestionReadinessErrorCodes.EventColumnsMissing);
        Assert.Equal("probe_failed",
            PostgresCloudIngestionReadinessErrorCodes.ProbeFailed);
    }

    private sealed class StubProbe(
        PostgresCloudIngestionReadinessResult result) :
        IPostgresCloudIngestionReadinessProbe
    {
        public int ExpectedSchemaVersion =>
            PostgresCentralMigrationCatalog.CurrentVersion;

        public CancellationToken ObservedCancellationToken { get; private set; }

        public ValueTask<PostgresCloudIngestionReadinessResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromResult(result);
        }
    }
}
