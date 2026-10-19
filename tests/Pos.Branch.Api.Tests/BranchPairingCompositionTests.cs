using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchPairingCompositionTests
{
    [Fact]
    public void Disabled_pairing_registers_no_runtime_boundary_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchPairing:Enabled"] = "false",
            ["BranchPairing:DatabasePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<IBranchPairingTokenIssuanceStore>());
        Assert.Null(provider.GetService<BranchPairingTokenIssuanceService>());
    }

    [Fact]
    public void Enabled_pairing_fails_closed_without_existing_database()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-pairing-{Guid.NewGuid():N}.db");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchPairing:Enabled"] = "true",
            ["BranchPairing:DatabasePath"] = path
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "existing Branch database file",
                StringComparison.Ordinal));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Enabled_pairing_rejects_wrong_schema_version()
    {
        await using var database = new PairingDatabase();
        await database.ExecuteAsync(
            "PRAGMA user_version = 11;",
            SqliteOpenMode.ReadWriteCreate);
        using var provider = Provider(database.Configuration(enabled: true));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "schema version must be 12; actual version is 11",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_pairing_rejects_missing_pairing_table()
    {
        await using var database = new PairingDatabase();
        await database.ExecuteAsync(
            "PRAGMA user_version = 12;",
            SqliteOpenMode.ReadWriteCreate);
        using var provider = Provider(database.Configuration(enabled: true));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "missing required pairing tables",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_pairing_composes_one_sqlite_issuance_boundary()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        using var provider = Provider(database.Configuration(enabled: true));

        provider.GetRequiredService<IStartupValidator>().Validate();
        var store = provider.GetRequiredService<IBranchPairingTokenIssuanceStore>();
        var service = provider.GetRequiredService<BranchPairingTokenIssuanceService>();
        var challenge = await service.IssueAsync("BRANCH-01", "EMPLOYEE-01");

        Assert.IsType<SqliteBranchPairingTokenIssuanceStore>(store);
        Assert.Same(
            store,
            provider.GetRequiredService<IBranchPairingTokenIssuanceStore>());
        Assert.Same(
            service,
            provider.GetRequiredService<BranchPairingTokenIssuanceService>());
        Assert.True(BranchPairingTokenFormat.IsCanonicalPairingId(challenge.PairingId));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                "SELECT COUNT(*) FROM branch_pairing_tokens WHERE pairing_id = $pairingId;",
                ("$pairingId", challenge.PairingId)));
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            hosted => hosted.GetType().Assembly == typeof(Program).Assembly);
    }

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBranchPairing(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class PairingDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-pairing-composition-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public IReadOnlyDictionary<string, string?> Configuration(bool enabled) =>
            new Dictionary<string, string?>
            {
                ["BranchPairing:Enabled"] = enabled.ToString(),
                ["BranchPairing:DatabasePath"] = Path
            };

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task ExecuteAsync(
            string sql,
            SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
        {
            Directory.CreateDirectory(_directory);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = mode,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(
            string sql,
            params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            var value = await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Scalar query returned null.");
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
