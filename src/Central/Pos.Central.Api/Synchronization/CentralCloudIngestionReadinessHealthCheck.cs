using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralCloudIngestionReadinessHealthCheck : IHealthCheck
{
    public const string Name = "central-cloud-ingestion-postgres";

    public const string Tag = "ready";

    private readonly IPostgresCloudIngestionReadinessProbe _probe;

    public CentralCloudIngestionReadinessHealthCheck(
        IPostgresCloudIngestionReadinessProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = await _probe.CheckAsync(cancellationToken);
        var data = Evidence(result);
        return result.IsReady
            ? HealthCheckResult.Healthy(
                "Central PostgreSQL ingestion is ready.",
                data)
            : HealthCheckResult.Unhealthy(
                "Central PostgreSQL ingestion is not ready.",
                data: data);
    }

    private static IReadOnlyDictionary<string, object> Evidence(
        PostgresCloudIngestionReadinessResult result)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["expectedSchemaVersion"] = result.ExpectedSchemaVersion,
            ["migrationTablePresent"] = result.MigrationTablePresent,
            ["branchCloudEventsTablePresent"] =
                result.BranchCloudEventsTablePresent,
            ["missingBranchCloudEventColumns"] =
                result.MissingBranchCloudEventColumns.ToArray()
        };
        if (result.ActualSchemaVersion is { } actualSchemaVersion)
        {
            data["actualSchemaVersion"] = actualSchemaVersion;
        }

        if (result.ErrorCode is { } errorCode)
        {
            data["errorCode"] = errorCode;
        }

        return data;
    }
}
