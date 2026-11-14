using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data.Sqlite;
using Pos.Application.Security;
using Pos.Branch.Api.Ordering;
using Pos.Branch.Api.Security;
using Pos.Branch.Api.Synchronization;
using Pos.Branch.Infrastructure.Sqlite;
using Pos.Contracts.Ordering;

if (args is ["--central", var fixtureId])
{
    await CentralFixture.RunAsync(Path.Combine(Path.GetTempPath(), $"pc-pos-browser-{Guid.ParseExact(fixtureId, "N"):N}"));
    return;
}

if (args.Length is < 1 or > 2 || !File.Exists(Path.Combine(args[0], "index.html")))
{
    throw new ArgumentException("Pass the path to the built PC POS dist directory.");
}

const string Branch = "BROWSER-TEST";
const string Employee = "TEST-EMPLOYEE";
var pin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
var persistent = args.Length == 2;
var directory = persistent
    ? new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"pc-pos-browser-{Guid.ParseExact(args[1], "N"):N}"))
    : Directory.CreateTempSubdirectory("pc-pos-browser-");
directory.Create();
var database = Path.Combine(directory.FullName, "branch.db");
try
{
    if (!File.Exists(database))
    {
        await new SqliteTableSessionCommandStore(database).InitializeAsync();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO branch_employee_status VALUES ($branch, $employee, 'Active', $now, $now);
                INSERT INTO master_data_packages VALUES ($branch,'TEST',1,$hash,$now,$now,'Active');
                INSERT INTO master_data_billing_policy VALUES ($branch,'TEST','TEST-CALC','0','0',2,'AwayFromZero');
                INSERT INTO master_data_sellable_items
                  (branch_id,master_data_version,sellable_item_id,item_kind,display_name,resolved_price,central_active,branch_catalog_enabled,production_station_id)
                  VALUES ($branch,'TEST','ITEM-1','Item','Browser test item','100',1,1,'TEST-KITCHEN');
                """;
            command.Parameters.AddWithValue("$branch", Branch);
            command.Parameters.AddWithValue("$employee", Employee);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$hash", new string('A', 64));
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteTableRegistryStore(database).ApplyAsync(
            new TableRegistry(1, Branch, "TEST",
                [new("A01", "Browser table A01", "Test zone", 1), new("A02", "Browser table A02", "Test zone", 2)]),
            CancellationToken.None);
    }

    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=PC POS browser fixture", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder();
    san.AddIpAddress(IPAddress.Loopback);
    request.CertificateExtensions.Add(san.Build());
    using var generatedCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));

    using var certificate = X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pkcs12), null);

    var origin = "https://127.0.0.1:17443";
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        WebRootPath = Path.GetFullPath(args[0]),
        EnvironmentName = "BrowserTest"
    });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection();
    builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 17443, listen => listen.UseHttps(certificate)));
    foreach (var section in new[] { "BranchDeviceAuthentication", "BranchEmployeeSessions", "BranchPairing", "BranchOrdering" })
    {
        builder.Configuration[section + ":Enabled"] = "true";
        builder.Configuration[section + ":DatabasePath"] = database;
    }
    builder.Configuration["BranchEmployeeSessions:Lifetime"] = "00:30:00";
    builder.Configuration["BranchPairing:ServiceOrigin"] = origin;
    builder.Configuration["BranchOrdering:EnableCashSaleTest"] = "true";
    builder.Services.AddSingleton<IBranchEmployeePinValidator>(new FixturePinValidator(Branch, Employee, pin));
    builder.Services.AddBranchDeviceAuthentication(builder.Configuration);
    builder.Services.AddBranchEmployeeSessions(builder.Configuration);
    builder.Services.AddBranchPairing(builder.Configuration);
    builder.Services.AddBranchOrdering(builder.Configuration);
    if (persistent)
    {
        const string delivery = "BranchCloudOutboxDelivery:";
        builder.Configuration[delivery + "Enabled"] = "true";
        builder.Configuration[delivery + "DatabasePath"] = database;
        builder.Configuration[delivery + "BranchId"] = Branch;
        builder.Configuration[delivery + "ServiceOrigin"] = CentralFixture.Origin;
        builder.Configuration[delivery + "DeliveryPath"] = "/api/v1/branch-events";
        builder.Configuration[delivery + "RetryDelay"] = "00:00:01";
        builder.Configuration[delivery + "MaximumRetryDelay"] = "00:00:02";
        builder.Configuration[delivery + "RequestTimeout"] = "00:00:02";
        builder.Services.AddSingleton<IBranchCloudDeliveryAuthenticatedHttpHandlerFactory>(new CentralFixture.Transport(directory.FullName));
        builder.Services.AddBranchCloudOutboxDelivery(builder.Configuration);
    }

    await using var app = builder.Build();
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseBranchPairingHttpsOnly(builder.Configuration);
    app.UseBranchEmployeeSessionHttpsOnly(builder.Configuration);
    app.UseBranchOrderingGuards(builder.Configuration);
    app.UseRouting();
    app.UseBranchPairingRateLimiting(builder.Configuration);
    app.Use(async (context, next) =>
    {
        // Every successfully paired device in this isolated database is a PC POS fixture.
        if (context.Request.Path == "/api/v1/employee-sessions")
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync(context.RequestAborted);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT device_id FROM branch_devices WHERE branch_id = 'BROWSER-TEST' ORDER BY device_id;";
            await using var devices = await command.ExecuteReaderAsync(context.RequestAborted);
            var index = 0;
            while (await devices.ReadAsync(context.RequestAborted))
            {
                var entry = $"BranchDeviceAuthentication:ProvisionedRoles:{index++}:";
                builder.Configuration[entry + "BranchId"] = Branch;
                builder.Configuration[entry + "DeviceId"] = devices.GetString(0);
                builder.Configuration[entry + "Role"] = "PcPos";
            }
        }
        await next(context);
    });
    app.UseBranchIdentity(builder.Configuration);
    app.MapBranchPairingRedemptionEndpointIfEnabled(builder.Configuration);
    app.MapBranchEmployeeSessionLoginEndpointIfEnabled(builder.Configuration);
    app.MapBranchOrderingIfEnabled(builder.Configuration);

    var pairing = await app.Services.GetRequiredService<BranchPairingTokenIssuanceService>().IssueAsync(Branch, Employee);
    var secondPairing = await app.Services.GetRequiredService<BranchPairingTokenIssuanceService>().IssueAsync(Branch, "TEST-PAIRING-OPERATOR");
    await app.StartAsync();
    Console.WriteLine($"Fixture PIN: {pin}");
    Console.WriteLine($"Fixture pairing: {origin}/#pair?v=1&branchId={Branch}&pairingId={pairing.PairingId}&token={pairing.Token}");
    Console.WriteLine($"Fixture second pairing: {origin}/#pair?v=1&branchId={Branch}&pairingId={secondPairing.PairingId}&token={secondPairing.Token}");
    await app.WaitForShutdownAsync();
}
finally
{
    SqliteConnection.ClearAllPools();
    if (!persistent) { directory.Delete(recursive: true); }
}

file sealed class FixturePinValidator(string branch, string employee, string expectedPin) : IBranchEmployeePinValidator
{
    public Task<VerifiedBranchEmployee?> ValidateActiveAsync(string branchId, ReadOnlyMemory<char> pin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(branchId == branch && pin.Span.SequenceEqual(expectedPin)
            ? new VerifiedBranchEmployee(branch, employee)
            : null);
    }
}
