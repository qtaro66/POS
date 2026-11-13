using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Pos.Central.Api.Synchronization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
var branchMtlsEnabled = builder.Configuration.GetValue<bool>("CentralBranchMtls:Enabled");
if (branchMtlsEnabled)
{
    builder.WebHost.ConfigureKestrel(options => options.ConfigureHttpsDefaults(https =>
        https.ClientCertificateMode = ClientCertificateMode.AllowCertificate));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, CentralBranchCertificateAuthentication>(
        CentralBranchCertificateAuthentication.SchemeName, _ => { });
    builder.Services.AddAuthorizationBuilder().AddPolicy(CentralBranchEmployeePermissions.Policy, policy =>
    {
        policy.AddAuthenticationSchemes(CentralBranchCertificateAuthentication.SchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(CentralCloudClaimTypes.BranchId);
    });
}
builder.Services.AddCentralCloudIngestionRuntime(
    builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseBranchCloudEventIngestionHttpsOnly(builder.Configuration);
app.UseHttpsRedirection();
app.UseCentralCloudIngestionSecurityIfEnabled(builder.Configuration);

app.MapGet("/health", () => Results.Ok(new
{
    service = "Pos.Central.Api",
    status = "Healthy",
    utc = DateTimeOffset.UtcNow,
}));
app.MapCentralCloudIngestionReadinessEndpointIfEnabled(
    builder.Configuration);
app.MapBranchCloudEventIngestionEndpointIfEnabled(builder.Configuration);
if (branchMtlsEnabled && builder.Configuration.GetValue<bool>("CentralEmployeePermissions:Enabled"))
{
    app.MapGet(CentralBranchEmployeePermissions.Route, CentralBranchEmployeePermissions.Read)
        .RequireAuthorization(CentralBranchEmployeePermissions.Policy);
}

if (branchMtlsEnabled && builder.Configuration.GetValue<bool>("CentralTableRegistry:Enabled"))
{
    app.MapGet(CentralBranchTableRegistry.Route, CentralBranchTableRegistry.Read)
        .RequireAuthorization(CentralBranchEmployeePermissions.Policy);
}

app.Run();
