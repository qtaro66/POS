using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Central.Api.Synchronization;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Tests;

public sealed class CentralCloudIngestionReadinessHealthCheckTests
{
    [Fact]
    public async Task Ready_probe_maps_to_healthy_with_schema_evidence()
    {
        var probe = new StubProbe(Result(ready: true));
        var healthCheck = new CentralCloudIngestionReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(1, result.Data["expectedSchemaVersion"]);
        Assert.Equal(1, result.Data["actualSchemaVersion"]);
        Assert.True((bool)result.Data["migrationTablePresent"]);
        Assert.Empty(Assert.IsType<string[]>(
            result.Data["missingBranchCloudEventColumns"]));
        Assert.DoesNotContain("errorCode", result.Data);
    }

    [Fact]
    public async Task Failed_probe_maps_to_unhealthy_without_sensitive_details()
    {
        var probe = new StubProbe(Result(ready: false));
        var healthCheck = new CentralCloudIngestionReadinessHealthCheck(probe);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            PostgresCloudIngestionReadinessErrorCodes.EventColumnsMissing,
            result.Data["errorCode"]);
        Assert.Equal(
            ["received_at"],
            Assert.IsType<string[]>(
                result.Data["missingBranchCloudEventColumns"]));
        Assert.DoesNotContain("connection", result.Description!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_probe()
    {
        var probe = new StubProbe(Result(ready: true));
        var healthCheck = new CentralCloudIngestionReadinessHealthCheck(probe);
        using var cancellationSource = new CancellationTokenSource();

        await healthCheck.CheckHealthAsync(
            new HealthCheckContext(),
            cancellationSource.Token);

        Assert.Equal(
            cancellationSource.Token,
            probe.ObservedCancellationToken);
    }

    [Fact]
    public void Missing_probe_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CentralCloudIngestionReadinessHealthCheck(null!));
    }

    [Fact]
    public async Task Missing_context_is_rejected_before_probe_access()
    {
        var probe = new StubProbe(Result(ready: true));
        var healthCheck = new CentralCloudIngestionReadinessHealthCheck(probe);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            healthCheck.CheckHealthAsync(null!));
        Assert.Equal(0, probe.CallCount);
    }

    private static PostgresCloudIngestionReadinessResult Result(bool ready) =>
        new(
            IsReady: ready,
            ExpectedSchemaVersion: 1,
            ActualSchemaVersion: 1,
            MigrationTablePresent: true,
            BranchCloudEventsTablePresent: true,
            MissingBranchCloudEventColumns: ready ? [] : ["received_at"],
            ErrorCode: ready
                ? null
                : PostgresCloudIngestionReadinessErrorCodes
                    .EventColumnsMissing);

    private sealed class StubProbe(
        PostgresCloudIngestionReadinessResult result) :
        IPostgresCloudIngestionReadinessProbe
    {
        public int ExpectedSchemaVersion => result.ExpectedSchemaVersion;

        public CancellationToken ObservedCancellationToken { get; private set; }

        public int CallCount { get; private set; }

        public ValueTask<PostgresCloudIngestionReadinessResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedCancellationToken = cancellationToken;
            return ValueTask.FromResult(result);
        }
    }
}
