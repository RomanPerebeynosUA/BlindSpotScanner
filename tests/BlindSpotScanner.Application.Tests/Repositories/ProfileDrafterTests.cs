using System.Text.Json;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Application.Tests.Llm;
using BlindSpotScanner.Domain.Entities;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class ProfileDrafterTests
{
    private const string Answer = """{"markdown":"# Repo profile — drafted","openQuestions":["Is the payment client a real seam?"]}""";

    private readonly StubGitHubClient _github = new();
    private readonly FakeRepositoryStore _repositories = new();
    private readonly Guid _repositoryId = Guid.NewGuid();
    private FakeLlmClient _llm = new(_ => FakeLlmClient.Json(Answer));

    public ProfileDrafterTests()
    {
        _repositories.Repositories.Add(new Repository { Id = _repositoryId, Owner = "example-org", Name = "bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/example-org/bookstore" });
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
    public async Task DraftAsync_SendsFilteredTreeBuildFilesAndSamples()
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
        result.Draft.Should().BeEquivalentTo(new ProfileDraft("# Repo profile — drafted", ["Is the payment client a real seam?"]));
    }

    [Fact]
    public async Task DraftAsync_SkipsFilesOverMaxBytes()
    {
        // Act
        await Drafter(maxFileBytes: 20).DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        SentInput().GetProperty("sampleTests").EnumerateArray().Select(f => f.GetProperty("path").GetString()).Should().Equal(
            "tests/Bookstore.UnitTests/PriceTests.cs");
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
        _llm = new FakeLlmClient(_ => FakeLlmClient.Json("""{"markdown":"  ","openQuestions":[]}"""));

        // Act
        await Drafter().DraftAsync(_repositoryId, CancellationToken.None);

        // Assert
        var answer = JsonDocument.Parse("""{"markdown":"  ","openQuestions":[]}""").RootElement;
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
    public async Task DraftAsync_UnknownRepository_ReturnsRepositoryNotFound()
    {
        // Act
        var result = await Drafter().DraftAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(DraftProfileStatus.RepositoryNotFound);
        _llm.Requests.Should().BeEmpty();
    }
}
