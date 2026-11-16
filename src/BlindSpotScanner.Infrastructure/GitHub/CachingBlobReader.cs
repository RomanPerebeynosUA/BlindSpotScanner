using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>
/// Serves blobs from <c>file_cache</c> and fetches misses from GitHub. A SHA's content never
/// changes, so a cached blob is valid forever and across repositories.
/// </summary>
public sealed class CachingBlobReader(BlindSpotScannerDbContext db, IGitHubClient github) : IBlobReader
{
    public async Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct)
    {
        var cached = await db.FileCache.AsNoTracking()
            .Where(f => f.BlobSha == blobSha)
            .Select(f => f.Content)
            .SingleOrDefaultAsync(ct);
        if (cached is not null)
        {
            return cached;
        }

        var content = await github.GetBlobTextAsync(owner, name, blobSha, ct);

        // PostgreSQL text can't hold NUL; such (binary) content is returned but not cached.
        if (content.Contains('\0', StringComparison.Ordinal))
        {
            return content;
        }

        var entry = new FileCacheEntry { BlobSha = blobSha, Content = content };
        db.FileCache.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another scan cached the same blob first; its content is identical by definition.
            db.Entry(entry).State = EntityState.Detached;
        }

        return content;
    }
}
