using BlindSpotScanner.Api.Auth;
using BlindSpotScanner.Api.Hubs;
using BlindSpotScanner.Infrastructure;
using BlindSpotScanner.Infrastructure.Logging;
using BlindSpotScanner.Infrastructure.Telemetry;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
// Enums travel as their names (RunStatus "Failed"), as the run endpoints already do by hand.
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<BlindSpotScanner.Application.ICurrentUser, HttpCurrentUser>();
builder.Services.AddBlindSpotScannerCore();
builder.Services.AddBlindSpotScannerLogging(builder);
builder.Services.AddBlindSpotScannerTelemetry(
    builder.Configuration, "blindspotscanner-api", t => t.AddAspNetCoreInstrumentation(), m => m.AddAspNetCoreInstrumentation());
builder.AddBlindSpotScannerAuth();
builder.Services.AddSingleton<ScanEventRelay>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScanEventRelay>());

var app = builder.Build();

if (AuthSetup.LocalModeWarning(builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions(), app.Environment.IsDevelopment()) is { } localMode)
{
    app.Logger.LogWarning("{Warning}", localMode);
}

// v1 applies migrations at startup (D21); the worker retries until the tables exist.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BlindSpotScannerDbContext>().Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();
app.MapHub<ScanHub>("/hubs/scans").RequireAuthorization(Roles.Viewer);

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
