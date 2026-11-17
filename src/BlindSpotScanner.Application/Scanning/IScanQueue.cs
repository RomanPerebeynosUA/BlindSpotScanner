using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>The scan job queue (doc 02: a DB table, behind an interface so a broker can replace it).</summary>
public interface IScanQueue
{
    Task EnqueueAsync(ScanRun run, CancellationToken ct);

    /// <summary>
    /// Takes the oldest queued run, or a running one whose lease expired and that has been tried only
    /// once (doc 10: a crashed worker's job is retried once). Marks it Running and leases it to <paramref name="owner"/>.
    /// </summary>
    Task<Guid?> TryDequeueAsync(Guid owner, TimeSpan lease, CancellationToken ct);

    /// <summary>Extends the lease; false when the run is no longer Running or another worker now owns it.</summary>
    Task<bool> HeartbeatAsync(Guid runId, Guid owner, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Ends running runs whose lease expired after their retry: Cancelled when a cancel was requested,
    /// otherwise Failed. Returns each run with its final status.
    /// </summary>
    Task<IReadOnlyList<(Guid RunId, Domain.Enums.RunStatus Status)>> FailAbandonedAsync(CancellationToken ct);
}
