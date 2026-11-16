using FluentAssertions;

namespace BlindSpotScanner.Domain.Tests;

public class GitHubRepoUrlTests
{
    [Fact]
    public void TryParse_OwnerAndRepo_ReturnsThem()
    {
        // Arrange
        const string text = "https://github.com/example-org/Bookstore-Api";

        // Act
        var parsed = GitHubRepoUrl.TryParse(text, out var url);

        // Assert
        new { parsed, url }.Should().BeEquivalentTo(new { parsed = true, url = new GitHubRepoUrl("example-org", "Bookstore-Api", null) });
    }

    [Fact]
    public void TryParse_TreeBranch_ReturnsBranch()
    {
        // Arrange
        const string text = "https://github.com/example-org/bookstore-api/tree/develop";

        // Act
        GitHubRepoUrl.TryParse(text, out var url);

        // Assert
        url.Should().Be(new GitHubRepoUrl("example-org", "bookstore-api", "develop"));
    }

    [Fact]
    public void TryParse_BranchWithSlash_KeepsWholeBranch()
    {
        // Arrange
        const string text = "https://github.com/example-org/bookstore-api/tree/feature/x";

        // Act
        GitHubRepoUrl.TryParse(text, out var url);

        // Assert
        url!.Branch.Should().Be("feature/x");
    }

    [Theory]
    [InlineData("https://github.com/example-org/bookstore-api.git")]
    [InlineData("https://github.com/example-org/bookstore-api/")]
    [InlineData("http://GitHub.com/example-org/bookstore-api")]
    public void TryParse_DotGitTrailingSlashOrHostCase_AreIgnored(string text)
    {
        // Arrange
        // Act
        GitHubRepoUrl.TryParse(text, out var url);

        // Assert
        url.Should().Be(new GitHubRepoUrl("example-org", "bookstore-api", null));
    }

    [Theory]
    [InlineData("https://gitlab.com/example-org/bookstore-api")]
    [InlineData("https://github.com/onlyowner")]
    [InlineData("https://github.com/example-org/bookstore-api/blob/main/README.md")]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void TryParse_NotARepositoryUrl_ReturnsFalse(string? text)
    {
        // Arrange
        // Act
        var parsed = GitHubRepoUrl.TryParse(text, out var url);

        // Assert
        new { parsed, url }.Should().BeEquivalentTo(new { parsed = false, url = (GitHubRepoUrl?)null });
    }

    [Fact]
    public void Parse_Invalid_ThrowsFormatException()
    {
        // Arrange
        // Act
        var act = () => GitHubRepoUrl.Parse("https://gitlab.com/a/b");

        // Assert
        act.Should().Throw<FormatException>().WithMessage("*https://github.com/{owner}/{repo}[/tree/{branch}]*");
    }
}
