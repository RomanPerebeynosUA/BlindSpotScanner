using BlindSpotScanner.Infrastructure;
using BlindSpotScanner.Infrastructure.Logging;
using BlindSpotScanner.Infrastructure.Telemetry;
using BlindSpotScanner.Infrastructure.Scanning;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBlindSpotScannerCore();
builder.Services.AddBlindSpotScannerLogging(builder);
builder.Services.AddBlindSpotScannerTelemetry(builder.Configuration, "blindspotscanner-worker");
builder.Services.AddHostedService<ScanWorker>();

var host = builder.Build();
host.Run();
