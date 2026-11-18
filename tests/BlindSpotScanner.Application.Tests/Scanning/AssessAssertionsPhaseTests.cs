using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class AssessAssertionsPhaseTests
{
    private static readonly RunContext Run = new("acme", "shop", "main", new string('a', 40), "https://github.com/acme/shop");

    private sealed class FakeAssertionReader(IReadOnlyDictionary<string, IReadOnlyList<TestAssertionFacts>> factsBySource) : ITestAssertionReader
    {
        public IReadOnlyList<TestAssertionFacts> Read(string source) => factsBySource[source];
    }

    private static Task<PhaseResult<IReadOnlyList<TestAssertion>>> AssessAsync(string path, TestAssertionFacts facts)
    {
        var blobs = new FakeBlobReader(new Dictionary<string, string> { ["sha1"] = "source" });
        var reader = new FakeAssertionReader(new Dictionary<string, IReadOnlyList<TestAssertionFacts>> { ["source"] = [facts] });
        var tests = new TestClassification([new ClassifiedTestFile(path, "sha1", TestFileKind.Integration, "glob", [facts.TestMethod])]);
        return new AssessAssertionsPhase(blobs, reader).AssessAsync(Run, tests, CancellationToken.None);
    }

    [Fact]
    public async Task AssessAsync_CitationUsesPathAndQualifiedMethod()
    {
        // Arrange
        var facts = new TestAssertionFacts("BooksTests.Get_works", 3, null, false, [], [], true);

        // Act
        var result = await AssessAsync("tests/A.IntegrationTests/BooksTests.cs", facts);

        // Assert
        result.Value.Should().ContainSingle().Which.Test.Should().Be("tests/A.IntegrationTests/BooksTests.cs::BooksTests.Get_works");
    }

    [Fact]
    public async Task AssessAsync_KnownLevel_SourceIsCode()
    {
        // Arrange
        var facts = new TestAssertionFacts("BooksTests.Get_works", 3, null, false, [], [], true);

        // Act
        var result = await AssessAsync("tests/A.IntegrationTests/BooksTests.cs", facts);

        // Assert
        result.Value.Single().LevelSource.Should().Be(LevelSource.Code);
    }

    [Fact]
    public async Task AssessAsync_UnknownLevel_StaysUnknown()
    {
        // Arrange
        var facts = new TestAssertionFacts("BooksTests.Get_works", null, null, false, [], [], true);

        // Act
        var result = await AssessAsync("tests/A.IntegrationTests/BooksTests.cs", facts);

        // Assert
        result.Value.Single().Should().BeEquivalentTo(new { Level = (int?)null, LevelSource = LevelSource.Unknown });
    }

    [Fact]
    public async Task AssessAsync_ResolvedFields_ComputesUncheckedFields()
    {
        // Arrange
        var facts = new TestAssertionFacts("OrdersTests.Place", 3, null, false, ["lines[].bookId", "total"], ["Total"], true);

        // Act
        var result = await AssessAsync("tests/A.IntegrationTests/OrdersTests.cs", facts);

        // Assert
        result.Value.Single().UncheckedFields.Should().Equal("lines[].bookId");
    }

    [Fact]
    public async Task AssessAsync_UnresolvedFields_LeavesUncheckedEmpty()
    {
        // Arrange
        var facts = new TestAssertionFacts("OrdersTests.Place", 3, null, false, [], [], false);

        // Act
        var result = await AssessAsync("tests/A.IntegrationTests/OrdersTests.cs", facts);

        // Assert
        result.Value.Single().Should().BeEquivalentTo(new { FieldsResolved = false, UncheckedFields = Array.Empty<string>() });
    }

    [Fact]
    public async Task AssessAsync_NonCSharpIntegrationFile_SkippedWithNote()
    {
        // Arrange
        var facts = new TestAssertionFacts("books.spec.Get_works", 3, null, false, [], [], true);

        // Act
        var result = await AssessAsync("tests/e2e/books.spec.ts", facts);

        // Assert
        new { result.Value, result.Notes }.Should().BeEquivalentTo(new
        {
            Value = Array.Empty<TestAssertion>(),
            Notes = new[] { "tests/e2e/books.spec.ts: assertion levels not assessed (C# only)" },
        });
    }
}
