using System.Globalization;
using System.Text.Json;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <summary>
/// Docs 06 and 07: derives every computed part of a report from its detail, as a pure function.
/// The LLM never writes these numbers (principle 2).
/// </summary>
public static class ReportAssembler
{
    private const decimal LowConfidenceUnverifiedShare = 0.25m;

    private const int AssertionStrength = 5;

    private const string NotAssessed = "N/A — insufficient input";

    /// <summary>D27 points per assertion level L0…L5.</summary>
    private static readonly int[] LevelPoints = [1, 2, 4, 6, 8, 10];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The returned report carries a placeholder <c>validation</c> section (not passed, no violations);
    /// the caller writes the real one after <see cref="ReportValidator"/> has run.
    /// </summary>
    public static IntegrationReport Assemble(ReportDetail detail, IntegrationReport? previous)
    {
        var coverage = detail.EndpointCoverage.Select(c => c with { Status = Rollup(c.Endpoints) }).ToList();
        var risks = detail.TopRisks.Select((risk, index) => risk with { Rank = index + 1 }).ToList();
        var endpoints = Tally(coverage.SelectMany(c => c.Endpoints).Select(e => e.Status));
        var scorecard = detail.TestAssertions is { } tests
            ? detail.Scorecard.Select(d => d.Id == AssertionStrength ? DimensionFive(tests) : d).ToList()
            : detail.Scorecard;
        var (score, assessablePct) = Score(scorecard);
        var falsePositives = detail.Findings.Count(f => f.Kind == FindingKind.FalsePositive);
        var verdict = VerdictFor(endpoints);
        var mappedTo = WireName(detail.Meta.IntegrationMappedTo);

        var summary = new ReportSummary
        {
            Endpoints = endpoints,
            Controllers = Tally(coverage.Select(c => c.Status)),
            QualityScore = score,
            WeightAssessablePct = assessablePct,
            ScoreLabel = ScoreLabel(score, assessablePct, detail.Meta.IntegrationMappedTo, mappedTo),
            IntegrationMappedTo = mappedTo,
            FalsePositiveCount = falsePositives,
            SkippedTestCount = detail.Findings.Count(f => f.Kind == FindingKind.SkippedTest),
            UncoveredRealSeams = detail.Findings.Count(f => f.Kind == FindingKind.UncoveredSeam),
            AssertionLevels = detail.TestAssertions is { } rows ? Levels(rows) : null,
            Verdict = verdict,
            Bullets =
            [
                OverallBullet(verdict, endpoints),
                Bullet("Coverage", $"{endpoints.None} operation(s) have no integration test; {endpoints.Unverified} could not be checked."),
                TestQualityBullet(score, assessablePct, falsePositives),
            ],
            PreviousRunId = previous?.Meta.RunId,
        };

        var report = new IntegrationReport
        {
            Meta = detail.Meta,
            Inventory = detail.Inventory,
            EndpointCoverage = coverage,
            Scorecard = scorecard,
            Findings = detail.Findings,
            TopRisks = risks,
            TestAssertions = detail.TestAssertions,
            Summary = summary,
            Delta = null,
            Validation = new ReportValidation { Passed = false, Method = "script", Violations = [] },
        };

        var delta = previous is null ? null : RunComparer.Compare(previous, report);
        return report with
        {
            Summary = summary with { Bullets = [.. summary.Bullets, SinceLastScanBullet(delta, previous?.Summary.QualityScore, score)] },
            Delta = delta,
        };
    }

    /// <summary>Doc 06 controller rollup.</summary>
    private static CoverageStatus Rollup(IReadOnlyList<EndpointResult> endpoints)
    {
        if (endpoints.Count == 0 || endpoints.All(e => e.Status == CoverageStatus.None))
        {
            return CoverageStatus.None;
        }

        if (endpoints.All(e => e.Status == CoverageStatus.Covered))
        {
            return CoverageStatus.Covered;
        }

        var anyUnverified = endpoints.Any(e => e.Status == CoverageStatus.Unverified);
        var anyCoveredOrWeak = endpoints.Any(e => e.Status is CoverageStatus.Covered or CoverageStatus.Weak);
        return anyUnverified && !anyCoveredOrWeak ? CoverageStatus.Unverified : CoverageStatus.Weak;
    }

