using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pos.Application.Security;
using Pos.Branch.Api.Security;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class BranchDeviceAuthenticationCompositionTests
{
    [Fact]
    public void Disabled_authentication_registers_no_runtime_boundary_and_requires_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchDeviceAuthentication:Enabled"] = "false",
            ["BranchDeviceAuthentication:DatabasePath"] = string.Empty
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Null(provider.GetService<IBranchDeviceCredentialValidator>());
        Assert.Null(provider.GetService<BranchDeviceTokenAuthenticationService>());
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public void Enabled_authentication_fails_closed_without_existing_database()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-device-auth-{Guid.NewGuid():N}.db");
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchDeviceAuthentication:Enabled"] = "true",
            ["BranchDeviceAuthentication:DatabasePath"] = path
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
    public async Task Enabled_authentication_rejects_wrong_schema_version()
    {
        await using var database = new AuthenticationDatabase();
        await database.InitializeAsync();
        await database.ExecuteAsync("PRAGMA user_version = 11;");
        using var provider = Provider(database.Configuration(enabled: true));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "schema version must be 15; actual version is 11",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_authentication_rejects_missing_device_tables()
    {
        await using var database = new AuthenticationDatabase();
        await database.CreateEmptyVersionTwelveAsync();
        using var provider = Provider(database.Configuration(enabled: true));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains(
                "missing required device credential tables",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enabled_authentication_composes_one_read_only_runtime_boundary()
    {
        await using var database = new AuthenticationDatabase();
        await database.InitializeAsync();
        var issued = BranchDeviceCredentialFormat.Generate();
        await database.SeedActiveCredentialAsync(issued);
        using var provider = Provider(database.Configuration(enabled: true));

        provider.GetRequiredService<IStartupValidator>().Validate();
        var validator = provider.GetRequiredService<IBranchDeviceCredentialValidator>();
        var authentication = provider.GetRequiredService<BranchDeviceTokenAuthenticationService>();
        var result = await authentication.AuthenticateAsync(issued.Token);

        Assert.IsType<SqliteBranchDeviceCredentialValidator>(validator);
        Assert.Same(
            validator,
            provider.GetRequiredService<IBranchDeviceCredentialValidator>());
        Assert.Same(
            authentication,
            provider.GetRequiredService<BranchDeviceTokenAuthenticationService>());
        var device = Assert.IsType<VerifiedBranchDevice>(result);
        Assert.Equal("BRANCH-01", device.BranchId);
        Assert.Equal("DEVICE-01", device.DeviceId);
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = Assert.IsType<AuthorizationPolicy>(
            await policyProvider.GetPolicyAsync(
                BranchDeviceAuthenticationDefaults.AuthorizationPolicyName));
        Assert.Equal(
            [BranchDeviceAuthenticationDefaults.SchemeName],
            policy.AuthenticationSchemes);
        Assert.Contains(
            policy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            policy.Requirements,
            requirement => HasClaimRequirement(
                requirement,
                BranchPosClaimTypes.BranchId));
        Assert.Contains(
            policy.Requirements,
            requirement => HasClaimRequirement(
                requirement,
                BranchPosClaimTypes.DeviceId));
        Assert.DoesNotContain(
            policy.Requirements,
            requirement => HasClaimRequirement(
                requirement,
                BranchPosClaimTypes.EmployeeId));
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service.GetType().Assembly == typeof(Program).Assembly);
    }

    private static bool HasClaimRequirement(
        IAuthorizationRequirement requirement,
        string claimType) =>
        requirement is ClaimsAuthorizationRequirement claims &&
        string.Equals(claims.ClaimType, claimType, StringComparison.Ordinal);

    private static ServiceProvider Provider(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBranchDeviceAuthentication(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class AuthenticationDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-device-auth-composition-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public IReadOnlyDictionary<string, string?> Configuration(bool enabled) =>
            new Dictionary<string, string?>
            {
                ["BranchDeviceAuthentication:Enabled"] = enabled.ToString(),
                ["BranchDeviceAuthentication:DatabasePath"] = Path
            };

        public async Task InitializeAsync()
        {
            await new SqliteTableSessionCommandStore(Path).InitializeAsync();
        }

        public async Task CreateEmptyVersionTwelveAsync()
        {
            Directory.CreateDirectory(_directory);
            await ExecuteAsync("PRAGMA user_version = 15;", SqliteOpenMode.ReadWriteCreate);
        }

        public async Task SeedActiveCredentialAsync(IssuedBranchDeviceCredential credential)
        {
            await ExecuteAsync(
                """
                INSERT INTO branch_devices (
                    branch_id, device_id, display_name, lifecycle_status,
                    paired_by_employee_id, paired_at)
                VALUES (
                    'BRANCH-01', 'DEVICE-01', 'Synthetic test device', 'Active',
                    'EMPLOYEE-01', '2026-10-19T00:00:00.0000000+00:00');

                INSERT INTO branch_device_credentials (
                    credential_id, branch_id, device_id, secret_hash,
                    lifecycle_status, issued_at)
                VALUES (
                    $credentialId, 'BRANCH-01', 'DEVICE-01', $secretHash,
                    'Active', '2026-10-19T00:00:00.0000000+00:00');
                """,
                SqliteOpenMode.ReadWrite,
                ("$credentialId", credential.CredentialId),
                ("$secretHash", credential.CopySecretHash()));
        }

        public Task ExecuteAsync(string sql) =>
            ExecuteAsync(sql, SqliteOpenMode.ReadWrite);

        public async ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            await ValueTask.CompletedTask;
        }

        private async Task ExecuteAsync(
            string sql,
            SqliteOpenMode mode,
            params (string Name, object Value)[] parameters)
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
            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            await command.ExecuteNonQueryAsync();
        }
    }
}
