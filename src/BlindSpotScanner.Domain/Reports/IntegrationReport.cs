using System.Text.Json.Serialization;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Reports;

/// <summary>The canonical, immutable report of one scan run (contracts/integration-report.schema.json).</summary>
public sealed record IntegrationReport
{
    public required ReportMeta Meta { get; init; }
    public required ReportInventory Inventory { get; init; }
    public required IReadOnlyList<ControllerCoverage> EndpointCoverage { get; init; }
    public required IReadOnlyList<ScoreDimension> Scorecard { get; init; }
    public required IReadOnlyList<ReportFinding> Findings { get; init; }
    public required IReadOnlyList<TopRisk> TopRisks { get; init; }
    public required ReportSummary Summary { get; init; }
    /// <summary>Null on the first scan of a repository; always written (as in the examples).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public RunDelta? Delta { get; init; }
    public required ReportValidation Validation { get; init; }
}

public sealed record ReportMeta
{
    public required string SchemaVersion { get; init; }
    public required Guid RunId { get; init; }
    public required ReportRepo Repo { get; init; }
    public required string Branch { get; init; }
    public required string CommitSha { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ModelId { get; init; }
    public required string PromptSetVersion { get; init; }
    public required int ProfileVersion { get; init; }
    public required IntegrationMappedTo IntegrationMappedTo { get; init; }
    public decimal? CostUsd { get; init; }
}

public sealed record ReportRepo
{
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }
}

public sealed record ReportInventory
{
    public required int ControllerFiles { get; init; }
    public required int EndpointsTotal { get; init; }
    public required int TestFilesTotal { get; init; }
    public required int IntegrationTestFiles { get; init; }
    /// <summary>
    /// Kept in ordinal key order so the canonical bytes don't depend on input order
    /// (jsonb sorts object keys, so a stored report comes back in a different order).
    /// </summary>
    public IReadOnlyDictionary<string, int>? Classification
    {
        get;
        init => field = value is null ? null : new SortedDictionary<string, int>(value.ToDictionary(), StringComparer.Ordinal);
    }
    public int? ReadInFull { get; init; }
    public IReadOnlyList<string>? UnverifiedReasons { get; init; }
    /// <summary>The seams detected in startup code (D34); null when they could not be detected.</summary>
    public IReadOnlyList<ReportSeam>? ProductionSeams { get; init; }
}

public sealed record ReportSeam
{
    public required SeamKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
}

public sealed record ReportValidation
{
    public required bool Passed { get; init; }
    public required string Method { get; init; }
    public required IReadOnlyList<string> Violations { get; init; }
    public int? CitationsChecked { get; init; }
    public int? CitationsResolved { get; init; }
}
