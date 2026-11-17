using BlindSpotScanner.Application.Scanning;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>Returns canned facts per source text.</summary>
internal sealed class FakeTestSyntaxReader(IReadOnlyDictionary<string, TestFileFacts> factsBySource) : ITestSyntaxReader
{
    public TestFileFacts Read(string source) => factsBySource[source];
}
