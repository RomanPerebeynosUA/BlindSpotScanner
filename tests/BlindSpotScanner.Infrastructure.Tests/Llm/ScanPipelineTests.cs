using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.Reports;
using BlindSpotScanner.Infrastructure.Tests.Extraction;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

/// <summary>M5 acceptance (doc 11): with a fake LLM returning canned JSON, the full pipeline produces a valid report.</summary>
public class ScanPipelineTests
{
    public static readonly RepoProfile Profile = new()
    {
        RepositoryId = Guid.NewGuid(),
        Version = 1,
        Markdown = "# Profile — bookstore",
        IntegrationMappedTo = IntegrationMappedTo.RealSeam,
        ControllerGlobs = ["src/**/Controllers/**/*Controller.cs"],
        MinimalApiGlobs = ["src/**/Endpoints/**/*.cs"],
        TestGlobs = ["tests/**/*.cs"],
        IntegrationGlobs = ["tests/*.IntegrationTests/**"],
        IntegrationAttributes = ["Trait(\"Category\",\"Integration\")"],
        IntegrationBaseTypes = ["WebApplicationFactory"],
        NotIntegrationGlobs = ["**/*Fixture.cs"],
    };

    private static Task<ScanOutcome> ScanAsync(ILlmClient llm, IntegrationReport? previous = null, IScanObserver? observer = null)
    {
        var repo = FixtureRepository.Bookstore();
        var pipeline = new ScanPipeline(
            repo, repo, new AspNetCoreEndpointExtractor(), new CSharpTestSyntaxReader(), llm,
            new ReportValidator(new ReportSchemaValidator()),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero)),
            new ScanPipelineOptions("claude-sonnet-5-5", "1.0.0", 200_000));
        return pipeline.RunAsync(Guid.NewGuid(), "https://github.com/example-org/bookstore", Profile, previous, observer ?? NoScanObserver.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Pipeline_CannedLlm_ProducesValidReport()
    {
        // Act
        var outcome = await ScanAsync(new CannedLlmClient());

        // Assert
        new { outcome.Violations, outcome.Report.Validation.Passed, outcome.Report.Inventory.EndpointsTotal }.Should().BeEquivalentTo(new
        {
            Violations = Array.Empty<string>(),
            Passed = true,
            EndpointsTotal = 6,
        });
    }

    [Fact]
    public async Task Pipeline_CannedLlm_SummaryMatchesCannedGrades()
    {
        // Act
        var outcome = await ScanAsync(new CannedLlmClient());

        // Assert (Books 2 covered + 1 weak, Orders 1 covered + 1 none; no test mentions health, and the
        // route filter is a heuristic, so its absence is unverified rather than a confirmed none)
        outcome.Report.Summary.Endpoints.Should().BeEquivalentTo(new StatusCounts { Total = 6, Covered = 3, Weak = 1, Skipped = 0, None = 1, Unverified = 1 });
    }

    [Fact]
    public async Task Pipeline_LlmAlwaysFails_ReportStillValid()
    {
        // Act
        var outcome = await ScanAsync(new FailingLlmClient());

        // Assert
        new
        {
            outcome.Violations,
            Unverified = outcome.Report.Summary.Endpoints.Unverified,
            outcome.Report.Summary.QualityScore,
        }.Should().BeEquivalentTo(new { Violations = Array.Empty<string>(), Unverified = 6, QualityScore = (decimal?)null });
    }

    [Fact]
    public async Task Pipeline_Observer_SeesEveryPhaseInOrder()
    {
        // Arrange
        var observer = new RecordingObserver();

        // Act
        await ScanAsync(new CannedLlmClient(), observer: observer);

        // Assert
        observer.Events.Should().Equal(ScanPhases.All.SelectMany(p => new[] { $"start {p}", $"end {p}" }));
    }

    [Fact]
    public async Task Pipeline_PreviousReport_ProducesDelta()
    {
        // Arrange
        var first = await ScanAsync(new CannedLlmClient());

        // Act
        var second = await ScanAsync(new CannedLlmClient(), previous: first.Report);

        // Assert
        new { Comparable = second.Report.Delta?.Comparable, second.Violations }.Should().BeEquivalentTo(new { Comparable = (bool?)true, Violations = Array.Empty<string>() });
    }

    private sealed class RecordingObserver : IScanObserver
    {
        public List<string> Events { get; } = [];

        public Task PhaseStartedAsync(string phase, CancellationToken ct)
        {
            Events.Add($"start {phase}");
            return Task.CompletedTask;
        }

        public Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct)
        {
            Events.Add($"end {report.Phase}");
            return Task.CompletedTask;
        }

        public Task PhaseFailedAsync(string phase, string reason, CancellationToken ct)
        {
            Events.Add($"failed {phase}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingLlmClient : ILlmClient
    {
        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct) =>
            Task.FromResult(new LlmResult(null, ["the answer is not valid JSON"], CannedLlmClient.CallUsage, FromCache: false));
    }
}
