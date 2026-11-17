using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Extraction;
using BlindSpotScanner.Infrastructure.Reports;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Extraction;
using BlindSpotScanner.Infrastructure.Tests.Llm;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace BlindSpotScanner.Infrastructure.Tests.Scanning;

[Collection("scan-queue")]
public sealed class ScanOrchestratorTests(PostgresFixture db) : IAsyncLifetime
{
    private const string Books = "tests/Bookstore.IntegrationTests/BooksApiTests.cs";
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A bookstore repository with its profile, and a queued run of it that a worker has taken.</summary>
    private async Task<Guid> RunningScanAsync(decimal maxCostUsd = 5m, Guid? repositoryId = null, string modelId = "claude-sonnet-5-5")
    {
        await using var context = db.CreateContext();
        var repoId = repositoryId ?? await SeedRepositoryAsync(context);
        var run = ScanRun.Enqueue(Guid.NewGuid(), repoId, null, maxCostUsd, 1, modelId, "1.0.0", _time.GetUtcNow());
        var queue = new PostgresScanQueue(context, _time);
        await queue.EnqueueAsync(run, CancellationToken.None);
        (await queue.TryDequeueAsync(Guid.NewGuid(), TimeSpan.FromMinutes(2), CancellationToken.None)).Should().Be(run.Id);
        return run.Id;
    }

    /// <summary>Runs SQL against the run while the scan is in P3, as another worker or the reaper would.</summary>
    private RecordingEvents DuringExtract(Guid runId, Func<Guid, FormattableString> sql) => new(onProgress: async phase =>
    {
        if (phase == ScanPhases.Extract)
        {
            await using var context = db.CreateContext();
            await context.Database.ExecuteSqlAsync(sql(runId));
        }
    });

    private static async Task<Guid> SeedRepositoryAsync(Infrastructure.Persistence.BlindSpotScannerDbContext context)
    {
        var repository = new Repository { Id = Guid.NewGuid(), Owner = "example-org", Name = "bookstore", DefaultBranch = "main", HtmlUrl = "https://github.com/example-org/bookstore" };
        context.Repositories.Add(repository);
        context.RepoProfiles.Add(new RepoProfile
        {
            RepositoryId = repository.Id,
            Version = 1,
            Markdown = ScanPipelineTests.Profile.Markdown,
            IntegrationMappedTo = ScanPipelineTests.Profile.IntegrationMappedTo,
            ControllerGlobs = ScanPipelineTests.Profile.ControllerGlobs,
            MinimalApiGlobs = ScanPipelineTests.Profile.MinimalApiGlobs,
            TestGlobs = ScanPipelineTests.Profile.TestGlobs,
            IntegrationGlobs = ScanPipelineTests.Profile.IntegrationGlobs,
            IntegrationAttributes = ScanPipelineTests.Profile.IntegrationAttributes,
            IntegrationBaseTypes = ScanPipelineTests.Profile.IntegrationBaseTypes,
            NotIntegrationGlobs = ScanPipelineTests.Profile.NotIntegrationGlobs,
        });
        await context.SaveChangesAsync();
        return repository.Id;
    }

    private async Task<RunStatus?> OrchestrateAsync(Guid runId, ILlmClient? llm = null, IScanEvents? events = null, IGitHubClient? github = null)
    {
        await using var context = db.CreateContext();
        var repo = FixtureRepository.Bookstore();
        var gh = github ?? repo;
        var pipelines = new ScanPipelineFactory(gh, repo, new AspNetCoreEndpointExtractor(), new CSharpTestSyntaxReader(),
            new ReportValidator(new ReportSchemaValidator()), _time, new ScanPipelineOptions("claude-sonnet-5-5", "1.0.0", 200_000));
        var orchestrator = new ScanOrchestrator(new EfScanStore(context), events ?? new RecordingEvents(), () => llm ?? new CannedLlmClient(), pipelines,
            new CitationVerifier(gh, new CSharpTestSyntaxReader()), _time);
        return await orchestrator.RunAsync(runId, CancellationToken.None);
    }

    private async Task<(ScanRun Run, List<ScanStep> Steps)> LoadAsync(Guid runId)
    {
        await using var context = db.CreateContext();
        return (await context.ScanRuns.AsNoTracking().SingleAsync(r => r.Id == runId),
            await context.ScanSteps.AsNoTracking().Where(s => s.ScanRunId == runId).OrderBy(s => s.StartedAt).ThenBy(s => s.Phase).ToListAsync());
    }

