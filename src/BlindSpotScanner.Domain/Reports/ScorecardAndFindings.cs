using System.Text.Json.Serialization;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Reports;

public sealed record ScoreDimension
{
    public required int Id { get; init; }
    public required string Dimension { get; init; }
    public required decimal Weight { get; init; }

    /// <summary>Null when the dimension is not scorable; always written (the schema requires it).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int? Score { get; init; }

    public required bool Scorable { get; init; }
    public Confidence? Confidence { get; init; }
    public required IReadOnlyList<string> JustificationBullets { get; init; }
    public IReadOnlyList<string>? Evidence { get; init; }
}

public sealed record ReportFinding
{
    public required string Id { get; init; }
    public required FindingKind Kind { get; init; }
    public required Severity Severity { get; init; }
    public required string StableKey { get; init; }
    public required string Headline { get; init; }
    public string? WhyItMatters { get; init; }
    public IReadOnlyList<string>? Detail { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
}

public sealed record TopRisk
{
    public required int Rank { get; init; }
    public required string Headline { get; init; }
    public required string WhyItMatters { get; init; }
    public required Severity Severity { get; init; }
    public required IReadOnlyList<string> Detail { get; init; }
    public IReadOnlyList<string>? Evidence { get; init; }
}
