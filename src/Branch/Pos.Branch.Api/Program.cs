using Pos.Branch.Api.Commands;
using Pos.Branch.Api.Printing;
using Pos.Branch.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddBranchDeviceAuthentication(builder.Configuration);
builder.Services.AddBranchEmployeeSessions(builder.Configuration);
builder.Services.AddBranchPairing(builder.Configuration);
builder.Services.AddBranchCommandStatusQuery(builder.Configuration);
builder.Services.AddPrintSpoolPolling(builder.Configuration);
builder.Services.AddPrintSpoolRetention(builder.Configuration);
builder.Services.AddEscPosRasterRendering(builder.Configuration);
builder.Services.AddSnmpPrinterStatus(builder.Configuration);
builder.Services.AddPrintRuntime(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseBranchPairingHttpsOnly(builder.Configuration);
app.UseBranchEmployeeSessionHttpsOnly(builder.Configuration);
app.UseHttpsRedirection();
app.UseRouting();
app.UseBranchPairingRateLimiting(builder.Configuration);

app.MapGet("/health", () => Results.Ok(new
{
    service = "Pos.Branch.Api",
    status = "Healthy",
    utc = DateTimeOffset.UtcNow,
}));
app.MapBranchPairingRedemptionEndpointIfEnabled(builder.Configuration);
app.MapBranchEmployeeSessionLoginEndpointIfEnabled(builder.Configuration);

app.Run();
