using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class RepositoryServiceTests
{
    private readonly StubGitHubClient _github = new();
    private readonly FakeRepositoryStore _store = new();

    private RepositoryService Service => new(_store, _github);

    private static Repository Existing() => new()
    {
        Id = Guid.NewGuid(), Owner = "Example-Org", Name = "Bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/Example-Org/Bookstore",
    };

    [Fact]
    public async Task AddAsync_ValidUrl_SavesGitHubCanonicalNamesAndProfileV1()
    {
        // Arrange
        const string url = "https://github.com/example-org/bookstore.git";

        // Act
        var result = await Service.AddAsync(url, null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.Created);
        _store.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Owner = "Example-Org", Name = "Bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/Example-Org/Bookstore" });
        _store.Profiles.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { RepositoryId = result.Repository!.Id, Version = 1, IntegrationMappedTo = IntegrationMappedTo.RealSeam });
    }

    [Fact]
    public async Task AddAsync_InvalidUrl_ReturnsInvalidUrl()
    {
        // Arrange
        const string url = "https://gitlab.com/a/b";

        // Act
        var result = await Service.AddAsync(url, null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.InvalidUrl);
        _store.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_RepositoryNotVisible_ReturnsNotFound()
    {
        // Arrange
        _github.RepositoryError = new GitHubNotFoundException("Not found.");

        // Act
        var result = await Service.AddAsync("https://github.com/example-org/secret", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.NotFound);
    }

    [Fact]
    public async Task AddAsync_TokenRejected_ReturnsGitHubUnavailable()
    {
        // Arrange
        _github.RepositoryError = new GitHubAuthenticationException("Bad credentials.");

        // Act
        var result = await Service.AddAsync("https://github.com/example-org/bookstore", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.GitHubUnavailable);
    }

    [Theory]
    [InlineData("dev", "https://github.com/example-org/bookstore/tree/feat/x", "dev")]
    [InlineData(null, "https://github.com/example-org/bookstore/tree/feat/x", "feat/x")]
    [InlineData(null, "https://github.com/example-org/bookstore", "main")]
    public async Task AddAsync_BranchPrecedence_BodyThenUrlThenGitHubDefault(string? bodyBranch, string url, string expected)
    {
        // Act
        var result = await Service.AddAsync(url, bodyBranch, CancellationToken.None);

        // Assert
        result.Repository!.DefaultBranch.Should().Be(expected);
        _github.RequestedBranch.Should().Be(expected);
    }

    [Fact]
    public async Task AddAsync_UnknownBranch_ReturnsBranchNotFound()
    {
        // Act
        var result = await Service.AddAsync("https://github.com/example-org/bookstore", "nope", CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.BranchNotFound);
        _store.Repositories.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_SameRepositoryDifferentUrlForm_ReturnsAlreadyRegistered()
    {
        // Arrange
        var existing = Existing();
        _store.Repositories.Add(existing);

        // Act
        var result = await Service.AddAsync("https://github.com/EXAMPLE-ORG/bookstore/tree/main", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.AlreadyRegistered);
        result.Repository.Should().BeSameAs(existing);
    }

    [Fact]
    public async Task AddAsync_StoreRejectsDuplicate_ReturnsAlreadyRegistered()
    {
        // Arrange
        var winner = Existing();
        _store.ConcurrentWinner = winner;

        // Act
        var result = await Service.AddAsync("https://github.com/example-org/bookstore", null, CancellationToken.None);

        // Assert
        result.Status.Should().Be(AddRepositoryStatus.AlreadyRegistered);
        result.Repository.Should().BeSameAs(winner);
    }

    [Fact]
    public void Default_FillsTemplateAndD22Globs()
    {
        // Arrange
        var repositoryId = Guid.NewGuid();

        // Act
        var profile = ProfileTemplate.Default(repositoryId, "Example-Org/Bookstore");

        // Assert
        profile.Markdown.Should().StartWith("# Repo profile — Example-Org/Bookstore\n");
        profile.Should().BeEquivalentTo(new
        {
            RepositoryId = repositoryId,
            Version = 1,
            IntegrationMappedTo = IntegrationMappedTo.RealSeam,
            ControllerGlobs = new[] { "src/**/Controllers/**/*Controller.cs" },
            MinimalApiGlobs = new[] { "src/**/Endpoints/**/*.cs" },
            TestGlobs = new[] { "tests/**/*.cs" },
            IntegrationGlobs = new[] { "tests/*.IntegrationTests/**" },
            IntegrationAttributes = new[] { "Trait(\"Category\",\"Integration\")" },
            IntegrationBaseTypes = new[] { "WebApplicationFactory" },
            NotIntegrationGlobs = new[] { "**/*Fixture.cs" },
        });
    }
}
