using Microsoft.AspNetCore.Authorization;
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
        Assert.Null(provider.GetService<IBranchPairingRedemptionStore>());
        Assert.Null(provider.GetService<BranchPairingRedemptionService>());
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public void Enabled_pairing_fails_closed_without_existing_database()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-pairing-{Guid.NewGuid():N}.db");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchDeviceAuthentication:Enabled"] = "true",
            ["BranchEmployeeSessions:Enabled"] = "true",
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

    [Theory]
    [InlineData(false, true, "BranchDeviceAuthentication")]
    [InlineData(true, false, "BranchEmployeeSessions")]
    public async Task Enabled_pairing_requires_complete_identity_configuration(
        bool deviceAuthenticationEnabled,
        bool employeeSessionsEnabled,
        string missingSection)
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var configuration = database.Configuration(enabled: true).ToDictionary();
        configuration["BranchDeviceAuthentication:Enabled"] =
            deviceAuthenticationEnabled.ToString();
        configuration["BranchEmployeeSessions:Enabled"] =
            employeeSessionsEnabled.ToString();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Provider(configuration));

        Assert.Contains(missingSection, exception.Message, StringComparison.Ordinal);
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
                "schema version must be 21; actual version is 11",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_pairing_rejects_missing_pairing_table()
    {
        await using var database = new PairingDatabase();
        await database.ExecuteAsync(
            "PRAGMA user_version = 21;",
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
    public async Task Enabled_pairing_fails_closed_without_service_origin()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var configuration = database.Configuration(enabled: true).ToDictionary();
        configuration["BranchPairing:ServiceOrigin"] = string.Empty;
        using var provider = Provider(configuration);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "ServiceOrigin must be an absolute HTTPS IP origin",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://192.168.2.2")]
    [InlineData("https://branch.local")]
    [InlineData("https://192.168.2.2/pairing")]
    public async Task Enabled_pairing_rejects_non_phase_zero_service_origin(
        string serviceOrigin)
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        var configuration = database.Configuration(enabled: true).ToDictionary();
        configuration["BranchPairing:ServiceOrigin"] = serviceOrigin;
        using var provider = Provider(configuration);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "ServiceOrigin must be an absolute HTTPS IP origin",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_pairing_composes_sqlite_issuance_and_redemption_boundaries()
    {
        await using var database = new PairingDatabase();
        await database.InitializeAsync();
        using var provider = Provider(database.Configuration(enabled: true));

        provider.GetRequiredService<IStartupValidator>().Validate();
        var store = provider.GetRequiredService<IBranchPairingTokenIssuanceStore>();
        var service = provider.GetRequiredService<BranchPairingTokenIssuanceService>();
        var redemptionStore = provider.GetRequiredService<IBranchPairingRedemptionStore>();
        var redemptionService = provider.GetRequiredService<BranchPairingRedemptionService>();
        var challenge = await service.IssueAsync("BRANCH-01", "EMPLOYEE-01");
        var redeemed = await redemptionService.RedeemAsync(
            "BRANCH-01",
            challenge.PairingId,
            challenge.Token,
            "Dining Tablet 01");

        Assert.IsType<SqliteBranchPairingTokenIssuanceStore>(store);
        Assert.IsType<SqliteBranchPairingRedemptionStore>(redemptionStore);
        Assert.Same(
            store,
            provider.GetRequiredService<IBranchPairingTokenIssuanceStore>());
        Assert.Same(
            service,
            provider.GetRequiredService<BranchPairingTokenIssuanceService>());
        Assert.Same(
            redemptionStore,
            provider.GetRequiredService<IBranchPairingRedemptionStore>());
        Assert.Same(
            redemptionService,
            provider.GetRequiredService<BranchPairingRedemptionService>());
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
        Assert.True(BranchPairingTokenFormat.IsCanonicalPairingId(challenge.PairingId));
        Assert.NotNull(redeemed);
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM branch_pairing_tokens
                WHERE pairing_id = $pairingId AND lifecycle_status = 'Consumed';
                """,
                ("$pairingId", challenge.PairingId)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM branch_devices
                WHERE branch_id = 'BRANCH-01'
                  AND device_id = $deviceId
                  AND display_name = 'Dining Tablet 01'
                  AND lifecycle_status = 'Active';
                """,
                ("$deviceId", redeemed.DeviceId)));
        Assert.Equal(
            1L,
            await database.ScalarAsync<long>(
                """
                SELECT COUNT(*) FROM branch_device_credentials
                WHERE branch_id = 'BRANCH-01'
                  AND device_id = $deviceId
                  AND lifecycle_status = 'Active';
                """,
                ("$deviceId", redeemed.DeviceId)));
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
                ["BranchDeviceAuthentication:Enabled"] = "true",
                ["BranchEmployeeSessions:Enabled"] = "true",
                ["BranchPairing:Enabled"] = enabled.ToString(),
                ["BranchPairing:DatabasePath"] = Path,
                ["BranchPairing:ServiceOrigin"] = "https://192.168.2.2:8443"
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
