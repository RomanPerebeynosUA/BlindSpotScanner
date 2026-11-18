using System.Text.Json.Serialization;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>Computed by code from the report detail; never authored by the LLM (doc 07).</summary>
public sealed record ReportSummary
{
    public required StatusCounts Endpoints { get; init; }
    public required StatusCounts Controllers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required decimal? QualityScore { get; init; }

    public required decimal WeightAssessablePct { get; init; }
    public required string ScoreLabel { get; init; }
    public required string IntegrationMappedTo { get; init; }
    public int? FalsePositiveCount { get; init; }
    public int? SkippedTestCount { get; init; }
    public int? UncoveredRealSeams { get; init; }

    /// <summary>Absent in reports before schema 1.1.</summary>
    public AssertionLevels? AssertionLevels { get; init; }

    public required Verdict Verdict { get; init; }
    public required IReadOnlyList<SummaryBullet> Bullets { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? PreviousRunId { get; init; }
}

public sealed record StatusCounts
{
    public required int Total { get; init; }
    public required int Covered { get; init; }
    public required int Weak { get; init; }
    public required int Skipped { get; init; }
    public required int None { get; init; }
    public required int Unverified { get; init; }
}

public sealed record SummaryBullet
{
    public required string Label { get; init; }
    public required string Text { get; init; }
}