    private static StatusCounts Tally(IEnumerable<CoverageStatus> statuses)
    {
        var list = statuses.ToList();
        return new StatusCounts
        {
            Total = list.Count,
            Covered = list.Count(s => s == CoverageStatus.Covered),
            Weak = list.Count(s => s == CoverageStatus.Weak),
            Skipped = list.Count(s => s == CoverageStatus.Skipped),
            None = list.Count(s => s == CoverageStatus.None),
            Unverified = list.Count(s => s == CoverageStatus.Unverified),
        };
    }

    /// <summary>D27: dimension 5 from the per-test levels, with the rubric's name and weight.</summary>
    private static ScoreDimension DimensionFive(IReadOnlyList<TestAssertion> tests)
    {
        var (_, name, weight) = Rubric.Dimensions.Single(d => d.Id == AssertionStrength);
        var known = tests.Where(t => t.Level is not null).ToList();
        if (known.Count == 0)
        {
            return new ScoreDimension
            {
                Id = AssertionStrength,
                Dimension = name,
                Weight = weight,
                Score = null,
                Scorable = false,
                Confidence = Confidence.Low,
                JustificationBullets = [$"What is tested: {NotAssessed}", $"What is missing: {NotAssessed}", "Evidence: none", $"Why this score: {NotAssessed}"],
                Evidence = [],
            };
        }

        var mean = (decimal)known.Sum(t => LevelPoints[t.Level!.Value]) / known.Count;
        var evidence = known.OrderBy(t => LevelPoints[t.Level!.Value]).ThenBy(t => t.Test, StringComparer.Ordinal).Take(3).Select(t => t.Test).ToList();
        var unknown = tests.Count - known.Count;
        return new ScoreDimension
        {
            Id = AssertionStrength,
            Dimension = name,
            Weight = weight,
            Score = (int)Math.Round(mean, 0, MidpointRounding.AwayFromZero),
            Scorable = true,
            Confidence = unknown == 0 ? Confidence.High : Confidence.Med,
            JustificationBullets =
            [
                string.Create(Invariant, $"What is tested: {known.Count(t => t.Level >= 3)} of {known.Count} integration tests check response values or saved state (L3 or higher)."),
                string.Create(Invariant, $"What is missing: {known.Count(t => t.Level <= 2)} test(s) check only the status code or shape (L0–L2); {unknown} could not be classified."),
                $"Evidence: {string.Join(", ", evidence)}",
                string.Create(Invariant, $"Why this score: the mean of the per-test points (L0 1, L1 2, L2 4, L3 6, L4 8, L5 10) over {known.Count} test(s) is {mean:0.0}."),
            ],
            Evidence = evidence,
        };
    }

    private static AssertionLevels Levels(IReadOnlyList<TestAssertion> tests) => new()
    {
        L0 = tests.Count(t => t.Level == 0),
        L1 = tests.Count(t => t.Level == 1),
        L2 = tests.Count(t => t.Level == 2),
        L3 = tests.Count(t => t.Level == 3),
        L4 = tests.Count(t => t.Level == 4),
        L5 = tests.Count(t => t.Level == 5),
        Unknown = tests.Count(t => t.Level is null),
    };

    /// <summary>Doc 06 rollup: weighted mean over scorable dimensions, plus the assessable weight share.</summary>
    private static (decimal? Score, decimal AssessablePct) Score(IReadOnlyList<ScoreDimension> scorecard)
    {
        var scored = scorecard.Where(d => d.Scorable && d.Score is not null).ToList();
        var totalWeight = scorecard.Sum(d => d.Weight);
        var scoredWeight = scored.Sum(d => d.Weight);

        decimal? score = scoredWeight == 0
            ? null
            : Math.Round(scored.Sum(d => d.Score!.Value * d.Weight) / scoredWeight, 1, MidpointRounding.AwayFromZero);
        var assessablePct = totalWeight == 0
            ? 0m
            : Math.Round(scoredWeight / totalWeight * 100m, 0, MidpointRounding.AwayFromZero);
        return (score, assessablePct);
    }

