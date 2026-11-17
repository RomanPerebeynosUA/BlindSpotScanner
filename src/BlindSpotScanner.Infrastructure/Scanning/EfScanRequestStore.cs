using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Scanning;

public sealed class EfScanRequestStore(BlindSpotScannerDbContext db) : IScanRequestStore
{
    public Task<bool> RepositoryExistsAsync(Guid repositoryId, CancellationToken ct) => db.Repositories.AnyAsync(r => r.Id == repositoryId, ct);

    public Task<int?> LatestProfileVersionAsync(Guid repositoryId, CancellationToken ct) =>
        db.RepoProfiles.Where(p => p.RepositoryId == repositoryId).MaxAsync(p => (int?)p.Version, ct);

    public Task<ScanRun?> FindRunAsync(Guid runId, CancellationToken ct) => db.ScanRuns.SingleOrDefaultAsync(r => r.Id == runId, ct);

    public async Task<IReadOnlyList<ScanStep>> StepsAsync(Guid runId, CancellationToken ct) =>
        await db.ScanSteps.AsNoTracking().Where(s => s.ScanRunId == runId).OrderBy(s => s.StartedAt).ToListAsync(ct);

    public async Task<bool> SaveChangesAsync(CancellationToken ct)
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

    public Task ReloadAsync(ScanRun run, CancellationToken ct) => db.Entry(run).ReloadAsync(ct);
}
