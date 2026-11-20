using BlindSpotScanner.Application.Scanning;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>Returns a canned result per path; a path without canned seams registers none.</summary>
internal sealed class FakeEndpointExtractor(
    IReadOnlyDictionary<string, ExtractionResult> resultByPath,
    IReadOnlyDictionary<string, IReadOnlyList<ProductionSeam>>? seamsByPath = null) : IEndpointExtractor
{
    public ExtractionResult Extract(string path, string source) => resultByPath[path];

    public IReadOnlyList<ProductionSeam> DetectSeams(string path, string source) =>
        seamsByPath?.GetValueOrDefault(path) ?? [];
}
