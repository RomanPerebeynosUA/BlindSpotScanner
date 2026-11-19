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

    private static readonly FakeTestSyntaxReader Reader = new(new Dictionary<string, TestFileFacts>
    {
        ["public class BooksApiTests { }"] = new([], [], ["BooksApiTests.List_returns_seeded_books"], ["BooksApiTests.List_returns_seeded_books"], ["BooksApiTests"]),
    });

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
        new GradeQualityPhase(llm, Blobs, Reader).GradeAsync(Run, tests ?? Tests, Coverage, Profile, 200_000, CancellationToken.None);

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


    private static string Finding(string evidence) =>
        $$"""{"kind":"FalsePositive","severity":"Medium","subject":"{{BooksTests}}::List_returns_seeded_books","headline":"Only checks the status","detail":["200 only"],"evidence":["{{evidence}}"]}""";

    [Fact]
    public async Task Grade_MalformedFindingEvidence_IsDroppedNotReasked()
    {
        // Arrange (eShopOnWeb: Haiku cited a whole test file on every attempt)
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(findings: Finding("src/Web/AuthClient.cs"))));

        // Act
        var result = await Grade(llm);

        // Assert
        var finding = result.Value.Findings.Single(f => f.Kind == FindingKind.FalsePositive);
        new { finding.Evidence, Note = result.Notes.Any(n => n.Contains("src/Web/AuthClient.cs", StringComparison.Ordinal)) }.Should().BeEquivalentTo(new
        {
            Evidence = Array.Empty<string>(),
            Note = true,
        });
    }

    [Fact]
    public async Task Grade_Validator_AcceptsMalformedRiskEvidence()
    {
        // Act (dropped when the answer is applied, so it never costs a re-ask)
        var errors = await ValidateWith(Answer(risks: Risk().Replace("\"evidence\":[]", "\"evidence\":[\"src/Web/AuthClient.cs\"]", StringComparison.Ordinal)));

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Grade_Validator_RejectsCitedNameNotDeclaredInGivenFile()
    {
        // Act
        var errors = await ValidateWith(Answer().Replace($"\"evidence\":[\"{BooksTests}::List_returns_seeded_books\"]}}", $"\"evidence\":[\"{BooksTests}::AuthHandler\"]}}", StringComparison.Ordinal));

        // Assert
        errors.Should().Contain($"scorecard/1: {BooksTests}::AuthHandler is not a method in the test or fixture files you were given");
    }

    [Fact]
    public async Task Grade_FindingEvidenceInFileNotGiven_IsDropped()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(findings: Finding("src/Web/AuthClient.cs::SendAsync"))));

        // Act
        var result = await Grade(llm);

        // Assert
        result.Value.Findings.Single(f => f.Kind == FindingKind.FalsePositive).Evidence.Should().BeEmpty();
    }

    [Fact]
    public async Task Grade_Validator_RejectsUndeclaredNameInEvidenceBullet()
    {
        // Act
        var errors = await ValidateWith(Answer().Replace($"\"Evidence: {BooksTests}::List_returns_seeded_books\"", $"\"Evidence: {BooksTests}::Ghost_test.\"", StringComparison.Ordinal));

        // Assert
        errors.Should().Contain($"scorecard/1: {BooksTests}::Ghost_test is not a method in the test or fixture files you were given");
    }


    [Fact]
    public async Task Grade_Validator_AcceptsClassDeclaredInGivenFile()
    {
        // Act (a fixture class is evidence too: eShopOnWeb's TestApplication)
        var errors = await ValidateWith(Answer().Replace($"\"evidence\":[\"{BooksTests}::List_returns_seeded_books\"]}}", $"\"evidence\":[\"{BooksTests}::BooksApiTests\"]}}", StringComparison.Ordinal));

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Grade_ShortPathCitation_IsCompletedInTheScorecard()
    {
        // Arrange (TodoApp: Haiku cited "TodoApiTests.cs::…" without its folder)
        var shortPath = Answer().Replace(BooksTests, "BooksApiTests.cs", StringComparison.Ordinal);
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(shortPath));

        // Act
        var result = await Grade(llm);

        // Assert
        var first = result.Value.Scorecard[0];
        new { Errors = llm.Requests.Single().Validate!(JsonDocument.Parse(shortPath).RootElement), first.Evidence, Bullet = first.JustificationBullets[2] }.Should().BeEquivalentTo(new
        {
            Errors = Array.Empty<string>(),
            Evidence = new[] { $"{BooksTests}::List_returns_seeded_books" },
            Bullet = $"Evidence: {BooksTests}::List_returns_seeded_books",
        });
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
