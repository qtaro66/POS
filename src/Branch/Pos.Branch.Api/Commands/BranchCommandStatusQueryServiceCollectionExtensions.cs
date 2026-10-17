using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Pos.Application.TableSessions;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Commands;

public static class BranchCommandStatusQueryServiceCollectionExtensions
{
    public static IServiceCollection AddBranchCommandStatusQuery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<
            IBranchCommandStatusReadinessProbe,
            SqliteBranchCommandStatusReadinessProbe>();
        services
            .AddOptions<BranchCommandStatusQueryOptions>()
            .Bind(configuration.GetSection(BranchCommandStatusQueryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<BranchCommandStatusQueryOptions>,
            BranchCommandStatusQueryOptionsValidator>();

        var enabled = configuration
            .GetSection(BranchCommandStatusQueryOptions.SectionName)
            .GetValue<bool>(nameof(BranchCommandStatusQueryOptions.Enabled));
        if (!enabled)
        {
            return services;
        }

        services.TryAddSingleton<IBranchCommandStatusQuery>(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<BranchCommandStatusQueryOptions>>()
                .Value;
            return new SqliteBranchCommandStatusQuery(options.DatabasePath.Trim());
        });
        return services;
    }
}
