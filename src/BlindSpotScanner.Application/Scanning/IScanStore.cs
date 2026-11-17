using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <param name="Profile">The repository's latest profile version, or null when it has none.</param>
/// <param name="Previous">The latest succeeded report of the same repository and branch (for the delta).</param>
public sealed record ScanJob(ScanRun Run, Repository Repository, RepoProfile? Profile, IntegrationReport? Previous);

/// <summary>What the orchestrator reads and writes about one run.</summary>
public interface IScanStore
{
    Task<ScanJob?> LoadAsync(Guid runId, CancellationToken ct);

    Task SaveStepAsync(ScanStep step, CancellationToken ct);

    Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct);

    /// <summary>Persists the run's state (pin, report, outcome).</summary>
    Task SaveAsync(ScanRun run, CancellationToken ct);
}

/// <summary>Progress for the UI (doc 03: ScanProgress / ScanCompleted on /hubs/scans).</summary>
public interface IScanEvents
{
    Task ProgressAsync(Guid runId, string phase, int pct, string message, CancellationToken ct);

    Task CompletedAsync(Guid runId, RunStatus status, CancellationToken ct);
}
