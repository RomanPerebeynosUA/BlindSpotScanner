using BlindSpotScanner.Domain.Entities;

namespace BlindSpotScanner.Application.Scanning;

/// <param name="DefaultMaxCostUsd"><c>Llm:MaxCostUsdPerScan</c> (doc 10), used when a request names no cap.</param>
public sealed record ScanRequestOptions(string ModelId, string PromptSetVersion, decimal DefaultMaxCostUsd);

public enum StartScanStatus
{
    Accepted,
    RepositoryNotFound,
    NoProfile,
    InvalidMaxCost,
}

public sealed record StartScanResult(StartScanStatus Status, Guid? RunId = null);

/// <summary>What the API's scan and run endpoints read and write.</summary>
public interface IScanRequestStore
{
    Task<bool> RepositoryExistsAsync(Guid repositoryId, CancellationToken ct);

    Task<int?> LatestProfileVersionAsync(Guid repositoryId, CancellationToken ct);

    /// <summary>The run, tracked so a cancel request can be saved.</summary>
    Task<ScanRun?> FindRunAsync(Guid runId, CancellationToken ct);

    Task<IReadOnlyList<ScanStep>> StepsAsync(Guid runId, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>The application side of the scan/run endpoints (OpenAPI): start, read, steps, cancel.</summary>
public sealed class ScanRequests(IScanRequestStore store, IScanQueue queue, IScanEvents events, TimeProvider time, ScanRequestOptions options)
{
    public async Task<StartScanResult> StartAsync(Guid repositoryId, string? branch, decimal? maxCostUsd, CancellationToken ct)
    {
        if (maxCostUsd is <= 0)
        {
            return new StartScanResult(StartScanStatus.InvalidMaxCost);
        }

        if (!await store.RepositoryExistsAsync(repositoryId, ct))
        {
            return new StartScanResult(StartScanStatus.RepositoryNotFound);
        }

        if (await store.LatestProfileVersionAsync(repositoryId, ct) is not { } profileVersion)
        {
            return new StartScanResult(StartScanStatus.NoProfile);
        }

        var run = ScanRun.Enqueue(
            Guid.NewGuid(), repositoryId, string.IsNullOrWhiteSpace(branch) ? null : branch.Trim(), maxCostUsd ?? options.DefaultMaxCostUsd,
            profileVersion, options.ModelId, options.PromptSetVersion, time.GetUtcNow());
        await queue.EnqueueAsync(run, ct);
        return new StartScanResult(StartScanStatus.Accepted, run.Id);
    }

    public Task<ScanRun?> FindAsync(Guid runId, CancellationToken ct) => store.FindRunAsync(runId, ct);

    public Task<IReadOnlyList<ScanStep>> StepsAsync(Guid runId, CancellationToken ct) => store.StepsAsync(runId, ct);

    /// <summary>False when the run doesn't exist. A queued run is cancelled at once; a running one at its next phase.</summary>
    public async Task<bool> CancelAsync(Guid runId, CancellationToken ct)
    {
        if (await store.FindRunAsync(runId, ct) is not { } run)
        {
            return false;
        }

        var wasQueued = run.Status == Domain.Enums.RunStatus.Queued;
        run.RequestCancel();
        await store.SaveChangesAsync(ct);
        if (wasQueued)
        {
            await events.CompletedAsync(run.Id, run.Status, ct);
        }

        return true;
    }
}
