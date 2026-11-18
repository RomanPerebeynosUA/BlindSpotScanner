using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// Doc 10 observability: a <c>scan.run</c> span with one <c>scan.phase</c> span per phase, and the
/// scan metrics. Exported by OpenTelemetry when the host configures an exporter (D26).
/// </summary>
public static class ScanTelemetry
{
    public const string SourceName = "BlindSpotScanner";

    public static readonly ActivitySource Source = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("bss.scan.duration", "s", "Scan duration");

    /// <summary>Tagged <c>direction</c> = input|output.</summary>
    public static readonly Counter<long> Tokens = Meter.CreateCounter<long>("bss.scan.tokens", "{token}", "LLM tokens per scan");

    public static readonly Counter<double> Cost = Meter.CreateCounter<double>("bss.scan.cost", "USD", "LLM cost per scan");

    /// <summary>Tagged <c>reason</c>: a fixed category from <see cref="FailureCategory"/>.</summary>
    public static readonly Counter<long> Failures = Meter.CreateCounter<long>("bss.scan.failures", "{scan}", "Failed scans");

    private static long _gitHubRateLimitRemaining = -1;

    /// <summary>A fixed tag for a failure reason (citation, validation, model-mismatch, profile, error), so the metric keeps a bounded number of series.</summary>
    public static string FailureCategory(string reason) =>
        reason.StartsWith("citation:", StringComparison.Ordinal) ? "citation"
        : reason.StartsWith("validation:", StringComparison.Ordinal) ? "validation"
        : reason.StartsWith("This worker runs", StringComparison.Ordinal) ? "model-mismatch"
        : reason.StartsWith("Profile version", StringComparison.Ordinal) ? "profile"
        : "error";

    static ScanTelemetry() =>
        Meter.CreateObservableGauge(
            "bss.github.ratelimit.remaining",
            () => GitHubRateLimitRemaining is var remaining and >= 0 ? [new Measurement<long>(remaining)] : Array.Empty<Measurement<long>>(),
            "{request}",
            "GitHub requests left in the current rate-limit window");

    /// <summary>The last <c>x-ratelimit-remaining</c> GitHub sent; -1 until one is seen.</summary>
    public static long GitHubRateLimitRemaining
    {
        get => Interlocked.Read(ref _gitHubRateLimitRemaining);
        set => Interlocked.Exchange(ref _gitHubRateLimitRemaining, value);
    }
}
