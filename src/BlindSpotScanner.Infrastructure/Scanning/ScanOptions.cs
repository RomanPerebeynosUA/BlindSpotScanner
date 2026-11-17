namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>The <c>Scan</c> configuration section (doc 10, plus the queue timings of D21).</summary>
public sealed class ScanOptions
{
    public int MaxConcurrentScans { get; set; } = 2;

    public int MaxFileBytes { get; set; } = 200_000;

    public int LeaseSeconds { get; set; } = 120;

    public int HeartbeatSeconds { get; set; } = 30;

    public int PollMilliseconds { get; set; } = 2_000;
}
