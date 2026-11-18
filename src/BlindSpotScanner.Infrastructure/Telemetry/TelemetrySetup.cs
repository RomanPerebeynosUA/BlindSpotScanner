using BlindSpotScanner.Application.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BlindSpotScanner.Infrastructure.Telemetry;

/// <summary>
/// Doc 10 traces and metrics (D26): the scan's <see cref="ScanTelemetry"/> source and meter plus
/// HttpClient spans (GitHub, Jira, Claude calls). Exported over OTLP only when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set; otherwise nothing leaves the process.
/// </summary>
public static class TelemetrySetup
{
    public static IServiceCollection AddBlindSpotScannerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        var export = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t =>
            {
                t.AddSource(ScanTelemetry.SourceName).AddHttpClientInstrumentation();
                tracing?.Invoke(t);
                if (export)
                {
                    t.AddOtlpExporter();
                }
            })
            .WithMetrics(m =>
            {
                m.AddMeter(ScanTelemetry.SourceName).AddHttpClientInstrumentation();
                metrics?.Invoke(m);
                if (export)
                {
                    m.AddOtlpExporter();
                }
            });
        return services;
    }
}