    [Fact]
    public async Task Run_Bookstore_SucceedsAndStoresReport()
    {
        // Arrange
        var runId = await RunningScanAsync();

        // Act
        var status = await OrchestrateAsync(runId);

        // Assert
        var (run, steps) = await LoadAsync(runId);
        var report = ReportJsonSerializer.Deserialize(run.ReportJson!);
        new { status, run.Status, run.CommitSha, Steps = steps.Count, report.Validation.CitationsChecked, report.Validation.CitationsResolved }.Should().BeEquivalentTo(new
        {
            status = RunStatus.Succeeded,
            Status = RunStatus.Succeeded,
            CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
            Steps = 8,
            CitationsChecked = (int?)4,
            CitationsResolved = (int?)4,
        });
    }

    [Fact]
    public async Task Run_ForgedCitation_FailsWithCitationListed()
    {
        // Arrange
        var runId = await RunningScanAsync();

        // Act
        await OrchestrateAsync(runId, llm: new CannedLlmClient(forgeCitation: true));

        // Assert
        var (run, _) = await LoadAsync(runId);
        new { run.Status, run.FailureReasons, run.ReportJson }.Should().BeEquivalentTo(new
        {
            Status = RunStatus.Failed,
            FailureReasons = new[] { $"citation: {Books}::Deletes_book: no method named Deletes_book" },
            ReportJson = (string?)null,
        });
    }

