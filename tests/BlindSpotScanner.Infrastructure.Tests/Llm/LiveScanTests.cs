using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Llm;
using BlindSpotScanner.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

/// <summary>M5's opt-in live test: a real scan of a small public ASP.NET Core repo with the real Claude API.</summary>
public class LiveScanTests
{
    private static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile — live sample (generic ASP.NET Core conventions)",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["**/Controllers/**/*Controller.cs"],
        MinimalApiGlobs = ["**/Program.cs", "**/Endpoints/**/*.cs"],
        TestGlobs = ["**/*Tests/**/*.cs", "**/*Tests.cs"],
        IntegrationGlobs = ["**/*IntegrationTests/**", "**/*IntegrationTests.cs"],
        IntegrationBaseTypes = ["WebApplicationFactory"],
        NotIntegrationGlobs = ["**/*Fixture.cs"],
    };

    [LiveFact]
    public async Task LiveScan_PublicRepo_ProducesValidReport()
    {
        // Arrange
        var gitHubOptions = Options.Create(new GitHubOptions { Token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") });
        var github = new GitHubClient(new HttpClient(new GitHubRateLimitHandler(TimeProvider.System, gitHubOptions) { InnerHandler = new HttpClientHandler() }), gitHubOptions);
        var llmOptions = Options.Create(new LlmOptions());
        var pipeline = new ScanPipeline(
            github, new DirectBlobReader(github), new AspNetCoreEndpointExtractor(), new CSharpTestSyntaxReader(),
            new ClaudeLlmClient(new PromptLibrary(), llmOptions), new ReportValidator(new ReportSchemaValidator()), TimeProvider.System,
            new ScanPipelineOptions(llmOptions.Value.Model, llmOptions.Value.PromptSetVersion, 200_000));

        // Act
        var outcome = await pipeline.RunAsync(Guid.NewGuid(), Environment.GetEnvironmentVariable("BSS_LIVE_REPO_URL")!, Profile, previous: null, NoScanObserver.Instance, CancellationToken.None);

        // Assert
        outcome.Violations.Should().BeEmpty();
    }

    private sealed class DirectBlobReader(IGitHubClient github) : IBlobReader
    {
        public Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct) => github.GetBlobTextAsync(owner, name, blobSha, ct);
    }
}
