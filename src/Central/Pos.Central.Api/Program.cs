using Pos.Central.Api.Synchronization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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

app.Run();
