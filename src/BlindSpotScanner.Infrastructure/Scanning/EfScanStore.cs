using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// The orchestrator's store over EF Core. The run stays tracked, so saving it writes only the
/// columns the scan changed; a cancel request written meanwhile by the API is not overwritten.
/// </summary>
public sealed class EfScanStore(BlindSpotScannerDbContext db) : IScanStore
{
    public async Task<ScanJob?> LoadAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ScanRuns.SingleOrDefaultAsync(r => r.Id == runId, ct);
        var repository = run is null ? null : await db.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Id == run.RepositoryId, ct);
        if (run is null || repository is null)
        {
            return null;
        }

        // The version the run was queued with: an edit while it waits must not change what it measures.
        var profile = await db.RepoProfiles.AsNoTracking()
            .SingleOrDefaultAsync(p => p.RepositoryId == repository.Id && p.Version == run.ProfileVersion, ct);
        var branch = run.RequestedBranch ?? repository.DefaultBranch;
        var previousJson = await db.ScanRuns.AsNoTracking()
            .Where(r => r.RepositoryId == repository.Id && r.Status == RunStatus.Succeeded && r.Branch == branch && r.Id != runId)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => r.ReportJson)
            .FirstOrDefaultAsync(ct);
        return new ScanJob(run, repository, profile, previousJson is null ? null : ReportJsonSerializer.Deserialize(previousJson));
    }

    public async Task SaveStepAsync(ScanStep step, CancellationToken ct)
    {
        db.ScanSteps.Add(step);
        await db.SaveChangesAsync(ct);
    }

    public Task ClearStepsAsync(Guid runId, CancellationToken ct) =>
        db.ScanSteps.Where(s => s.ScanRunId == runId).ExecuteDeleteAsync(ct);

    public Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct) =>
        db.ScanRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => r.CancelRequested).SingleAsync(ct);

    public async Task<bool> SaveAsync(ScanRun run, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