    [Fact]
    public async Task Run_CancelRequestedDuringScan_EndsCancelled()
    {
        // Arrange (someone presses cancel while P3 runs)
        var runId = await RunningScanAsync();
        var events = new RecordingEvents(onProgress: async phase =>
        {
            if (phase == ScanPhases.Extract)
            {
                await using var context = db.CreateContext();
                var run = await context.ScanRuns.SingleAsync(r => r.Id == runId);
                run.RequestCancel();
                await context.SaveChangesAsync();
            }
        });

        // Act
        var status = await OrchestrateAsync(runId, events: events);

        // Assert
        var (loaded, steps) = await LoadAsync(runId);
        new { status, loaded.Status, loaded.ReportJson, Phases = steps.Select(s => s.Phase) }.Should().BeEquivalentTo(new
        {
            status = RunStatus.Cancelled,
            Status = RunStatus.Cancelled,
            ReportJson = (string?)null,
            Phases = new[] { "P1", "P2", "P3" },
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Run_BudgetExhausted_SucceedsWithReason()
    {
        // Arrange (each canned call costs $0.004: P4b and Books' mapping spend the $0.006 cap)
        var runId = await RunningScanAsync(maxCostUsd: 0.006m);

        // Act
        await OrchestrateAsync(runId);

        // Assert
        var (run, _) = await LoadAsync(runId);
        var report = ReportJsonSerializer.Deserialize(run.ReportJson!);
        new { run.Status, Reason = report.Inventory.UnverifiedReasons!.Contains("LLM budget of $0.006 reached; items not yet processed are unverified") }
            .Should().BeEquivalentTo(new { Status = RunStatus.Succeeded, Reason = true });
    }

    [Fact]
    public async Task Run_PreviousSucceededRun_ProducesDelta()
    {
        // Arrange
        var first = await RunningScanAsync();
        await OrchestrateAsync(first);
        var repositoryId = (await LoadAsync(first)).Run.RepositoryId;
        var second = await RunningScanAsync(repositoryId: repositoryId);

        // Act
        await OrchestrateAsync(second);

        // Assert
        var report = ReportJsonSerializer.Deserialize((await LoadAsync(second)).Run.ReportJson!);
        new { report.Delta?.BaseRunId, report.Summary.PreviousRunId }.Should().BeEquivalentTo(new { BaseRunId = (Guid?)first, PreviousRunId = (Guid?)first });
    }

    [Fact]
    public async Task Run_RepositoryGone_FailsWithMessage()
    {
        // Arrange
        var runId = await RunningScanAsync();

        // Act
        await OrchestrateAsync(runId, github: new GoneGitHub());

        // Assert
        (await LoadAsync(runId)).Run.FailureReasons.Should().Equal("Repository example-org/bookstore was not found or the token has no access to it.");
    }

    [Fact]
    public async Task Run_LeaseTakenOverMidScan_DiscardsResult()
    {
        // Arrange (this worker's lease expired and another worker took the run)
        var runId = await RunningScanAsync();
        var events = DuringExtract(runId, id => $"UPDATE scan_runs SET lease_owner = gen_random_uuid() WHERE id = {id}");

        // Act
        var status = await OrchestrateAsync(runId, events: events);

        // Assert
        var (run, _) = await LoadAsync(runId);
        new { status, run.Status, run.ReportJson, events.Completed }.Should().BeEquivalentTo(new
        {
            status = (RunStatus?)null,
            Status = RunStatus.Running,
            ReportJson = (string?)null,
            Completed = Array.Empty<RunStatus>(),
        });
    }

    [Fact]
    public async Task Run_FailedByReaperMidScan_StaysFailed()
    {
        // Arrange
        var runId = await RunningScanAsync();
        var events = DuringExtract(runId, id => $"UPDATE scan_runs SET status = 'Failed' WHERE id = {id}");

        // Act
        var status = await OrchestrateAsync(runId, events: events);

        // Assert
        new { status, (await LoadAsync(runId)).Run.Status }.Should().BeEquivalentTo(new { status = (RunStatus?)null, Status = RunStatus.Failed });
    }

    [Fact]
    public async Task Run_ProfileEditedWhileQueued_UsesQueuedVersion()
    {
        // Arrange (version 2 would find no controllers at all)
        var runId = await RunningScanAsync();
        await using (var context = db.CreateContext())
        {
            var repositoryId = (await context.ScanRuns.SingleAsync(r => r.Id == runId)).RepositoryId;
            context.RepoProfiles.Add(new RepoProfile
            {
                RepositoryId = repositoryId,
                Version = 2,
                Markdown = "# Profile v2",
                IntegrationMappedTo = IntegrationMappedTo.RealSeam,
                ControllerGlobs = ["nothing/**"],
                TestGlobs = ["tests/**/*.cs"],
            });
            await context.SaveChangesAsync();
        }

        // Act
        await OrchestrateAsync(runId);

        // Assert
        var report = ReportJsonSerializer.Deserialize((await LoadAsync(runId)).Run.ReportJson!);
        new { report.Meta.ProfileVersion, report.Summary.Endpoints.Total }.Should().BeEquivalentTo(new { ProfileVersion = 1, Total = 6 });
    }

    [Fact]
    public async Task Run_QueuedForAnotherModel_FailsBeforeScanning()
    {
        // Arrange
        var runId = await RunningScanAsync(modelId: "claude-opus-5-5");

        // Act
        await OrchestrateAsync(runId);

        // Assert
        var (run, steps) = await LoadAsync(runId);
        new { run.Status, run.FailureReasons, Steps = steps.Count }.Should().BeEquivalentTo(new
        {
            Status = RunStatus.Failed,
            FailureReasons = new[] { "This worker runs claude-sonnet-5-5 with prompts 1.0.0, but the scan was queued for claude-opus-5-5 with prompts 1.0.0." },
            Steps = 0,
        });
    }

    [Fact]
    public async Task Run_Completes_PublishesCompletedOnce()
    {
        // Arrange
        var runId = await RunningScanAsync();
        var events = new RecordingEvents();

        // Act
        await OrchestrateAsync(runId, events: events);

        // Assert
        new { events.Completed, Progress = events.Progress.Count }.Should().BeEquivalentTo(new { Completed = new[] { RunStatus.Succeeded }, Progress = 8 });
    }

    private sealed class RecordingEvents(Func<string, Task>? onProgress = null) : IScanEvents
    {
        public List<string> Progress { get; } = [];

        public List<RunStatus> Completed { get; } = [];

        public async Task ProgressAsync(Guid runId, string phase, int pct, string message, CancellationToken ct)
        {
            Progress.Add(phase);
            if (onProgress is not null)
            {
                await onProgress(phase);
            }
        }

        public Task CompletedAsync(Guid runId, RunStatus status, CancellationToken ct)
        {
            Completed.Add(status);
            return Task.CompletedTask;
        }
    }

    private sealed class GoneGitHub : IGitHubClient
    {
        public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) =>
            throw new GitHubNotFoundException($"Repository {owner}/{name} was not found or the token has no access to it.");

        public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) => throw new NotSupportedException();

        public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct) => throw new NotSupportedException();

        public Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct) => throw new NotSupportedException();

        public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct) => throw new NotSupportedException();
    }
}
