using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.TableSessions;
using Pos.Branch.Api.Commands;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchCommandStatusQueryCompositionTests
{
    [Fact]
    public void Disabled_query_registers_no_boundary_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchCommandStatusQuery:Enabled"] = "false",
            ["BranchCommandStatusQuery:DatabasePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<IBranchCommandStatusQuery>());
    }

    [Fact]
    public void Enabled_query_fails_closed_without_existing_database()
    {
        var missingDatabase = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.db");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchCommandStatusQuery:Enabled"] = "true",
            ["BranchCommandStatusQuery:DatabasePath"] = missingDatabase
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
    public async Task Enabled_query_composes_one_read_only_boundary()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            using var provider = Provider(new Dictionary<string, string?>
            {
                ["BranchCommandStatusQuery:Enabled"] = "true",
                ["BranchCommandStatusQuery:DatabasePath"] = databasePath
            });

            provider.GetRequiredService<IStartupValidator>().Validate();
            var query = provider.GetRequiredService<IBranchCommandStatusQuery>();

            Assert.IsType<SqliteBranchCommandStatusQuery>(query);
            Assert.Same(query, provider.GetRequiredService<IBranchCommandStatusQuery>());
            Assert.DoesNotContain(
                provider.GetServices<IHostedService>(),
                service => service.GetType().Assembly == typeof(Program).Assembly);
            Assert.Null(await query.FindAsync("BRANCH-01", "REQ-MISSING"));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Readiness_and_query_include_committed_wal_data()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"branch-{Guid.NewGuid():N}.db");
        try
        {
            await new SqliteTableSessionCommandStore(databasePath).InitializeAsync();
            await using var writer = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false
                }.ToString());
            await writer.OpenAsync();
            await ExecuteAsync(writer, "PRAGMA journal_mode = WAL;");
            await ExecuteAsync(writer, "PRAGMA wal_autocheckpoint = 0;");
            await ExecuteAsync(writer, """
                INSERT INTO command_receipts (
                    branch_id, request_id, device_id, employee_id,
                    client_created_at, outcome, table_session_id,
                    session_status, table_version, error_code,
                    server_received_at, error_details_json, branch_sequence)
                VALUES (
                    'BRANCH-01', 'REQ-WAL', 'DEVICE-01', 'EMPLOYEE-01',
                    '2026-10-17T10:00:00.0000000+00:00', 'Rejected', 'SESSION-01',
                    NULL, NULL, 'TestRejection',
                    '2026-10-17T10:00:01.0000000+00:00', NULL, 1);
                """);
            Assert.True(new FileInfo($"{databasePath}-wal").Length > 0);
            var probe = new SqliteBranchCommandStatusReadinessProbe();
            var query = new SqliteBranchCommandStatusQuery(databasePath);

            var result = probe.Check(databasePath);
            var receipt = await query.FindAsync("BRANCH-01", "REQ-WAL");

            Assert.True(result.IsReady);
            Assert.NotNull(receipt);
            Assert.Equal(BranchCommandOutcome.Rejected, receipt.Outcome);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-wal");
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBranchCommandStatusQuery(configuration);
        return services.BuildServiceProvider();
    }
}
