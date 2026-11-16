using System.Globalization;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <summary>
/// Doc 08: the delta between two runs, as a pure function. Reads only detail, inventory, meta and
/// summary counts/score; never bullets or an existing delta, so the assembler can call it before
/// the bullets exist.
/// </summary>
public static class RunComparer
{
    private const decimal MaxComparableDenominatorShift = 0.20m;

    public static RunDelta Compare(IntegrationReport previous, IntegrationReport current)
    {
        var comparability = Comparability(previous, current);
        return new RunDelta
        {
            BaseRunId = previous.Meta.RunId,
            BaseCommitSha = previous.Meta.CommitSha,
            BaseGeneratedAt = previous.Meta.GeneratedAt,
            Comparable = IsComparable(previous, current),
            Comparability = comparability,
            Metrics = Metrics(previous.Summary, current.Summary),
            Endpoints = Endpoints(previous, current),
            Scorecard = Scorecard(previous, current),
            Findings = new DeltaFindings
            {
                Fixed = Missing(previous.Findings.Select(f => f.StableKey), current.Findings.Select(f => f.StableKey)),
                New = Missing(current.Findings.Select(f => f.StableKey), previous.Findings.Select(f => f.StableKey)),
            },
            Risks = new DeltaRisks
            {
                Dropped = Missing(previous.TopRisks.Select(r => r.Headline), current.TopRisks.Select(r => r.Headline)),
                New = Missing(current.TopRisks.Select(r => r.Headline), previous.TopRisks.Select(r => r.Headline)),
            },
        };
    }

    private static bool IsComparable(IntegrationReport previous, IntegrationReport current)
    {
        if (previous.Meta.IntegrationMappedTo != current.Meta.IntegrationMappedTo)
        {
            return false;
        }

        var before = previous.Inventory.EndpointsTotal;
        var after = current.Inventory.EndpointsTotal;
        if (before == 0)
        {
            return after == 0;
        }

        return Math.Abs(after - before) / (decimal)before <= MaxComparableDenominatorShift;
    }

