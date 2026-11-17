using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

public sealed record RunHeadline(Guid RunId, string Branch, string CommitSha, DateTimeOffset GeneratedAt, decimal? Score);

/// <param name="Swapped">The request named the newer run as base; the comparison runs old → new (D23).</param>
/// <param name="AdHoc">True when the pair isn't adjacent, so the delta was computed live (doc 08).</param>
public sealed record Comparison(RunHeadline Base, RunHeadline Target, bool Swapped, bool AdHoc, RunDelta Delta);

public enum CompareStatus
{
    Compared,
    NotFound,
    SameRun,
    NoReport,
    DifferentRepositories,
}

public sealed record CompareResult(CompareStatus Status, Comparison? Comparison = null);

/// <summary>
/// Doc 08 ad-hoc compare: adjacent runs return the delta stored on the newer report; any other pair
/// is computed live with <see cref="RunComparer"/>. The UI never computes a delta itself.
/// </summary>
public sealed class RunComparison(IScanRequestStore runs)
{
    public async Task<CompareResult> CompareAsync(Guid baseRunId, Guid targetRunId, CancellationToken ct)
    {
        if (baseRunId == targetRunId)
        {
            return new CompareResult(CompareStatus.SameRun);
        }

        var first = await runs.FindRunAsync(baseRunId, ct);
        var second = await runs.FindRunAsync(targetRunId, ct);
        if (first is null || second is null)
        {
            return new CompareResult(CompareStatus.NotFound);
        }

        if (first is not { Status: RunStatus.Succeeded, ReportJson: { } firstJson } || second is not { Status: RunStatus.Succeeded, ReportJson: { } secondJson })
        {
            return new CompareResult(CompareStatus.NoReport);
        }

        if (first.RepositoryId != second.RepositoryId)
        {
            return new CompareResult(CompareStatus.DifferentRepositories);
        }

        var (a, b) = (ReportJsonSerializer.Deserialize(firstJson), ReportJsonSerializer.Deserialize(secondJson));

        // Always old → new, so "newly covered" and "regressed" mean progress over time (D23).
        var swapped = (a.Meta.GeneratedAt, a.Meta.RunId).CompareTo((b.Meta.GeneratedAt, b.Meta.RunId)) > 0;
        var (older, newer) = swapped ? (b, a) : (a, b);
        var adjacent = newer.Delta?.BaseRunId == older.Meta.RunId;
        var delta = adjacent ? newer.Delta! : RunComparer.Compare(older, newer);
        return new CompareResult(CompareStatus.Compared, new Comparison(Headline(older), Headline(newer), swapped, !adjacent, delta));
    }

    private static RunHeadline Headline(IntegrationReport report) =>
        new(report.Meta.RunId, report.Meta.Branch, report.Meta.CommitSha, report.Meta.GeneratedAt, report.Summary.QualityScore);
}
