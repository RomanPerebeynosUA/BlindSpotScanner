using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>The scan job queue (doc 02: a DB table, behind an interface so a broker can replace it).</summary>
public interface IScanQueue
{
    Task EnqueueAsync(ScanRun run, CancellationToken ct);

    /// <summary>
    /// Takes the oldest queued run, or a running one whose lease expired and that has been tried only
    /// once (doc 10: a crashed worker's job is retried once). Marks it Running and leases it.
    /// </summary>
    Task<Guid?> TryDequeueAsync(TimeSpan lease, CancellationToken ct);

    /// <summary>Extends the lease; false when the run is no longer Running.</summary>
    Task<bool> HeartbeatAsync(Guid runId, TimeSpan lease, CancellationToken ct);

    /// <summary>Fails running runs whose lease expired after their retry; returns their ids.</summary>
    Task<IReadOnlyList<Guid>> FailAbandonedAsync(CancellationToken ct);
}