    private static List<string> Comparability(IntegrationReport previous, IntegrationReport current)
    {
        var (p, c) = (previous.Meta, current.Meta);
        var reasons = new List<string>();
        if (previous.Inventory.EndpointsTotal != current.Inventory.EndpointsTotal)
        {
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"endpoint denominator changed: {previous.Inventory.EndpointsTotal} → {current.Inventory.EndpointsTotal}"));
        }

        if (p.IntegrationMappedTo != c.IntegrationMappedTo)
        {
            reasons.Add("integrationMappedTo changed");
        }

        if (p.ProfileVersion != c.ProfileVersion)
        {
            reasons.Add(string.Create(CultureInfo.InvariantCulture, $"profile version changed {p.ProfileVersion} → {c.ProfileVersion}"));
        }

        if (p.SchemaVersion != c.SchemaVersion)
        {
            reasons.Add("schema version changed");
        }

        if (p.ModelId != c.ModelId || p.PromptSetVersion != c.PromptSetVersion)
        {
            reasons.Add("model or prompt set changed");
        }

        if (p.Branch != c.Branch)
        {
            reasons.Add("branch differs");
        }

        return reasons;
    }

    private static List<DeltaMetric> Metrics(ReportSummary previous, ReportSummary current) =>
    [
        Metric("qualityScore", previous.QualityScore, current.QualityScore, higherIsBetter: true),
        Metric("endpoints.covered", previous.Endpoints.Covered, current.Endpoints.Covered, higherIsBetter: true),
        Metric("endpoints.weak", previous.Endpoints.Weak, current.Endpoints.Weak, higherIsBetter: false),
        Metric("endpoints.none", previous.Endpoints.None, current.Endpoints.None, higherIsBetter: false),
        Metric("endpoints.unverified", previous.Endpoints.Unverified, current.Endpoints.Unverified, higherIsBetter: false),
        Metric("falsePositiveCount", previous.FalsePositiveCount, current.FalsePositiveCount, higherIsBetter: false),
    ];

    private static DeltaMetric Metric(string name, decimal? previous, decimal? current, bool higherIsBetter)
    {
        var change = current - previous;
        var direction = change switch
        {
            null or 0 => DeltaDirection.Unchanged,
            > 0 => higherIsBetter ? DeltaDirection.Improved : DeltaDirection.Regressed,
            < 0 => higherIsBetter ? DeltaDirection.Regressed : DeltaDirection.Improved,
        };
        return new DeltaMetric { Name = name, Prev = previous, Curr = current, Change = change, Direction = direction };
    }

    private static DeltaEndpoints Endpoints(IntegrationReport previous, IntegrationReport current)
    {
        var beforeList = Flatten(previous);
        var before = beforeList.ToDictionary(e => e.Key);
        var after = Flatten(current);
        var afterKeys = after.Select(e => e.Key).ToHashSet();

        var newlyCovered = new List<string>();
        var regressed = new List<string>();
        var confidenceChanged = new List<string>();
        var added = new List<string>();
        foreach (var endpoint in after)
        {
            if (!before.TryGetValue(endpoint.Key, out var old))
            {
                added.Add(endpoint.Display);
            }
            else if (old.Status != endpoint.Status && (old.Status == CoverageStatus.Unverified || endpoint.Status == CoverageStatus.Unverified))
            {
                confidenceChanged.Add(endpoint.Display);
            }
            else if (endpoint.Status == CoverageStatus.Covered && old.Status != CoverageStatus.Covered)
            {
                newlyCovered.Add(endpoint.Display);
            }
            else if (IsRegression(old.Status, endpoint.Status))
            {
                regressed.Add(endpoint.Display);
            }
        }

        return new DeltaEndpoints
        {
            NewlyCovered = newlyCovered,
            Regressed = regressed,
            Added = added,
            Removed = beforeList.Where(e => !afterKeys.Contains(e.Key)).Select(e => e.Display).ToList(),
            ConfidenceChanged = confidenceChanged,
        };
    }

    private static bool IsRegression(CoverageStatus before, CoverageStatus after) =>
        (before, after) switch
        {
            (CoverageStatus.Covered, CoverageStatus.Weak or CoverageStatus.None or CoverageStatus.Skipped) => true,
            (CoverageStatus.Weak, CoverageStatus.None or CoverageStatus.Skipped) => true,
            _ => false,
        };

    /// <summary>
    /// Endpoints in report order. An identity that occurs more than once (the same route declared in
    /// two controllers) is paired by occurrence, so the n-th occurrence matches the n-th one before.
    /// </summary>
    private static List<(string Key, string Display, CoverageStatus Status)> Flatten(IntegrationReport report)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<(string Key, string Display, CoverageStatus Status)>();
        foreach (var endpoint in report.EndpointCoverage.SelectMany(controller => controller.Endpoints))
        {
            var identity = EndpointKey.Of(endpoint.Method, endpoint.Route);
            var occurrence = seen[identity] = seen.GetValueOrDefault(identity) + 1;
            var key = occurrence == 1 ? identity : $"{identity}#{occurrence}";
            result.Add((key, EndpointKey.Display(endpoint.Method, endpoint.Route), endpoint.Status));
        }

        return result;
    }

    private static List<DeltaScorecardItem> Scorecard(IntegrationReport previous, IntegrationReport current)
    {
        // Duplicate ids are a validator violation; here the first occurrence wins so comparing never throws.
        var before = previous.Scorecard.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        return current.Scorecard
            .Where(d => before.TryGetValue(d.Id, out var old) && old.Score != d.Score)
            .OrderBy(d => d.Id)
            .Select(d => new DeltaScorecardItem
            {
                Dimension = d.Dimension,
                Prev = before[d.Id].Score,
                Curr = d.Score,
                Change = d.Score - before[d.Id].Score,
            })
            .ToList();
    }

    /// <summary>Items of <paramref name="source"/> (in its order) that are absent from <paramref name="other"/>.</summary>
    private static List<string> Missing(IEnumerable<string> source, IEnumerable<string> other)
    {
        var present = other.ToHashSet(StringComparer.Ordinal);
        return source.Where(item => !present.Contains(item)).ToList();
    }
}
