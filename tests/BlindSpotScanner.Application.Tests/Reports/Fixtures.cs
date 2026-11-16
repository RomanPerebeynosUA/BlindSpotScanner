using System.Text.Json;
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

    private static IntegrationReport Load(string name) =>
        ReportJsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
}
