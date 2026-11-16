using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.GitHub;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.GitHub;

/// <summary>The M3 acceptance criterion: with WireMock fixtures, P1–P2 produce the inventory counts.</summary>
public sealed class ResolveAndEnumerateTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private void Serve(string path, string fixture) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GitHub", "Fixtures", fixture))));

    [Fact]
    public async Task ResolveAndEnumerate_BookstoreFixture_ProducesInventoryCounts()
    {
        // Arrange
        Serve("/repos/example-org/bookstore-api", "bookstore-repo.json");
        Serve("/repos/example-org/bookstore-api/branches/main", "bookstore-branch.json");
        Serve("/repos/example-org/bookstore-api/git/trees/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "bookstore-tree.json");
        var github = new GitHubClient(new HttpClient(), Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = "ghp_test" }));
        var profile = new RepoProfile
        {
            RepositoryId = Guid.NewGuid(),
            Version = 1,
            Markdown = "# Bookstore",
            IntegrationMappedTo = IntegrationMappedTo.RealSeam,
            ControllerGlobs = ["**/Controllers/*Controller.cs"],
            TestGlobs = ["tests/**/*.cs"],
        };

        // Act
        var run = await new ResolvePhase(github).ResolveAsync("https://github.com/example-org/bookstore-api", CancellationToken.None);
        var inventory = await new EnumeratePhase(github).EnumerateAsync(run, profile, CancellationToken.None);

        // Assert (examples/sample-report.json: inventory.controllerFiles = 2, testFilesTotal = 10)
        new { run.CommitSha, inventory.ControllerFileCount, inventory.TestFileCount, inventory.UnverifiedReasons }.Should().BeEquivalentTo(new
        {
            CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
            ControllerFileCount = 2,
            TestFileCount = 10,
            UnverifiedReasons = Array.Empty<string>(),
        });
    }
}
