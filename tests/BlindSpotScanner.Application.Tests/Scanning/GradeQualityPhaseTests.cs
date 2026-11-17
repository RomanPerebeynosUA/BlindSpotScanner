using System.Text.Json;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Application.Tests.Llm;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class GradeQualityPhaseTests
{
    private const string BooksTests = "tests/Api.IntegrationTests/BooksApiTests.cs";

    private static readonly RunContext Run = new("example-org", "bookstore-api", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore-api");

    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["**/Controllers/*Controller.cs"],
        TestGlobs = ["tests/**/*.cs"],
        IntegrationGlobs = ["tests/*.IntegrationTests/**"],
    };

    private static readonly FakeBlobReader Blobs = new(new Dictionary<string, string> { ["t-books"] = "public class BooksApiTests { }" });

    private static readonly TestClassification Tests = new(
    [
        new ClassifiedTestFile(BooksTests, "t-books", TestFileKind.Integration, "rule", ["BooksApiTests.List_returns_seeded_books"]),
    ]);

    private static readonly IReadOnlyList<ControllerCoverage> Coverage =
    [
        new ControllerCoverage
        {
            Controller = "OrdersController",
            SourcePath = "src/Api/Controllers/OrdersController.cs",
            RoutePrefix = "api/orders",
            Status = CoverageStatus.Unverified,
            Endpoints =
            [
                new EndpointResult { Method = EndpointMethod.Post, Route = "api/orders/{id}/cancel", Status = CoverageStatus.None, Verified = true, Evidence = [] },
            ],
        },
    ];

    private static string Dimension(int id) =>
        $$"""{"id":{{id}},"score":6,"scorable":true,"confidence":"High","justificationBullets":["What is tested: x","What is missing: y","Evidence: {{BooksTests}}::List_returns_seeded_books","Why this score: 5-6 band"],"evidence":["{{BooksTests}}::List_returns_seeded_books"]}""";

    private static string Answer(string? risks = null, string? findings = null, int dimensions = 7) =>
        $$"""{"scorecard":[{{string.Join(',', Enumerable.Range(1, dimensions).Select(Dimension))}}],"findings":[{{findings}}],"topRisks":[{{risks}}]}""";

    private static Task<PhaseResult<QualityGrade>> Grade(FakeLlmClient llm, TestClassification? tests = null) =>
        new GradeQualityPhase(llm, Blobs).GradeAsync(Run, tests ?? Tests, Coverage, Profile, 200_000, CancellationToken.None);

    [Fact]
    public async Task Grade_ValidAnswer_UsesRubricNamesAndWeights()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));

        // Act
        var result = await Grade(llm);

        // Assert
        result.Value.Scorecard.Select(d => (d.Id, d.Dimension, d.Weight, d.Score)).Should().Equal(Rubric.Dimensions.Select(d => (d.Id, d.Name, d.Weight, (int?)6)));
    }

    [Fact]
    public async Task Grade_LlmFailure_AllUnscorable()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Failure("schema"));

        // Act
        var result = await Grade(llm);

        // Assert
        result.Value.Scorecard.Should().AllSatisfy(d => d.Should().BeEquivalentTo(new { Scorable = false, Score = (int?)null }));
    }

    [Fact]
    public async Task Grade_NoIntegrationTests_NotSentAllUnscorable()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));

        // Act
        var result = await Grade(llm, new TestClassification([]));

        // Assert
        new { Requests = llm.Requests.Count, Scorable = result.Value.Scorecard.Count(d => d.Scorable) }.Should().BeEquivalentTo(new { Requests = 0, Scorable = 0 });
    }

    [Fact]
    public async Task Grade_NoneEndpoints_AddUncoveredEndpointFindings()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));

        // Act
        var result = await Grade(llm);

        // Assert
        result.Value.Findings.Single().Should().BeEquivalentTo(new
        {
            Id = "f1",
            Kind = FindingKind.UncoveredEndpoint,
            Severity = Severity.High,
            StableKey = "uncovered-endpoint:POST api/orders/{}/cancel",
            Headline = "POST api/orders/{id}/cancel has no integration test",
        });
    }

    [Fact]
    public async Task Grade_FindingStableKeys_AreKebabKindAndSubject()
    {
        // Arrange
        var finding = $$"""{"kind":"FalsePositive","severity":"Medium","subject":"{{BooksTests}}::List_returns_seeded_books","headline":"Only checks the status","detail":["200 only"],"evidence":["{{BooksTests}}::List_returns_seeded_books"]}""";
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(findings: finding)));

        // Act
        var result = await Grade(llm);

        // Assert
        result.Value.Findings[1].Should().BeEquivalentTo(new { Id = "f2", Kind = FindingKind.FalsePositive, StableKey = $"false-positive:{BooksTests}::List_returns_seeded_books" });
    }

    [Fact]
    public async Task Grade_Validator_RejectsRiskWithPath()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));
        await Grade(llm);
        var risk = """{"headline":"BooksApiTests.cs only checks status","whyItMatters":"Wrong data passes.","severity":"Medium","detail":["200 only"],"evidence":[]}""";
        using var answer = JsonDocument.Parse(Answer(risks: risk));

        // Act
        var errors = llm.Requests.Single().Validate!(answer.RootElement);

        // Assert
        errors.Should().Equal("topRisks/0/headline: contains a path, citation or file name");
    }

    [Fact]
    public async Task Grade_Validator_RejectsMissingDimension()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));
        await Grade(llm);
        using var answer = JsonDocument.Parse(Answer(dimensions: 6));

        // Act
        var errors = llm.Requests.Single().Validate!(answer.RootElement);

        // Assert
        errors.Should().Equal("scorecard: dimension 7 must appear exactly once");
    }

    private static string Risk(string detail = "[\"200 only\"]") =>
        $$"""{"headline":"Some tests pass on wrong data","whyItMatters":"Wrong data passes.","severity":"Medium","detail":{{detail}},"evidence":[]}""";

    private static async Task<IReadOnlyList<string>> ValidateWith(string answerJson)
    {
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer()));
        await Grade(llm);
        using var answer = JsonDocument.Parse(answerJson);
        return llm.Requests.Single().Validate!(answer.RootElement);
    }

    [Fact]
    public async Task Grade_Validator_RejectsUncitedScorecardEvidence()
    {
        // Act
        var errors = await ValidateWith(Answer().Replace($"\"evidence\":[\"{BooksTests}::List_returns_seeded_books\"]}}", "\"evidence\":[\"tests/ApiFactory.cs\"]}", StringComparison.Ordinal));

        // Assert
        errors.Should().Contain("scorecard/1/evidence/0: malformed citation 'tests/ApiFactory.cs'");
    }

    [Fact]
    public async Task Grade_Validator_RejectsMoreThanFiveRisks()
    {
        // Act
        var errors = await ValidateWith(Answer(risks: string.Join(',', Enumerable.Repeat(Risk(), 6))));

        // Assert
        errors.Should().Equal("topRisks: at most 5 risks");
    }

    [Fact]
    public async Task Grade_Validator_RejectsRiskWithoutDetail()
    {
        // Act
        var errors = await ValidateWith(Answer(risks: Risk(detail: "[]")));

        // Assert
        errors.Should().Equal("topRisks/0/detail: needs 1 to 8 bullets");
    }
}
