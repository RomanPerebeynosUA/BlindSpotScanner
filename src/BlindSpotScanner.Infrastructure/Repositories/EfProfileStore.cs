using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Repositories;

public sealed class EfProfileStore(BlindSpotScannerDbContext db) : IProfileStore
{
    private const string UniqueViolation = "23505";

    public Task<RepoProfile?> GetAsync(Guid repositoryId, int? version, CancellationToken ct) =>
        db.RepoProfiles.AsNoTracking()
            .Where(p => p.RepositoryId == repositoryId && (version == null || p.Version == version))
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<int>> VersionsAsync(Guid repositoryId, CancellationToken ct) =>
        await db.RepoProfiles.Where(p => p.RepositoryId == repositoryId).OrderByDescending(p => p.Version).Select(p => p.Version).ToListAsync(ct);

    public async Task<bool> TryAddAsync(RepoProfile profile, CancellationToken ct)
    {
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
}
