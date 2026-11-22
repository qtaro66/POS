using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralClosingReportContextHealthCheck(IPostgresClosingReportContextReadinessProbe readiness) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await readiness.IsReadyAsync(cancellationToken) ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Closing report lifecycle persistence is not ready.");
}
