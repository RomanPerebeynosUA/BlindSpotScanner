using BlindSpotScanner.Infrastructure;
using BlindSpotScanner.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBlindSpotScannerCore(builder.Configuration);
builder.Services.AddHostedService<ScanWorker>();

var host = builder.Build();
host.Run();
