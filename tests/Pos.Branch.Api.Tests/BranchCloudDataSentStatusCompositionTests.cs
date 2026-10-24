using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.Synchronization;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCloudDataSentStatusCompositionTests
{
    [Fact]
    public void Disabled_status_registers_no_query_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchCloudDataSentStatus:Enabled"] = "false",
            ["BranchCloudDataSentStatus:DatabasePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Null(provider.GetService<IBranchCloudDataSentStatusQuery>());
        Assert.Null(provider.GetService<IBranchCloudDeliveryConfirmationStore>());
        Assert.IsType<SqliteBranchCloudDataSentStatusReadinessProbe>(
            provider.GetRequiredService<IBranchCloudDataSentStatusReadinessProbe>());
    }

    [Fact]
    public void Enabled_status_fails_closed_without_existing_database()
    {
        var missingDatabase = Path.Combine(
            Path.GetTempPath(),
            $"missing-data-sent-{Guid.NewGuid():N}.db");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchCloudDataSentStatus:Enabled"] = "true",
            ["BranchCloudDataSentStatus:DatabasePath"] = missingDatabase,
            ["BranchEmployeeSessions:Enabled"] = "true"
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "existing Branch database file",
                StringComparison.Ordinal));
        Assert.False(File.Exists(missingDatabase));
    }

    [Fact]
    public async Task Enabled_status_composes_query_and_confirmation_store()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-data-sent-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            using var provider = Provider(new Dictionary<string, string?>
            {
                ["BranchCloudDataSentStatus:Enabled"] = "true",
                ["BranchCloudDataSentStatus:DatabasePath"] = databasePath,
                ["BranchEmployeeSessions:Enabled"] = "true"
            });

            provider.GetRequiredService<IStartupValidator>().Validate();
            var query = provider.GetRequiredService<IBranchCloudDataSentStatusQuery>();
            var store = provider
                .GetRequiredService<IBranchCloudDeliveryConfirmationStore>();

            Assert.IsType<SqliteBranchCloudDataSentStatusQuery>(query);
            Assert.IsType<SqliteBranchCloudDeliveryConfirmationStore>(store);
            Assert.Same(
                query,
                provider.GetRequiredService<IBranchCloudDataSentStatusQuery>());
            Assert.Same(
                store,
                provider.GetRequiredService<IBranchCloudDeliveryConfirmationStore>());
            Assert.DoesNotContain(
                provider.GetServices<IHostedService>(),
                service => service.GetType().Assembly == typeof(Program).Assembly);
            Assert.Null(await query.FindLatestAsync("BRANCH-01"));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Existing_confirmation_store_registration_is_preserved()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-data-sent-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            var custom = new AlwaysConfirmedStore();
            using var provider = Provider(
                new Dictionary<string, string?>
                {
                    ["BranchCloudDataSentStatus:Enabled"] = "true",
                    ["BranchCloudDataSentStatus:DatabasePath"] = databasePath,
                    ["BranchEmployeeSessions:Enabled"] = "true"
                },
                services => services.AddSingleton<
                    IBranchCloudDeliveryConfirmationStore>(custom));

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.Same(
                custom,
                provider.GetRequiredService<
                    IBranchCloudDeliveryConfirmationStore>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Existing_query_registration_is_preserved()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-data-sent-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            var custom = new NeverSentQuery();
            using var provider = Provider(
                new Dictionary<string, string?>
                {
                    ["BranchCloudDataSentStatus:Enabled"] = "true",
                    ["BranchCloudDataSentStatus:DatabasePath"] = databasePath,
                    ["BranchEmployeeSessions:Enabled"] = "true"
                },
                services => services.AddSingleton<IBranchCloudDataSentStatusQuery>(
                    custom));

            provider.GetRequiredService<IStartupValidator>().Validate();

            Assert.Same(
                custom,
                provider.GetRequiredService<IBranchCloudDataSentStatusQuery>());
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Enabled_status_requires_employee_sessions()
    {
        var services = new ServiceCollection();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["BranchCloudDataSentStatus:Enabled"] = "true",
            ["BranchCloudDataSentStatus:DatabasePath"] = @"C:\branch.db",
            ["BranchEmployeeSessions:Enabled"] = "false"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBranchCloudDataSentStatus(configuration));

        Assert.Contains("BranchEmployeeSessions", exception.Message);
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var configuration = Configuration(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            BranchCloudDataSentStatusServiceCollectionExtensions
                .AddBranchCloudDataSentStatus(null!, configuration));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddBranchCloudDataSentStatus(null!));
    }

    private static ServiceProvider Provider(
        IReadOnlyDictionary<string, string?> values,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddBranchCloudDataSentStatus(Configuration(values));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class NeverSentQuery : IBranchCloudDataSentStatusQuery
    {
        public Task<BranchCloudDataSentStatus?> FindLatestAsync(
            string branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BranchCloudDataSentStatus?>(null);
    }

    private sealed class AlwaysConfirmedStore :
        IBranchCloudDeliveryConfirmationStore
    {
        public Task<BranchCloudDeliveryConfirmationResult> ConfirmAsync(
            BranchCloudDeliveryConfirmation confirmation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(BranchCloudDeliveryConfirmationResult.Confirmed);
    }
}
