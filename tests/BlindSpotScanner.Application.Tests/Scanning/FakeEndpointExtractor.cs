using BlindSpotScanner.Application.Scanning;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>Returns a canned result per path.</summary>
internal sealed class FakeEndpointExtractor(IReadOnlyDictionary<string, ExtractionResult> resultByPath) : IEndpointExtractor
{
    public ExtractionResult Extract(string path, string source) => resultByPath[path];
}
