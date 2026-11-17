using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Application.Tests.Llm;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ClassifyAmbiguousPhaseTests
{
    private const string Price = "tests/Api.UnitTests/PriceTests.cs";

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

    private static ClassifiedTestFile File(string path, TestFileKind kind, string sha = "sha") =>
        new(path, sha, kind, kind == TestFileKind.Ambiguous ? "no profile rule matched" : "rule", ["PriceTests.A"]);

    private static FakeBlobReader Blobs(string text = "public class PriceTests { [Fact] public void A() { } }") =>
        new(new Dictionary<string, string> { ["sha"] = text, ["sha-2"] = text });

    private static string Answer(string path, string kind) =>
        $$"""{"files":[{"path":"{{path}}","kind":"{{kind}}","confidence":"High","reason":"hosts a test server","evidenceKind":"observed"}]}""";

    private static Task<PhaseResult<TestClassification>> Classify(FakeLlmClient llm, IReadOnlyList<ClassifiedTestFile> files, FakeBlobReader? blobs = null, int maxFileBytes = 200_000) =>
        new ClassifyAmbiguousPhase(llm, blobs ?? Blobs()).ClassifyAsync(Run, new TestClassification(files), Profile, maxFileBytes, CancellationToken.None);

    [Fact]
    public async Task Classify_LlmSaysIntegration_BecomesIntegration()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Price, "integration")));

        // Act
        var result = await Classify(llm, [File(Price, TestFileKind.Ambiguous)]);

        // Assert
        new { result.Value.Files[0].Kind, result.Value.Files[0].Reason }.Should().BeEquivalentTo(new
        {
            Kind = TestFileKind.Integration,
            Reason = "LLM (High): hosts a test server",
        });
    }

    [Fact]
    public async Task Classify_LlmSaysUnit_BecomesOther()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Price, "unit")));

        // Act
        var result = await Classify(llm, [File(Price, TestFileKind.Ambiguous)]);

        // Assert
        result.Value.Files[0].Kind.Should().Be(TestFileKind.Other);
    }

    [Fact]
    public async Task Classify_RuleDecidedFiles_AreNotSent()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Price, "unit")));

        // Act
        await Classify(llm, [File("tests/Api.IntegrationTests/BooksApiTests.cs", TestFileKind.Integration), File(Price, TestFileKind.Ambiguous), File("tests/Helpers.cs", TestFileKind.Other)]);

        // Assert
        llm.Requests.Should().ContainSingle().Which.InputJson.Should().Contain(Price);
    }

    [Fact]
    public async Task Classify_DeniedPath_StaysAmbiguousWithNote()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer("tests/secrets.cs", "unit")));

        // Act
        var result = await Classify(llm, [File("tests/secrets.cs", TestFileKind.Ambiguous)]);

        // Assert
        new { result.Value.Files[0].Kind, result.Notes, Sent = llm.Requests.Count }.Should().BeEquivalentTo(new
        {
            Kind = TestFileKind.Ambiguous,
            Notes = new[] { "tests/secrets.cs: not sent to the LLM (denied path)" },
            Sent = 0,
        });
    }

    [Fact]
    public async Task Classify_TooLarge_StaysAmbiguousWithNote()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Price, "unit")));

        // Act
        var result = await Classify(llm, [File(Price, TestFileKind.Ambiguous)], maxFileBytes: 10);

        // Assert
        new { result.Value.Files[0].Kind, result.Notes }.Should().BeEquivalentTo(new
        {
            Kind = TestFileKind.Ambiguous,
            Notes = new[] { $"{Price}: not sent to the LLM (larger than 10 bytes)" },
        });
    }

    [Fact]
    public async Task Classify_LlmFailure_StaysAmbiguousWithNote()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Failure("schema: missing files"));

        // Act
        var result = await Classify(llm, [File(Price, TestFileKind.Ambiguous)]);

        // Assert
        new { result.Value.Files[0].Kind, result.Notes }.Should().BeEquivalentTo(new
        {
            Kind = TestFileKind.Ambiguous,
            Notes = new[] { $"{Price}: classification failed (schema: missing files)" },
        });
    }

    [Fact]
    public async Task Classify_AnswerForAnotherPath_IsRejectedByValidator()
    {
        // Arrange
        var llm = new FakeLlmClient(_ => FakeLlmClient.Json(Answer(Price, "unit")));
        await Classify(llm, [File(Price, TestFileKind.Ambiguous)]);
        var wrongPath = System.Text.Json.JsonDocument.Parse(Answer("tests/Other.cs", "unit")).RootElement;

        // Act
        var errors = llm.Requests.Single().Validate!(wrongPath);

        // Assert
        errors.Should().Equal($"files: expected exactly one answer for '{Price}'");
    }

    [Fact]
    public async Task Classify_Usage_IsSummed()
    {
        // Arrange
        var llm = new FakeLlmClient(r => FakeLlmClient.Json(Answer(r.InputJson.Contains("Stock", StringComparison.Ordinal) ? "tests/StockTests.cs" : Price, "unit")));

        // Act
        var result = await Classify(llm, [File(Price, TestFileKind.Ambiguous), File("tests/StockTests.cs", TestFileKind.Ambiguous, "sha-2")]);

        // Assert
        result.Usage.Should().Be(new LlmUsage(200, 40, 0.0008m));
    }
}
