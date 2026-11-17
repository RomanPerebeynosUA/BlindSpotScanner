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

    private const int TrendLength = 12;

    /// <summary>The unique (Owner, Name) index; any other violation is a bug, not "already registered".</summary>
    private const string NameIndex = "ix_repositories_owner_name";

    public Task<Repository?> FindAsync(Guid id, CancellationToken ct) => db.Repositories.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

    public Task<Repository?> FindByRunAsync(Guid runId, CancellationToken ct) =>
        db.Repositories.AsNoTracking().Where(r => db.ScanRuns.Any(s => s.Id == runId && s.RepositoryId == r.Id)).SingleOrDefaultAsync(ct);

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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: NameIndex })
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
            .Select(g => g.OrderByDescending(r => r.GeneratedAt).ThenByDescending(r => r.Id)
                .Select(r => new { r.RepositoryId, r.Id, r.Score, r.EndpointsCovered, r.EndpointsTotal, r.GeneratedAt })
                .First())
            .ToDictionaryAsync(r => r.RepositoryId, ct);
        var active = await db.ScanRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Queued || r.Status == RunStatus.Running)
            .GroupBy(r => r.RepositoryId)
            .Select(g => g.OrderByDescending(r => r.QueuedAt).ThenByDescending(r => r.Id).Select(r => new { r.RepositoryId, r.Id }).First())
            .ToDictionaryAsync(r => r.RepositoryId, r => r.Id, ct);
        // Headline columns only (no report JSON), grouped here: a few rows per repository.
        var succeeded = await db.ScanRuns.AsNoTracking()
            .Where(r => r.Status == RunStatus.Succeeded && r.GeneratedAt != null)
            .Select(r => new { r.RepositoryId, r.Id, GeneratedAt = r.GeneratedAt!.Value, r.Score, r.EndpointsCovered, r.EndpointsTotal })
            .ToListAsync(ct);
        var trends = succeeded
            .GroupBy(r => r.RepositoryId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TrendPoint>)g.OrderByDescending(r => r.GeneratedAt).ThenByDescending(r => r.Id).Take(TrendLength).Reverse()
                    .Select(r => new TrendPoint(r.Id, r.GeneratedAt, r.Score, r.EndpointsTotal is > 0 ? (decimal)r.EndpointsCovered!.Value / r.EndpointsTotal.Value : null))
                    .ToList());
        var newest = await db.ScanRuns.AsNoTracking()
            .GroupBy(r => r.RepositoryId)
            .Select(g => g.OrderByDescending(r => r.GeneratedAt ?? r.QueuedAt).ThenByDescending(r => r.Id).Select(r => new { r.RepositoryId, r.Status }).First())
            .ToDictionaryAsync(r => r.RepositoryId, r => r.Status, ct);

        return repositories
            .Select(r =>
            {
                var last = latest.GetValueOrDefault(r.Id);
                return new RepositorySummary(
                    r.Id, $"{r.Owner}/{r.Name}", r.DefaultBranch, last?.Id, last?.Score,
                    last is { EndpointsTotal: > 0 } ? (decimal)last.EndpointsCovered!.Value / last.EndpointsTotal.Value : null,
                    last?.GeneratedAt, active.TryGetValue(r.Id, out var runId) ? runId : null,
                    newest.TryGetValue(r.Id, out var status) ? status : null,
                    trends.GetValueOrDefault(r.Id));
            })
            .OrderBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<ScanRun>> RunsAsync(Guid repositoryId, CancellationToken ct) =>
        await db.ScanRuns.AsNoTracking()
            .Where(r => r.RepositoryId == repositoryId)
            .OrderByDescending(r => r.GeneratedAt ?? r.QueuedAt)
            .ThenByDescending(r => r.Id) // a fixed order for equal timestamps, like the trend
            .ToListAsync(ct);
}
