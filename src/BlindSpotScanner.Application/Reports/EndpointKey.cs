using System.Text.RegularExpressions;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Reports;

/// <summary>Endpoint identity across runs (doc 08): METHOD + route, lower-cased, with {param} names normalized to {}.</summary>
internal static partial class EndpointKey
{
    public static string Of(EndpointMethod method, string route) =>
        RouteParameter().Replace(Display(method, route).ToLowerInvariant(), "{}");

    /// <summary>The endpoint as people read it, for example "POST api/orders/{id}/cancel".</summary>
    public static string Display(EndpointMethod method, string route) => $"{method.ToString().ToUpperInvariant()} {route}";

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex RouteParameter();
}
