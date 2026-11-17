using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Remediation;

public sealed class EfRemediationStore(BlindSpotScannerDbContext db) : IRemediationStore
{
    public async Task<Finding> EnsureFindingAsync(Guid runId, ReportFinding finding, CancellationToken ct)
    {
        if (await db.Findings.SingleOrDefaultAsync(f => f.ScanRunId == runId && f.ReportFindingId == finding.Id, ct) is { } existing)
        {
            return existing;
        }

        var row = new Finding
        {
            Id = Guid.NewGuid(),
            ScanRunId = runId,
            ReportFindingId = finding.Id,
            Kind = finding.Kind,
            Severity = finding.Severity,
            StableKey = finding.StableKey,
            Headline = finding.Headline,
            WhyItMatters = finding.WhyItMatters,
            Detail = [.. finding.Detail ?? []],
            Evidence = [.. finding.Evidence],
        };
        db.Findings.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return row;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request inserted the same finding first; use its row.
            db.Entry(row).State = EntityState.Detached;
            return await db.Findings.SingleAsync(f => f.ScanRunId == runId && f.ReportFindingId == finding.Id, ct);
        }
    }

    public Task<RemediationAction?> FindActionAsync(Guid repositoryId, string stableKey, ActionType type, CancellationToken ct) =>
        db.RemediationActions.AsNoTracking()
            .Where(a => a.RepositoryId == repositoryId && a.StableKey == stableKey && a.Type == type)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

    public async Task AddActionAsync(RemediationAction action, CancellationToken ct)
    {
        db.RemediationActions.Add(action);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RemediationAction>> ActionsAsync(Guid repositoryId, CancellationToken ct) =>
        await db.RemediationActions
            .Where(a => a.RepositoryId == repositoryId)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
