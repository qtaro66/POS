using Microsoft.Extensions.Options;
using Pos.Application.TableSessions;
using Pos.Application.Time;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Ordering;

public sealed class BranchOrderingOptions
{
    public const string SectionName = "BranchOrdering";
    public bool Enabled { get; init; }
    public string DatabasePath { get; init; } = string.Empty;
}

public sealed class BranchOrderingRuntime(string databasePath, TimeProvider time)
{
    public TableSessionCommandService Commands { get; } = new(new SqliteTableSessionCommandStore(databasePath), new Clock(time));
    public IBranchCommandStatusQuery Status { get; } = new SqliteBranchCommandStatusQuery(databasePath);
    public IBranchOrderingQuery Queries { get; } = new SqliteBranchOrderingQuery(databasePath);

    private sealed class Clock(TimeProvider provider) : IBranchClock
    {
        public DateTimeOffset UtcNow => provider.GetUtcNow();
    }
}

public sealed class BranchOrderingOptionsValidator(IConfiguration configuration) : IValidateOptions<BranchOrderingOptions>
{
    public ValidateOptionsResult Validate(string? name, BranchOrderingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled) { return ValidateOptionsResult.Success; }
        var path = options.DatabasePath;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
        {
            return ValidateOptionsResult.Fail("BranchOrdering requires an existing absolute DatabasePath.");
        }
        foreach (var dependency in new[] { "BranchDeviceAuthentication", "BranchEmployeeSessions" })
        {
            var section = configuration.GetSection(dependency);
            var otherPath = section.GetValue<string>("DatabasePath");
            if (!section.GetValue<bool>("Enabled") || string.IsNullOrWhiteSpace(otherPath)
                || !Path.IsPathFullyQualified(otherPath)
                || !string.Equals(Path.GetFullPath(path), Path.GetFullPath(otherPath),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Fail($"{dependency} must be enabled on the same database as BranchOrdering.");
            }
        }
        return SqliteBranchOrderingReadinessProbe.IsReady(path)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("BranchOrdering requires an initialized Branch database at the current schema version.");
    }
}

public static class BranchOrderingServiceCollectionExtensions
{
    public static IServiceCollection AddBranchOrdering(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<BranchOrderingOptions>().Bind(configuration.GetSection(BranchOrderingOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<BranchOrderingOptions>>(new BranchOrderingOptionsValidator(configuration));
        if (configuration.GetSection(BranchOrderingOptions.SectionName).GetValue<bool>("Enabled"))
        {
            services.AddSingleton(provider => new BranchOrderingRuntime(
                provider.GetRequiredService<IOptions<BranchOrderingOptions>>().Value.DatabasePath,
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        }
        return services;
    }
}
