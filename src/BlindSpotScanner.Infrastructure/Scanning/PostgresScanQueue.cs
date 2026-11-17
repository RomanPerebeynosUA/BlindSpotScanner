using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// The queue lives on <c>scan_runs</c> (D21). A dequeue is one statement with
/// <c>FOR UPDATE SKIP LOCKED</c>, so concurrent workers never take the same run. "Now" comes from
/// the injected clock, so lease expiry is testable.
/// </summary>
public sealed class PostgresScanQueue(BlindSpotScannerDbContext db, TimeProvider time) : IScanQueue
{
    private const int MaxAttempts = 2;
    private const string Abandoned = "worker lease expired twice; the scan was abandoned";

    public async Task EnqueueAsync(ScanRun run, CancellationToken ct)
    {
        db.ScanRuns.Add(run);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> TryDequeueAsync(Guid owner, TimeSpan lease, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var leasedUntil = now + lease;
        var taken = await db.Database.SqlQuery<Guid>($"""
            UPDATE scan_runs SET status = 'Running', attempts = attempts + 1, leased_until = {leasedUntil}, lease_owner = {owner}
            WHERE id = (
                SELECT id FROM scan_runs
                WHERE queued_at IS NOT NULL
                  AND (status = 'Queued' OR (status = 'Running' AND leased_until < {now} AND attempts < {MaxAttempts}))
                ORDER BY queued_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        return taken.Count == 0 ? null : taken[0];
    }

    public async Task<bool> HeartbeatAsync(Guid runId, Guid owner, TimeSpan lease, CancellationToken ct)
    {
        var leasedUntil = time.GetUtcNow() + lease;
        var updated = await db.Database.ExecuteSqlAsync(
            $"UPDATE scan_runs SET leased_until = {leasedUntil} WHERE id = {runId} AND status = 'Running' AND lease_owner = {owner}", ct);
        return updated == 1;
    }

    public async Task<IReadOnlyList<Guid>> FailAbandonedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        string[] reasons = [Abandoned];
        return await db.Database.SqlQuery<Guid>($"""
            UPDATE scan_runs SET status = 'Failed', failure_reasons = {reasons}
            WHERE status = 'Running' AND leased_until < {now} AND attempts >= {MaxAttempts}
            RETURNING id AS "Value"
            """).ToListAsync(ct);
    }
}
