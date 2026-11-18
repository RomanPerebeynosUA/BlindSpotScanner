using System.Text.Json.Serialization;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>Computed by RunComparer from (previous run, this run); never authored by the LLM (doc 08).</summary>
public sealed record RunDelta
{
    public required Guid BaseRunId { get; init; }
    public required string BaseCommitSha { get; init; }
    public required DateTimeOffset BaseGeneratedAt { get; init; }
    public required bool Comparable { get; init; }
    public required IReadOnlyList<string> Comparability { get; init; }

    /// <summary>
    /// False when the quality score and dimension 5 were measured differently (D27); endpoint deltas
    /// follow <see cref="Comparable"/>. Absent in deltas stored before schema 1.1, which read as true.
    /// </summary>
    public bool ScoreComparable { get; init; } = true;

    public required IReadOnlyList<DeltaMetric> Metrics { get; init; }
    public required DeltaEndpoints Endpoints { get; init; }
    public required IReadOnlyList<DeltaScorecardItem> Scorecard { get; init; }
    public required DeltaFindings Findings { get; init; }
    public required DeltaRisks Risks { get; init; }
}

public sealed record DeltaMetric
{
    public required string Name { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required decimal? Prev { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required decimal? Curr { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required decimal? Change { get; init; }

    public required DeltaDirection Direction { get; init; }
}

public sealed record DeltaEndpoints
{
    public required IReadOnlyList<string> NewlyCovered { get; init; }
    public required IReadOnlyList<string> Regressed { get; init; }
    public required IReadOnlyList<string> Added { get; init; }
    public required IReadOnlyList<string> Removed { get; init; }
    public IReadOnlyList<string>? ConfidenceChanged { get; init; }
}

public sealed record DeltaScorecardItem
{
    public required string Dimension { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? Prev { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? Curr { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? Change { get; init; }
}

public sealed record DeltaFindings
{
    public required IReadOnlyList<string> Fixed { get; init; }
    public required IReadOnlyList<string> New { get; init; }
}

public sealed record DeltaRisks
{
    public required IReadOnlyList<string> Dropped { get; init; }
    public required IReadOnlyList<string> New { get; init; }
}
