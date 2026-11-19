using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class ProfilePatternCheckerTests
{
    private readonly StubGitHubClient _github = new();
    private readonly FakeRepositoryStore _repositories = new();
    private readonly Guid _repositoryId = Guid.NewGuid();

    public ProfilePatternCheckerTests()
    {
        _repositories.Repositories.Add(new Repository { Id = _repositoryId, Owner = "example-org", Name = "bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/example-org/bookstore" });
        _github.Files["src/Bookstore.Api/Controllers/BooksController.cs"] = "class BooksController {}";
        _github.Files["tests/Bookstore.IntegrationTests/BooksApiTests.cs"] = "class BooksApiTests {}";
    }

    private static ProfileRules Rules(params string[] testGlobs) => new("real-seam", testGlobs, [], [], [], [], [], []);

    private ProfilePatternChecker Checker() => new(_repositories, _github);

    [Fact]
    public async Task CheckAsync_KnownRepo_CountsAgainstDefaultBranchTree()
    {
        // Act
        var result = await Checker().CheckAsync(_repositoryId, Rules("tests/**/*.cs", "nope/**"), CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new PatternCheckResult(
            PatternCheckStatus.Checked,
            [new PatternMatch("testGlobs", "tests/**/*.cs", 1), new PatternMatch("testGlobs", "nope/**", 0)],
            []));
    }

    [Fact]
    public async Task CheckAsync_MalformedPattern_ListsSyntaxErrorAndStillCounts()
    {
        // Act
        var result = await Checker().CheckAsync(_repositoryId, Rules("tests/** x"), CancellationToken.None);

        // Assert
        new { result.Errors.Count, Counted = result.Matches.Count }.Should().BeEquivalentTo(new { Count = 1, Counted = 1 });
    }

    [Fact]
    public async Task CheckAsync_UnknownRepo_RepositoryNotFound()
    {
        // Act
        var result = await Checker().CheckAsync(Guid.NewGuid(), Rules("tests/**"), CancellationToken.None);

        // Assert
        result.Status.Should().Be(PatternCheckStatus.RepositoryNotFound);
    }
}
