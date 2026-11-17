using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Repositories;

public sealed class EfRepositoryStore(BlindSpotScannerDbContext db) : IRepositoryStore
{
    private const string UniqueViolation = "23505";

    public Task<Repository?> FindAsync(Guid id, CancellationToken ct) => db.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

    public Task<Repository?> FindByNameAsync(string owner, string name, CancellationToken ct) =>
        db.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Owner == owner && r.Name == name, ct);

    public async Task<bool> TryAddAsync(Repository repository, RepoProfile profile, CancellationToken ct)
    {
        db.Repositories.Add(repository);
        db.RepoProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<RepositorySummary>> ListAsync(CancellationToken ct)
    {
        var repositories = await db.Repositories.AsNoTracking().ToListAsync(ct);
        var latest = await db.ScanRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Succeeded)
            .GroupBy(r => r.RepositoryId)
            .Select(g => g.OrderByDescending(r => r.GeneratedAt)
                .Select(r => new { r.RepositoryId, r.Id, r.Score, r.EndpointsCovered, r.EndpointsTotal, r.GeneratedAt })
                .First())
            .ToDictionaryAsync(r => r.RepositoryId, ct);
        var active = await db.ScanRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Queued || r.Status == RunStatus.Running)
            .GroupBy(r => r.RepositoryId)
            .Select(g => g.OrderByDescending(r => r.QueuedAt).Select(r => new { r.RepositoryId, r.Id }).First())
            .ToDictionaryAsync(r => r.RepositoryId, r => r.Id, ct);

        return repositories
            .Select(r =>
            {
                var last = latest.GetValueOrDefault(r.Id);
                return new RepositorySummary(
                    r.Id, $"{r.Owner}/{r.Name}", r.DefaultBranch, last?.Id, last?.Score,
                    last is { EndpointsTotal: > 0 } ? (decimal)last.EndpointsCovered!.Value / last.EndpointsTotal.Value : null,
                    last?.GeneratedAt, active.TryGetValue(r.Id, out var runId) ? runId : null);
            })
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ScanRun>> RunsAsync(Guid repositoryId, CancellationToken ct) =>
        await db.ScanRuns.AsNoTracking()
            .Where(r => r.RepositoryId == repositoryId)
            .OrderByDescending(r => r.GeneratedAt ?? r.QueuedAt)
            .ToListAsync(ct);
}