    /// <summary>Doc 06 verdict ladder over covered share = (covered + 0.5 × weak) / (total − unverified).</summary>
    private static Verdict VerdictFor(StatusCounts endpoints)
    {
        var assessed = endpoints.Total - endpoints.Unverified;
        if (assessed == 0)
        {
            return Verdict.Weak;
        }

        var share = (endpoints.Covered + (0.5m * endpoints.Weak)) / assessed;
        return share switch
        {
            >= 0.80m => Verdict.Strong,
            >= 0.60m => Verdict.Moderate,
            >= 0.35m => Verdict.Partial,
            _ => Verdict.Weak,
        };
    }

    private static string ScoreLabel(decimal? score, decimal assessablePct, IntegrationMappedTo mapping, string mappedTo)
    {
        var label = score is null
            ? "Not scorable"
            : string.Create(Invariant, $"{score:0.0} / 10 across scored dimensions ({assessablePct:0}% of weight assessable)");
        return mapping == IntegrationMappedTo.RealSeam ? label : $"{label} — not comparable to real-seam scores ({mappedTo})";
    }

    private static SummaryBullet OverallBullet(Verdict verdict, StatusCounts endpoints)
    {
        // The verdict enum has no "unknown" value (it stays weak), but the sentence must not claim
        // testing is weak when nothing could be assessed (D15, principle 1).
        if (endpoints.Total == 0)
        {
            return Bullet("Overall", "Integration testing could not be assessed: no API operations were found.");
        }

        if (endpoints.Unverified == endpoints.Total)
        {
            return Bullet("Overall", string.Create(Invariant,
                $"Integration testing could not be assessed: none of the {endpoints.Total} API operations could be checked."));
        }

        var text = string.Create(Invariant,
            $"Integration testing is {verdict.ToString().ToLowerInvariant()}: {endpoints.Covered} of {endpoints.Total} API operations are properly tested; {endpoints.Weak} more lightly tested.");
        var lowConfidence = endpoints.Total > 0 && (decimal)endpoints.Unverified / endpoints.Total > LowConfidenceUnverifiedShare;
        return Bullet("Overall", lowConfidence ? $"{text} (low confidence: many endpoints unverified)" : text);
    }

    private static SummaryBullet TestQualityBullet(decimal? score, decimal assessablePct, int falsePositives) =>
        Bullet("Test quality", score is null
            ? "Test quality could not be scored."
            : string.Create(Invariant,
                $"Test quality scores {score:0.0} out of 10 ({assessablePct:0}% of the rubric could be assessed); {falsePositives} test(s) only check that the request succeeded."));

    private static SummaryBullet SinceLastScanBullet(RunDelta? delta, decimal? previousScore, decimal? score)
    {
        if (delta is null)
        {
            return Bullet("Since last scan", "First scan of this repository — no comparison yet.");
        }

        var text = string.Create(Invariant,
            $"Since {delta.BaseGeneratedAt.UtcDateTime.ToString("dd MMM yyyy", Invariant)}: score {FormatScore(previousScore)} → {FormatScore(score)}; {delta.Endpoints.NewlyCovered.Count} operation(s) newly covered, {delta.Endpoints.Regressed.Count} regressed.");
        return Bullet("Since last scan", delta.Comparable ? text : $"{text} (not like-for-like — see comparability notes)");
    }

    private static string FormatScore(decimal? score) => score?.ToString("0.0", Invariant) ?? "N/A";

    private static SummaryBullet Bullet(string label, string text) => new() { Label = label, Text = text };

    /// <summary>The schema's wire value of an enum (for example "real-seam"), from the canonical options.</summary>
    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, ReportJsonSerializer.Options).Trim('"');
}
