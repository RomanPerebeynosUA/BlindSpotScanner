using System.Text.Json;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Application.Tests.Llm;
using BlindSpotScanner.Domain.Entities;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class ProfileDrafterTests
{
    private const string Endpoints = "src/**/Endpoints/**/*.cs";
    private const string ZeroMatchQuestion = $"minimalApiGlobs: '{Endpoints}' matches no file in the repository; check it before saving.";

    private static readonly string Answer = AnswerWith();

    /// <summary>A draft whose globs match the stub tree unless <paramref name="minimalApiGlobs"/> says otherwise.</summary>
    private static string AnswerWith(string minimalApiGlobs = "[]") => $$"""
        {"markdown":"# Repo profile — drafted","openQuestions":["Is the payment client a real seam?"],
         "integrationMappedTo":"real-seam","testGlobs":["tests/**/*.cs"],"controllerGlobs":["src/**/Controllers/*Controller.cs"],
         "minimalApiGlobs":{{minimalApiGlobs}},"integrationGlobs":["tests/*.IntegrationTests/**"],"integrationAttributes":[],
         "integrationBaseTypes":["WebApplicationFactory"],"notIntegrationGlobs":[]}
        """;

    /// <summary>Answers in order; the last one repeats.</summary>
    private static FakeLlmClient InOrder(params LlmResult[] answers)
    {
        var next = 0;
        return new FakeLlmClient(_ => answers[Math.Min(next++, answers.Length - 1)]);
    }

    private readonly StubGitHubClient _github = new();
    private readonly FakeRepositoryStore _repositories = new();
    private readonly Guid _repositoryId = Guid.NewGuid();
    private FakeLlmClient _llm = new(_ => FakeLlmClient.Json(Answer));

    public ProfileDrafterTests()
    {
        _repositories.Repositories.Add(new Repository { Id = _repositoryId, Owner = "example-org", Name = "bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/example-org/bookstore", LlmSharingAcknowledgedAt = DateTimeOffset.UnixEpoch });
        _github.Files["Bookstore.slnx"] = "<Solution />";
        _github.Files["src/Bookstore.Api/Bookstore.Api.csproj"] = "<Project />";
        _github.Files["src/Bookstore.Api/Controllers/BooksController.cs"] = "class BooksController {}";
        _github.Files["src/Bookstore.Api/bin/Debug/Bookstore.Api.dll"] = "binary";
        _github.Files["src/Bookstore.Api/.env"] = "SECRET=1";
        _github.Files["tests/Bookstore.UnitTests/PriceTests.cs"] = "class PriceTests {}";
        _github.Files["tests/Bookstore.IntegrationTests/BooksApiTests.cs"] = "class BooksApiTests {}";
    }

    private ProfileDrafter Drafter(int maxFileBytes = 200_000) => new(_repositories, _github, _llm, maxFileBytes);

    private JsonElement SentInput() => JsonDocument.Parse(_llm.Requests.Single().InputJson).RootElement;

    [Fact]
    public async Task DraftAsync_RepositoryWithNoiseFiles_SendsFilteredTreeBuildFilesAndSamples()
    {
        // Act
        await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        var input = SentInput();
        _llm.Requests.Single().Prompt.Should().Be(Prompts.DraftProfile);
        input.GetProperty("rootTree").EnumerateArray().Select(p => p.GetString()).Should().Equal(
            "Bookstore.slnx", "src/Bookstore.Api/Bookstore.Api.csproj", "src/Bookstore.Api/Controllers/BooksController.cs",
            "tests/Bookstore.IntegrationTests/BooksApiTests.cs", "tests/Bookstore.UnitTests/PriceTests.cs");
        input.GetProperty("buildFiles").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Should().Equal(
            "Bookstore.slnx", "src/Bookstore.Api/Bookstore.Api.csproj");
        input.GetProperty("sampleTests").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Should().Equal(
            "tests/Bookstore.IntegrationTests/BooksApiTests.cs", "tests/Bookstore.UnitTests/PriceTests.cs");
    }

    [Fact]
    public async Task DraftAsync_Answer_ReturnsMarkdownAndQuestions()
    {
        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(DraftProfileStatus.Drafted);
        result.Draft.Should().BeEquivalentTo(new { Markdown = "# Repo profile — drafted", OpenQuestions = new[] { "Is the payment client a real seam?" } });
    }

    [Fact]
    public async Task DraftAsync_Answer_ReturnsRulesAndMatches()
    {
        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { result.Draft!.Rules.TestGlobs, Tests = result.Draft!.Matches.Single(m => m.Field == "testGlobs") }.Should().BeEquivalentTo(new
        {
            TestGlobs = new[] { "tests/**/*.cs" },
            Tests = new PatternMatch("testGlobs", "tests/**/*.cs", 2),
        });
    }

    [Fact]
    public async Task DraftAsync_MalformedPattern_IsReasked()
    {
        // Arrange
        await Drafter().DraftAsync(_repositoryId, CancellationToken.None);
        using var answer = JsonDocument.Parse(AnswerWith("""["**/*Api.cs (x)"]"""));

        // Act
        var errors = _llm.Requests.Single().Validate!(answer.RootElement);

        // Assert
        errors.Should().Contain("minimalApiGlobs: '**/*Api.cs (x)' is not one pattern; put one pattern per line with no spaces or commas");
    }

    [Fact]
    public async Task DraftAsync_ZeroMatchPattern_ReasksOnceWithZeroMatchPatterns()
    {
        // Arrange
        _llm = InOrder(FakeLlmClient.Json(AnswerWith($"[\"{Endpoints}\"]")), FakeLlmClient.Json(Answer));

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        using var second = JsonDocument.Parse(_llm.Requests[1].InputJson);
        new
        {
            Requests = _llm.Requests.Count,
            ZeroMatch = second.RootElement.GetProperty("zeroMatchPatterns").EnumerateArray().Select(p => p.GetString()),
            result.Draft!.Rules.MinimalApiGlobs,
        }.Should().BeEquivalentTo(new { Requests = 2, ZeroMatch = new[] { $"minimalApiGlobs: {Endpoints}" }, MinimalApiGlobs = Array.Empty<string>() });
    }

    [Fact]
    public async Task DraftAsync_StillZeroAfterReask_FlagsOpenQuestion()
    {
        // Arrange
        _llm = InOrder(FakeLlmClient.Json(AnswerWith($"[\"{Endpoints}\"]")));

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        result.Draft!.OpenQuestions.Should().Equal("Is the payment client a real seam?", ZeroMatchQuestion);
    }

    [Fact]
    public async Task DraftAsync_ReaskFails_KeepsFirstAnswerAndFlags()
    {
        // Arrange
        _llm = InOrder(FakeLlmClient.Json(AnswerWith($"[\"{Endpoints}\"]")), FakeLlmClient.Failure("x"));

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { result.Status, result.Draft!.Rules.MinimalApiGlobs, Flagged = result.Draft!.OpenQuestions.Contains(ZeroMatchQuestion) }.Should().BeEquivalentTo(new
        {
            Status = DraftProfileStatus.Drafted,
            MinimalApiGlobs = new[] { Endpoints },
            Flagged = true,
        });
    }

    [Fact]
    public async Task DraftAsync_EmptyRuleList_NoReaskNoQuestion()
    {
        // Act (the answer's minimalApiGlobs is [], every other glob matches)
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { Requests = _llm.Requests.Count, result.Draft!.OpenQuestions }.Should().BeEquivalentTo(new
        {
            Requests = 1,
            OpenQuestions = new[] { "Is the payment client a real seam?" },
        });
    }

    [Fact]
    public async Task DraftAsync_FileOverMaxBytes_IsNotSent()
    {
        // Act
        await Drafter(maxFileBytes: 20).DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        SentInput().GetProperty("sampleTests").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Should().Equal(
            "tests/Bookstore.UnitTests/PriceTests.cs");
    }

    [Fact]
    public async Task DraftAsync_NotAcknowledged_RefusesWithoutCallingTheLlm()
    {
        // Arrange (doc 10: no code goes to the LLM before LLM data sharing is accepted)
        _repositories.Repositories.Single().LlmSharingAcknowledgedAt = null;

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        new { result.Status, Calls = _llm.Requests.Count }.Should().Be(new { Status = DraftProfileStatus.LlmSharingNotAcknowledged, Calls = 0 });
    }

    [Fact]
    public async Task DraftAsync_LlmFails_ReturnsLlmFailed()
    {
        // Arrange
        _llm = new FakeLlmClient(_ => FakeLlmClient.Failure("schema mismatch"));

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(DraftProfileStatus.LlmFailed);
    }

    [Fact]
    public async Task DraftAsync_BlankMarkdown_IsReasked()
    {
        // Arrange
        var blank = Answer.Replace("# Repo profile — drafted", "  ", StringComparison.Ordinal);
        _llm = new FakeLlmClient(_ => FakeLlmClient.Json(blank));

        // Act
        await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        var answer = JsonDocument.Parse(blank).RootElement;
        _llm.Requests.Single().Validate!(answer).Should().ContainSingle();
    }

    [Fact]
    public async Task DraftAsync_TruncatedTree_PrependsOpenQuestion()
    {
        // Arrange
        _github.TreeTruncated = true;

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        result.Draft!.OpenQuestions.Should().Equal(
            "The repository tree was truncated by GitHub; some folders may be missing from this draft.", "Is the payment client a real seam?");
    }

    [Fact]
    public async Task DraftAsync_MoreFilesThanSent_PrependsOpenQuestion()
    {
        // Arrange
        foreach (var i in Enumerable.Range(0, 3000))
        {
            _github.Files[$"src/Big/File{i:D4}.cs"] = "x";
        }

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        SentInput().GetProperty("rootTree").GetArrayLength().Should().Be(3000);
        result.Draft!.OpenQuestions[0].Should().Be(ProfileDrafter.CappedQuestion);
    }

    [Fact]
    public async Task DraftAsync_GitHubServerError_ReturnsGitHubFailed()
    {
        // Arrange
        _github.BlobError = new HttpRequestException("500 Internal Server Error");

        // Act
        var result = await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(DraftProfileStatus.GitHubFailed);
    }

    [Fact]
    public async Task DraftAsync_UnknownRepository_ReturnsRepositoryNotFound()
    {
        // Act
        var result = await Drafter().DraftAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(DraftProfileStatus.RepositoryNotFound);
        _llm.Requests.Should().BeEmpty();
    }
}
