using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Synchronization;

public sealed class ClosingReportDeliveryOptions
{
    public const string SectionName = "ClosingReportDelivery";
    public bool Enabled { get; init; }
    public bool LifecycleEnabled { get; init; }
    public string BranchId { get; init; } = string.Empty;
    public string CentralOrigin { get; init; } = string.Empty;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed class ClosingReportDeliveryOptionsValidator(IConfiguration configuration, IServiceProviderIsService services)
    : IValidateOptions<ClosingReportDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, ClosingReportDeliveryOptions options)
    {
        if (!options.Enabled)
        {
            return options.LifecycleEnabled
            ? ValidateOptionsResult.Fail("Lifecycle delivery requires base report delivery to be enabled.") : ValidateOptionsResult.Success;
        }
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.BranchId) || options.BranchId != options.BranchId.Trim())
        {
            errors.Add("Closing report delivery requires a canonical Branch ID.");
        }
        try { BranchCloudServiceOrigin.Require(options.CentralOrigin, nameof(options.CentralOrigin)); }
        catch (ArgumentException) { errors.Add("Closing report delivery requires a Central HTTPS origin without credentials, path, query or fragment."); }
        var path = configuration["BranchOrdering:DatabasePath"];
        if (!configuration.GetValue<bool>("BranchOrdering:Enabled") || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            errors.Add("Closing report delivery requires enabled Branch ordering and its absolute database path.");
        }
        if (!configuration.GetValue<bool>("BranchCentralMtls:Enabled") ||
            !services.IsService(typeof(IBranchCloudDeliveryAuthenticatedHttpHandlerFactory)))
        {
            errors.Add("Closing report delivery requires the Branch mTLS HTTP handler.");
        }
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(2) ||
            options.LeaseDuration <= options.RequestTimeout || options.LeaseDuration > TimeSpan.FromMinutes(10) ||
            options.IdleDelay <= TimeSpan.Zero || options.IdleDelay > TimeSpan.FromMinutes(5) ||
            options.RetryDelay <= TimeSpan.Zero || options.MaximumRetryDelay < options.RetryDelay ||
            options.MaximumRetryDelay > TimeSpan.FromHours(1))
        {
            errors.Add("Closing delivery timings must be positive and bounded; lease must exceed request timeout and maximum retry must cover initial retry.");
        }
        if (options.LifecycleEnabled && options.LeaseDuration.TotalSeconds <= options.RequestTimeout.TotalSeconds * 3)
        {
            errors.Add("Lifecycle delivery lease must exceed three HTTP request deadlines for bounded parent recovery.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

public static class ClosingReportDeliveryServiceCollectionExtensions
{
    public const string ClientName = "ClosingReportDelivery";

    public static IServiceCollection AddClosingReportDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClosingReportDeliveryOptions>()
            .Bind(configuration.GetSection(ClosingReportDeliveryOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ClosingReportDeliveryOptions>, ClosingReportDeliveryOptionsValidator>();
        // The disabled default registers no worker, HTTP client or database writer.
        if (!configuration.GetValue<bool>(ClosingReportDeliveryOptions.SectionName + ":Enabled")) { return services; }
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider => provider
                .GetRequiredService<IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>().CreateHandler());
        services.AddSingleton<IClosingReportTransport>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value;
            var endpoint = BranchCloudDeliveryEndpoint.Require(options.CentralOrigin, HttpClosingReportTransport.Route,
                nameof(options.CentralOrigin), nameof(HttpClosingReportTransport.Route));
            return new HttpClosingReportTransport(provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName),
                endpoint, options.RequestTimeout, provider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton(provider => new ClosingReportDeliveryCycle(
            new SqliteTableSessionCommandStore(configuration["BranchOrdering:DatabasePath"]!),
            provider.GetRequiredService<IClosingReportTransport>(), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>(),
            provider.GetService<ClosingLifecycleDeliveryCycle>()));
        if (configuration.GetValue<bool>(ClosingReportDeliveryOptions.SectionName + ":LifecycleEnabled"))
        {
            services.AddSingleton<IClosingLifecycleTransport>(provider =>
            {
                var settings = provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>().Value;
                var endpoint = BranchCloudDeliveryEndpoint.Require(settings.CentralOrigin, HttpClosingLifecycleTransport.Route,
                    nameof(settings.CentralOrigin), nameof(HttpClosingLifecycleTransport.Route));
                return new HttpClosingLifecycleTransport(provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName),
                    endpoint, settings.RequestTimeout, provider.GetRequiredService<TimeProvider>());
            });
            services.AddSingleton(provider => new ClosingLifecycleDeliveryCycle(
                new SqliteTableSessionCommandStore(configuration["BranchOrdering:DatabasePath"]!),
                provider.GetRequiredService<IClosingLifecycleTransport>(), provider.GetRequiredService<IClosingReportTransport>(),
                provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<IOptions<ClosingReportDeliveryOptions>>()));
        }
        services.AddHostedService<ClosingReportDeliveryWorker>();
        return services;
    }
}
