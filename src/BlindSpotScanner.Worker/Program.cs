using BlindSpotScanner.Infrastructure;
using BlindSpotScanner.Infrastructure.Scanning;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBlindSpotScannerCore();
builder.Services.AddHostedService<ScanWorker>();

var host = builder.Build();
host.Run();
