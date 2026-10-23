using Microsoft.AspNetCore.Authentication;
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

public sealed class BranchEmployeeSessionCompositionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 22, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Disabled_sessions_register_no_runtime_boundaries_and_need_no_database()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["BranchEmployeeSessions:Enabled"] = "false",
            ["BranchEmployeeSessions:DatabasePath"] = string.Empty,
            ["BranchEmployeeSessions:Lifetime"] = "00:00:00"
        });

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Null(provider.GetService<IBranchEmployeeSessionLifetimePolicy>());
        Assert.Null(provider.GetService<IBranchEmployeeSessionIssuanceStore>());
        Assert.Null(provider.GetService<IBranchEmployeeSessionValidator>());
        Assert.Null(provider.GetService<BranchEmployeeSessionIssuanceService>());
        Assert.Null(provider.GetService<BranchEmployeeSessionAuthenticationService>());
        Assert.Null(provider.GetService<BranchEmployeePinAuthenticationService>());
        Assert.Null(provider.GetService<BranchEmployeeSessionLoginService>());
        Assert.Null(provider.GetService<IAuthenticationSchemeProvider>());
        Assert.Null(provider.GetService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public void Enabled_sessions_fail_closed_without_existing_database()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"missing-employee-sessions-{Guid.NewGuid():N}.db");
        using var provider = Provider(
            Configuration(path, enabled: true),
            services =>
            {
                services.AddSingleton<IBranchEmployeeStatusReader>(
                    new ActiveEmployeeStatusReader());
                services.AddSingleton<IBranchEmployeePinValidator>(
                    new ActiveEmployeePinValidator());
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
    public async Task Enabled_sessions_compose_persistence_lifetime_and_application_services()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        await database.SeedActiveDeviceAsync();
        var employeeStatus = new ActiveEmployeeStatusReader();
        using var provider = Provider(
            Configuration(database.Path, enabled: true),
            services =>
            {
                services.AddSingleton<IBranchEmployeeStatusReader>(employeeStatus);
                services.AddSingleton<IBranchEmployeePinValidator>(
                    new ActiveEmployeePinValidator());
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });

        provider.GetRequiredService<IStartupValidator>().Validate();
        var issuance = provider.GetRequiredService<BranchEmployeeSessionIssuanceService>();
        var issued = await issuance.IssueAsync(
            "BRANCH-01",
            "DEVICE-01",
            "EMPLOYEE-01");
        var authentication = provider
            .GetRequiredService<BranchEmployeeSessionAuthenticationService>();
        var verified = await authentication.AuthenticateAsync(
            "BRANCH-01",
            "DEVICE-01",
            issued.Token.AsMemory());

        Assert.Equal(Now.AddHours(8), issued.ExpiresAt);
        var session = Assert.IsType<VerifiedBranchEmployeeSession>(verified);
        Assert.Equal("BRANCH-01", session.BranchId);
        Assert.Equal("DEVICE-01", session.DeviceId);
        Assert.Equal("EMPLOYEE-01", session.EmployeeId);
        Assert.Equal(("BRANCH-01", "EMPLOYEE-01"), employeeStatus.LastRequest);
        var login = provider.GetRequiredService<BranchEmployeeSessionLoginService>();
        var loggedIn = await login.LoginAsync(
            "BRANCH-01",
            "DEVICE-01",
            "1234".AsMemory());
        Assert.NotNull(loggedIn);
        Assert.Equal("EMPLOYEE-01", loggedIn.EmployeeId);
        Assert.IsType<FixedBranchEmployeeSessionLifetimePolicy>(
            provider.GetRequiredService<IBranchEmployeeSessionLifetimePolicy>());
        Assert.IsType<SqliteBranchEmployeeSessionIssuanceStore>(
            provider.GetRequiredService<IBranchEmployeeSessionIssuanceStore>());
        Assert.IsType<SqliteBranchEmployeeSessionValidator>(
            provider.GetRequiredService<IBranchEmployeeSessionValidator>());
        Assert.Same(
            issuance,
            provider.GetRequiredService<BranchEmployeeSessionIssuanceService>());
        Assert.Same(
            authentication,
            provider.GetRequiredService<BranchEmployeeSessionAuthenticationService>());
        Assert.IsType<BranchEmployeePinAuthenticationService>(
            provider.GetRequiredService<BranchEmployeePinAuthenticationService>());
        Assert.Same(
            login,
            provider.GetRequiredService<BranchEmployeeSessionLoginService>());
        var scheme = await provider
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(BranchEmployeeSessionAuthenticationDefaults.SchemeName);
        Assert.NotNull(scheme);
        Assert.Equal(
            typeof(BranchEmployeeSessionAuthenticationHandler),
            scheme.HandlerType);
        var policy = Assert.IsType<AuthorizationPolicy>(
            await provider
                .GetRequiredService<IAuthorizationPolicyProvider>()
                .GetPolicyAsync(
                    BranchPosAuthorizationDefaults.CompleteIdentityPolicyName));
        Assert.Equal(
            [BranchEmployeeSessionAuthenticationDefaults.SchemeName],
            policy.AuthenticationSchemes);
        Assert.Contains(
            policy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is BranchCompleteIdentityAuthorizationRequirement);
        Assert.Contains(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is BranchCompleteIdentityAuthorizationHandler);
        Assert.DoesNotContain(
            provider.GetServices<IHostedService>(),
            service => service.GetType().Assembly == typeof(Program).Assembly);
    }

    [Fact]
    public async Task Enabled_sessions_compose_default_sqlite_employee_status_reader()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();

        using var provider = Provider(
            Configuration(database.Path, enabled: true),
            services => services.AddSingleton<IBranchEmployeePinValidator>(
                new ActiveEmployeePinValidator()));

        Assert.IsType<SqliteBranchEmployeeStatusReader>(
            provider.GetRequiredService<IBranchEmployeeStatusReader>());
        Assert.IsType<SqliteBranchEmployeeDataSyncStatusQuery>(
            provider.GetRequiredService<IBranchEmployeeDataSyncStatusQuery>());
    }

    [Fact]
    public async Task Enabled_sessions_require_device_authentication()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();
        var configuration = new Dictionary<string, string?>(
            Configuration(database.Path, enabled: true))
        {
            ["BranchDeviceAuthentication:Enabled"] = "false"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Provider(
                configuration,
                services =>
                {
                    services.AddSingleton<IBranchEmployeeStatusReader>(
                        new ActiveEmployeeStatusReader());
                    services.AddSingleton<IBranchEmployeePinValidator>(
                        new ActiveEmployeePinValidator());
                }));

        Assert.Contains(
            BranchDeviceAuthenticationOptions.SectionName,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabled_sessions_require_authoritative_employee_pin_validator()
    {
        await using var database = new SessionDatabase();
        await database.InitializeAsync();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Provider(
                Configuration(database.Path, enabled: true),
                services => services.AddSingleton<IBranchEmployeeStatusReader>(
                    new ActiveEmployeeStatusReader())));

        Assert.Contains(
            nameof(IBranchEmployeePinValidator),
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(
        IReadOnlyDictionary<string, string?> values,
        Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddBranchEmployeeSessions(configuration);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyDictionary<string, string?> Configuration(
        string databasePath,
        bool enabled) =>
        new Dictionary<string, string?>
        {
            ["BranchDeviceAuthentication:Enabled"] = "true",
            ["BranchEmployeeSessions:Enabled"] = enabled.ToString(),
            ["BranchEmployeeSessions:DatabasePath"] = databasePath,
            ["BranchEmployeeSessions:Lifetime"] = "08:00:00"
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ActiveEmployeeStatusReader : IBranchEmployeeStatusReader
    {
        public (string BranchId, string EmployeeId)? LastRequest { get; private set; }

        public Task<bool> IsActiveAsync(
            string branchId,
            string employeeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = (branchId, employeeId);
            return Task.FromResult(true);
        }
    }

    private sealed class ActiveEmployeePinValidator : IBranchEmployeePinValidator
    {
        public Task<VerifiedBranchEmployee?> ValidateActiveAsync(
            string branchId,
            ReadOnlyMemory<char> pin,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<VerifiedBranchEmployee?>(
                pin.Span.SequenceEqual("1234")
                    ? new VerifiedBranchEmployee(branchId, "EMPLOYEE-01")
                    : null);
        }
    }

    private sealed class SessionDatabase : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "pos-employee-session-composition-tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "branch.db");

        public Task InitializeAsync() =>
            new SqliteTableSessionCommandStore(Path).InitializeAsync();

        public async Task SeedActiveDeviceAsync()
        {
            await using var connection = Connection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO branch_devices (
                    branch_id, device_id, display_name, lifecycle_status,
                    paired_by_employee_id, paired_at)
                VALUES (
                    'BRANCH-01', 'DEVICE-01', 'Synthetic test device', 'Active',
                    'EMPLOYEE-01', '2026-10-22T03:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
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

        private SqliteConnection Connection() =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = SqliteOpenMode.ReadWrite,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
    }
}
