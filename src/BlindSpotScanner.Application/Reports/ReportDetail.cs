using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <summary>
/// The part of a report authored by the pipeline and the LLM. Everything else (summary, controller
/// rollups, risk ranks, delta) is computed from it by <see cref="ReportAssembler"/>.
/// </summary>
public sealed record ReportDetail
{
    public required ReportMeta Meta { get; init; }
    public required ReportInventory Inventory { get; init; }
    public required IReadOnlyList<ControllerCoverage> EndpointCoverage { get; init; }
    public required IReadOnlyList<ScoreDimension> Scorecard { get; init; }
    public required IReadOnlyList<ReportFinding> Findings { get; init; }
    public required IReadOnlyList<TopRisk> TopRisks { get; init; }
    public IReadOnlyList<TestAssertion>? TestAssertions { get; init; }

    public static ReportDetail From(IntegrationReport report) => new()
    {
        Meta = report.Meta,
        Inventory = report.Inventory,
        EndpointCoverage = report.EndpointCoverage,
        Scorecard = report.Scorecard,
        Findings = report.Findings,
        TopRisks = report.TopRisks,
        TestAssertions = report.TestAssertions,
    };
}
