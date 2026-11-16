using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Entities;

/// <summary>A report finding stored relationally, so remediation actions can refer to it.</summary>
public class Finding
{
    public required Guid Id { get; init; }
    public required Guid ScanRunId { get; init; }

    /// <summary>The finding's id inside the report JSON (for example "f2").</summary>
    public required string ReportFindingId { get; init; }

    public required FindingKind Kind { get; init; }
    public required Severity Severity { get; init; }

    /// <summary>Deterministic key, for example "uncovered-endpoint:POST /orders/{id}/cancel"; matches findings across runs.</summary>
    public required string StableKey { get; init; }

    public required string Headline { get; init; }
    public string? WhyItMatters { get; init; }
    public required string[] Detail { get; init; }
    public required string[] Evidence { get; init; }
}
