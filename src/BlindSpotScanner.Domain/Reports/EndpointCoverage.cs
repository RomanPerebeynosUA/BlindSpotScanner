using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Domain.Reports;

public sealed record ControllerCoverage
{
    public required string Controller { get; init; }
    public required string SourcePath { get; init; }
    public string? RoutePrefix { get; init; }
    public required CoverageStatus Status { get; init; }
    public required IReadOnlyList<EndpointResult> Endpoints { get; init; }
}

public sealed record EndpointResult
{
    public required EndpointMethod Method { get; init; }
    public required string Route { get; init; }
    public required CoverageStatus Status { get; init; }
    public required bool Verified { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public bool? FalsePositive { get; init; }
    public string? Notes { get; init; }
}
