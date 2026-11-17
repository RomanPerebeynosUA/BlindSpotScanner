using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Scanning;

/// <summary>
/// The queue lives on <c>scan_runs</c> (D21). A dequeue is one statement with
/// <c>FOR UPDATE SKIP LOCKED</c>, so concurrent workers never take the same run. Lease times use
/// the database clock (<c>now()</c>), so workers on hosts with skewed clocks agree on expiry.
/// </summary>
public sealed class PostgresScanQueue(BlindSpotScannerDbContext db) : IScanQueue
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
        var taken = await db.Database.SqlQuery<Guid>($"""
            UPDATE scan_runs SET status = 'Running', attempts = attempts + 1, leased_until = now() + {lease}, lease_owner = {owner}
            WHERE id = (
                SELECT id FROM scan_runs
                WHERE queued_at IS NOT NULL
                  AND (status = 'Queued' OR (status = 'Running' AND leased_until < now() AND attempts < {MaxAttempts}))
                ORDER BY queued_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        return taken.Count == 0 ? null : taken[0];
    }

    public async Task<bool> HeartbeatAsync(Guid runId, Guid owner, TimeSpan lease, CancellationToken ct)
    {
        var updated = await db.Database.ExecuteSqlAsync(
            $"UPDATE scan_runs SET leased_until = now() + {lease} WHERE id = {runId} AND status = 'Running' AND lease_owner = {owner}", ct);
        return updated == 1;
    }

    /// <summary>An abandoned run whose cancel was requested ends Cancelled (that was the user's intent); others Fail.</summary>
    public async Task<IReadOnlyList<(Guid RunId, RunStatus Status)>> FailAbandonedAsync(CancellationToken ct)
    {
        var cancelled = await db.Database.SqlQuery<Guid>($"""
            UPDATE scan_runs SET status = 'Cancelled'
            WHERE status = 'Running' AND leased_until < now() AND attempts >= {MaxAttempts} AND cancel_requested
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        string[] reasons = [Abandoned];
        var failed = await db.Database.SqlQuery<Guid>($"""
            UPDATE scan_runs SET status = 'Failed', failure_reasons = {reasons}
            WHERE status = 'Running' AND leased_until < now() AND attempts >= {MaxAttempts}
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        return [.. cancelled.Select(id => (id, RunStatus.Cancelled)), .. failed.Select(id => (id, RunStatus.Failed))];
    }
}
