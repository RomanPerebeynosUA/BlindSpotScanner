using System.Text.Json;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Tests.Reports;

/// <summary>The blueprint examples (golden files) and helpers to derive mutated copies of them.</summary>
internal static class Fixtures
{
    public static IntegrationReport Current => Load("sample-report.json");

    public static IntegrationReport Previous => Load("sample-report-previous.json");

    public static string Canonical<T>(T value) => JsonSerializer.Serialize(value, ReportJsonSerializer.Options);

    /// <summary>Replaces the endpoint identified by method + route (exact text) in every controller.</summary>
    public static IntegrationReport WithEndpoint(this IntegrationReport report, string method, string route, Func<EndpointResult, EndpointResult> change) =>
        report with
        {
            EndpointCoverage = report.EndpointCoverage
                .Select(controller => controller with
                {
                    Endpoints = controller.Endpoints
                        .Select(e => string.Equals(e.Method.ToString(), method, StringComparison.OrdinalIgnoreCase) && e.Route == route ? change(e) : e)
                        .ToList(),
                })
                .ToList(),
        };

    /// <summary>A detail whose endpoints are one controller with the given status counts (routes api/e0, api/e1, …).</summary>
    public static ReportDetail WithStatusCounts(this ReportDetail detail, int covered, int weak, int none, int unverified)
    {
        var statuses = Enumerable.Repeat(CoverageStatus.Covered, covered)
            .Concat(Enumerable.Repeat(CoverageStatus.Weak, weak))
            .Concat(Enumerable.Repeat(CoverageStatus.None, none))
            .Concat(Enumerable.Repeat(CoverageStatus.Unverified, unverified));
        var endpoints = statuses
            .Select((status, i) => new EndpointResult
            {
                Method = EndpointMethod.Get,
                Route = $"api/e{i}",
                Status = status,
                Verified = status != CoverageStatus.Unverified,
                Evidence = status is CoverageStatus.Covered or CoverageStatus.Weak ? ["tests/Api/ETests.cs::E_works"] : [],
            })
            .ToList();
        var controller = new ControllerCoverage { Controller = "EController", SourcePath = "src/Api/EController.cs", Status = CoverageStatus.Weak, Endpoints = endpoints };
        return detail with { EndpointCoverage = [controller], Inventory = detail.Inventory with { EndpointsTotal = endpoints.Count } };
    }

    private static IntegrationReport Load(string name) =>
        ReportJsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
}
