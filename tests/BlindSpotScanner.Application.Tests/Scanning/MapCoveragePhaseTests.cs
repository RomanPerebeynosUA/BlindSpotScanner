using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Application.Tests.Llm;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class MapCoveragePhaseTests
{
    private const string BooksTests = "tests/Api.IntegrationTests/BooksApiTests.cs";
    private const string HealthTests = "tests/Api.IntegrationTests/HealthTests.cs";
    private const string ListTest = BooksTests + "::List_returns_seeded_books";

    private static readonly RunContext Run = new("example-org", "bookstore-api", "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "https://github.com/example-org/bookstore-api");

    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["**/Controllers/*Controller.cs"],
        TestGlobs = ["tests/**/*.cs"],
    };

    private static readonly ExtractedController Books = new("BooksController", "src/Api/Controllers/BooksController.cs", "api/books",
    [
        new ExtractedEndpoint(EndpointMethod.Get, "api/books"),
        new ExtractedEndpoint(EndpointMethod.Get, "api/books/{id}"),
    ]);

    private static readonly ExtractedController Orders = new("OrdersController", "src/Api/Controllers/OrdersController.cs", "api/orders",
    [
        new ExtractedEndpoint(EndpointMethod.Post, "api/orders"),
    ]);

    private static readonly FakeBlobReader Blobs = new(new Dictionary<string, string>
    {
        ["t-books"] = """public class BooksApiTests { [Fact] public async Task List_returns_seeded_books() => await client.GetAsync("api/books"); }""",
        ["t-segment"] = """public class CatalogTests { [Fact] public async Task Lists() => await client.GetAsync($"{Base}/books"); }""",
        ["t-health"] = """public class HealthTests { [Fact] public async Task Ping() => await client.GetAsync("/health"); }""",
    });

    private static readonly TestClassification Tests = new(
    [
        new ClassifiedTestFile(BooksTests, "t-books", TestFileKind.Integration, "rule", ["BooksApiTests.List_returns_seeded_books"]),
        new ClassifiedTestFile(HealthTests, "t-health", TestFileKind.Integration, "rule", ["HealthTests.Ping"]),
    ]);

    private static string Answer(params string[] endpoints) => $$"""{"endpoints":[{{string.Join(',', endpoints)}}]}""";

    private static string Endpoint(string id, string status, params string[] evidence) =>
        $$"""{"id":"{{id}}","status":"{{status}}","evidence":[{{string.Join(',', evidence.Select(e => $"\"{e}\""))}}],"falsePositive":false,"notes":"n"}""";

    private static async Task<IReadOnlyList<EndpointResult>> MapBooks(FakeLlmClient llm, IReadOnlyList<string>? inventoryReasons = null)
    {
        var result = await new MapCoveragePhase(llm, Blobs).MapAsync(
            Run, new EndpointInventory([Books], []), Tests, new RepositoryInventory([], [], [], inventoryReasons ?? []), Profile, 200_000, CancellationToken.None);
        return result.Value.Single().Endpoints;
    }

    [Fact]
    public async Task Map_CoveredWithEvidence_IsVerified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", ListTest), Endpoint("e2", "none"))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints[0].Should().BeEquivalentTo(new { Method = EndpointMethod.Get, Route = "api/books", Status = CoverageStatus.Covered, Verified = true, Evidence = new[] { ListTest } });
    }

    [Fact]
    public async Task Map_EvidenceNotInSentFiles_IsUnverified()
    {
        // Arrange (HealthTests.cs was not sent for BooksController)
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", HealthTests + "::Ping"), Endpoint("e2", "none"))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints[0].Should().BeEquivalentTo(new { Status = CoverageStatus.Unverified, Verified = false, Notes = "evidence did not resolve to a test that was read" });
    }

    [Fact]
    public async Task Map_EvidenceNamingUnknownTest_IsUnverified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", BooksTests + "::Invented_test"), Endpoint("e2", "none"))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints[0].Status.Should().Be(CoverageStatus.Unverified);
    }

    [Fact]
    public async Task Map_MissingEndpointId_IsUnverified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", ListTest))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints[1].Should().BeEquivalentTo(new { Route = "api/books/{id}", Status = CoverageStatus.Unverified, Notes = "no answer for this endpoint" });
    }

    [Fact]
    public async Task Map_UnknownAndDuplicateIds_AreIgnored()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", ListTest), Endpoint("e1", "none"), Endpoint("e9", "covered", ListTest))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints.Select(e => e.Status).Should().Equal(CoverageStatus.Covered, CoverageStatus.Unverified);
    }

    [Fact]
    public async Task Map_NoneWithoutAllCandidates_IsUnverified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", ListTest), Endpoint("e2", "none"))));

        // Act
        var endpoints = await MapBooks(llm, inventoryReasons: ["tree truncated at 'tests'; files below it were not enumerated"]);

        // Assert
        endpoints[1].Status.Should().Be(CoverageStatus.Unverified);
    }

    [Fact]
    public async Task Map_NoneWithAllCandidates_StaysNone()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "covered", ListTest), Endpoint("e2", "none"))));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints[1].Should().BeEquivalentTo(new { Status = CoverageStatus.None, Verified = true });
    }

    [Fact]
    public async Task Map_NoCandidateTests_NotSentAndUnverified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Failure("must not be called"));

        // Act
        var result = await new MapCoveragePhase(llm, Blobs).MapAsync(
            Run, new EndpointInventory([Orders], []), Tests, new RepositoryInventory([], [], [], []), Profile, 200_000, CancellationToken.None);

        // Assert
        new { Requests = llm.Requests.Count, Statuses = result.Value.Single().Endpoints.Select(e => e.Status) }.Should().BeEquivalentTo(new
        {
            Requests = 0,
            Statuses = new[] { CoverageStatus.Unverified },
        });
    }

    [Fact]
    public async Task Map_OnlyCandidateTestsAreSent()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "none"), Endpoint("e2", "none"))));

        // Act
        await MapBooks(llm);

        // Assert
        new { Books = llm.Requests.Single().InputJson.Contains(BooksTests, StringComparison.Ordinal), Health = llm.Requests.Single().InputJson.Contains(HealthTests, StringComparison.Ordinal) }
            .Should().BeEquivalentTo(new { Books = true, Health = false });
    }

    [Fact]
    public async Task Map_LlmFailure_AllUnverified()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Failure("schema"));

        // Act
        var endpoints = await MapBooks(llm);

        // Assert
        endpoints.Should().AllSatisfy(e => e.Should().BeEquivalentTo(new { Status = CoverageStatus.Unverified, Verified = false, Notes = "mapping failed" }));
    }

    [Fact]
    public async Task Map_NoIntegrationTestsAtAll_IsNone()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Failure("must not be called"));

        // Act
        var result = await new MapCoveragePhase(llm, Blobs).MapAsync(
            Run, new EndpointInventory([Books], []), new TestClassification([]), new RepositoryInventory([], [], [], []), Profile, 200_000, CancellationToken.None);

        // Assert
        result.Value.Single().Endpoints.Select(e => e.Status).Should().Equal(CoverageStatus.None, CoverageStatus.None);
    }

    [Fact]
    public async Task Map_TestUsingRouteSegmentOnly_IsCandidate()
    {
        // Arrange (the URL is built from a base-path constant, so "api/books" never appears)
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "none"), Endpoint("e2", "none"))));
        var tests = new TestClassification([new ClassifiedTestFile("tests/Api.IntegrationTests/CatalogTests.cs", "t-segment", TestFileKind.Integration, "rule", ["CatalogTests.Lists"])]);

        // Act
        await new MapCoveragePhase(llm, Blobs).MapAsync(
            Run, new EndpointInventory([Books], []), tests, new RepositoryInventory([], [], [], []), Profile, 200_000, CancellationToken.None);

        // Assert
        llm.Requests.Should().ContainSingle().Which.InputJson.Should().Contain("CatalogTests.cs");
    }

    [Fact]
    public async Task Map_RootRoute_SendsEveryTest()
    {
        // Arrange (a "/" route has no literal text to search for)
        var root = new ExtractedController("RootEndpoints", "src/Api/Endpoints/RootEndpoints.cs", null, [new ExtractedEndpoint(EndpointMethod.Get, "")]);
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "none"))));

        // Act
        await new MapCoveragePhase(llm, Blobs).MapAsync(
            Run, new EndpointInventory([root], []), Tests, new RepositoryInventory([], [], [], []), Profile, 200_000, CancellationToken.None);

        // Assert
        new { Books = llm.Requests.Single().InputJson.Contains(BooksTests, StringComparison.Ordinal), Health = llm.Requests.Single().InputJson.Contains(HealthTests, StringComparison.Ordinal) }
            .Should().BeEquivalentTo(new { Books = true, Health = true });
    }

    private static async Task<IReadOnlyList<string>> ValidateWith(string answerJson)
    {
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Endpoint("e1", "none"), Endpoint("e2", "none"))));
        await MapBooks(llm);
        using var answer = System.Text.Json.JsonDocument.Parse(answerJson);
        return llm.Requests.Single().Validate!(answer.RootElement);
    }

    [Fact]
    public async Task Map_Validator_RejectsMissingDuplicateAndUnknownIds()
    {
        // Act
        var errors = await ValidateWith(Answer(Endpoint("e1", "none"), Endpoint("e1", "none"), Endpoint("e9", "none")));

        // Assert
        errors.Should().Equal("endpoints: e1 is answered more than once", "endpoints: unknown id e9", "endpoints: no answer for e2");
    }

    [Fact]
    public async Task Map_Validator_RejectsUnresolvedEvidence()
    {
        // Act
        var errors = await ValidateWith(Answer(Endpoint("e1", "covered", HealthTests + "::Ping"), Endpoint("e2", "none")));

        // Assert
        errors.Should().Equal($"endpoints/e1: evidence '{HealthTests}::Ping' does not name a test method of a file that was sent");
    }
}
