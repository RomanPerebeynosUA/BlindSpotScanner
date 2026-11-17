using BlindSpotScanner.Api.Hubs;
using BlindSpotScanner.Infrastructure;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
// Enums travel as their names (RunStatus "Failed"), as the run endpoints already do by hand.
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddBlindSpotScannerCore();
builder.Services.AddSingleton<ScanEventRelay>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScanEventRelay>());

var app = builder.Build();

// v1 applies migrations at startup (D21); the worker retries until the tables exist.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BlindSpotScannerDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<ScanHub>("/hubs/scans");

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
