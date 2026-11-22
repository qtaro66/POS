using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pos.Central.Persistence;

namespace Pos.Central.Api.Synchronization;

public sealed class CentralClosingReportOptions
{
    public const string SectionName = "CentralClosingReports";
    public bool Enabled { get; init; }
}

public sealed class CentralClosingReportOptionsValidator(IConfiguration configuration) : IValidateOptions<CentralClosingReportOptions>
{
    public ValidateOptionsResult Validate(string? name, CentralClosingReportOptions options)
    {
        if (!options.Enabled) { return ValidateOptionsResult.Success; }
        return configuration.GetValue<bool>("CentralCloudIngestion:Enabled") &&
            configuration.GetValue<bool>("CentralBranchMtls:Enabled") &&
            configuration["CentralCloudIngestion:AuthenticationScheme"] == CentralBranchCertificateAuthentication.SchemeName
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Closing reports require Central Cloud ingestion and its BranchCertificate mTLS authentication scheme.");
    }
}

public sealed class CentralClosingReportHealthCheck(IPostgresClosingReportReadinessProbe readiness) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await readiness.IsReadyAsync(cancellationToken) ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Closing report persistence is not ready.");
}

public static class CentralClosingReportComposition
{
    public static IServiceCollection AddCentralClosingReports(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CentralClosingReportOptions>()
            .Bind(configuration.GetSection(CentralClosingReportOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CentralClosingReportOptions>, CentralClosingReportOptionsValidator>();
        if (!configuration.GetValue<bool>(CentralClosingReportOptions.SectionName + ":Enabled")) { return services; }
        var connectionString = configuration.GetConnectionString(CentralPostgresMigrationServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString)) { throw new InvalidOperationException("Closing reports require the Central database connection configuration."); }
        services.AddPostgresClosingReportIngestion(connectionString);
        services.AddPostgresClosingReportContexts(connectionString);
        services.AddHealthChecks().AddCheck<CentralClosingReportHealthCheck>("central-closing-reports", tags: ["ready"])
            .AddCheck<CentralClosingReportContextHealthCheck>("central-closing-report-contexts", tags: ["ready"]);
        return services;
    }

    public static IApplicationBuilder UseCentralClosingReportHttpsOnly(this IApplicationBuilder app, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(CentralClosingReportOptions.SectionName + ":Enabled")) { return app; }
        return app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps && context.Request.Path.StartsWithSegments(CentralClosingReportEndpoints.Route))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.StatusCode = 400;
                return;
            }
            await next(context);
        });
    }
}
