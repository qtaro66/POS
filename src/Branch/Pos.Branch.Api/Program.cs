using Pos.Branch.Api.Printing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddPrintSpoolPolling(builder.Configuration);
builder.Services.AddEscPosRasterRendering(builder.Configuration);
builder.Services.AddSnmpPrinterStatus(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Pos.Branch.Api",
    status = "Healthy",
    utc = DateTimeOffset.UtcNow,
}));

app.Run();
