using BlindSpotScanner.Application.Scanning;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class ResolvePhaseTests
{
    private const string MainSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    private const string FeatureSha = "0f1e2d3c4b5a69788796a5b4c3d2e1f001234567";

    private static ResolvePhase Phase() => new(new FakeGitHubClient(
        "main",
        new Dictionary<string, string> { ["main"] = MainSha, ["feature/x"] = FeatureSha },
        []));

    [Fact]
    public async Task Resolve_UrlWithoutBranch_UsesDefaultBranchAndPinsSha()
    {
        // Arrange
        var phase = Phase();

        // Act
        var run = await phase.ResolveAsync("https://github.com/example-org/bookstore-api", CancellationToken.None);

        // Assert
        run.Should().Be(new RunContext("example-org", "bookstore-api", "main", MainSha, "https://github.com/example-org/bookstore-api"));
    }

    [Fact]
    public async Task Resolve_UrlWithBranch_PinsThatBranch()
    {
        // Arrange
        var phase = Phase();

        // Act
        var run = await phase.ResolveAsync("https://github.com/example-org/bookstore-api/tree/feature/x", CancellationToken.None);

        // Assert
        new { run.Branch, run.CommitSha }.Should().BeEquivalentTo(new { Branch = "feature/x", CommitSha = FeatureSha });
    }

    [Fact]
    public async Task Resolve_NotAGitHubUrl_ThrowsFormatException()
    {
        // Arrange
        var phase = Phase();

        // Act
        var act = () => phase.ResolveAsync("https://gitlab.com/a/b", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FormatException>();
    }
}
