using BlindSpotScanner.Application.Scanning;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>Returns a canned result per path; a path without canned seams registers none.</summary>
internal sealed class FakeEndpointExtractor(
    IReadOnlyDictionary<string, ExtractionResult> resultByPath,
    IReadOnlyDictionary<string, IReadOnlyList<ProductionSeam>>? seamsByPath = null) : IEndpointExtractor
{
    public IReadOnlyList<ProductionSeam> DetectSeams(string path, string source) =>
        seamsByPath?.GetValueOrDefault(path) ?? [];

    public Dictionary<string, bool> KebabCaseByPath { get; } = [];

    public bool UsesKebabCaseRouteTokens(string path, string source) => source.Contains("KEBAB", StringComparison.Ordinal);

    public ExtractionResult Extract(string path, string source, bool kebabCaseRouteTokens)
    {
        KebabCaseByPath[path] = kebabCaseRouteTokens;
        return resultByPath[path];
    }
}
