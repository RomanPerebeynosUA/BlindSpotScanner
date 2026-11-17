using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Persistence;
using BlindSpotScanner.Infrastructure.Scanning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// The api over a real PostgreSQL (Testcontainers), with GitHub and Claude served by WireMock. The
/// scan worker runs in the same host here (in production it is its own service), so a POSTed scan
/// really goes through the queue, the worker, the pipeline and the store.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16").Build();

    public FakeGitHubServer GitHub { get; } = new();

    public FakeClaudeServer Claude { get; } = new();

    protected virtual bool RunWorker => true;

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        GitHub.Dispose();
        Claude.Dispose();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _db.GetConnectionString());
        builder.UseSetting("GitHub:ApiBaseUrl", GitHub.Url);
        builder.UseSetting("Llm:BaseUrl", Claude.Url);
        builder.UseSetting("Llm:ApiKey", "test-key");
        builder.UseSetting("Scan:PollMilliseconds", "500");
        builder.UseSetting("Scan:HeartbeatSeconds", "1");
        builder.ConfigureTestServices(services =>
        {
            if (RunWorker)
            {
                services.AddHostedService<ScanWorker>();
            }
        });
    }

    /// <summary>
    /// A bookstore repository (optionally with a profile). Each profile's Markdown is unique, so LLM
    /// answers cached by an earlier test never serve this one; <paramref name="marker"/> lets a test
    /// steer the fake Claude (see <see cref="FakeClaudeServer.ForgeMarker"/>).
    /// </summary>
    public async Task<Guid> SeedRepositoryAsync(bool withProfile = true, string marker = "")
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindSpotScannerDbContext>();
        // Names are unique (D22); the fake GitHub serves the bookstore under any name of its owner.
        var name = $"{FakeGitHubServer.Name}-{Guid.NewGuid():N}";
        var repository = new Repository { Id = Guid.NewGuid(), Owner = FakeGitHubServer.Owner, Name = name, DefaultBranch = "main", HtmlUrl = $"https://github.com/{FakeGitHubServer.Owner}/{name}" };
        db.Repositories.Add(repository);
        if (withProfile)
        {
            db.RepoProfiles.Add(new RepoProfile
            {
                RepositoryId = repository.Id,
                Version = 1,
                Markdown = $"# Profile — bookstore {marker} ({repository.Id})",
                IntegrationMappedTo = IntegrationMappedTo.RealSeam,
                ControllerGlobs = ["src/**/Controllers/**/*Controller.cs"],
                MinimalApiGlobs = ["src/**/Endpoints/**/*.cs"],
                TestGlobs = ["tests/**/*.cs"],
                IntegrationGlobs = ["tests/*.IntegrationTests/**"],
                IntegrationAttributes = ["Trait(\"Category\",\"Integration\")"],
                IntegrationBaseTypes = ["WebApplicationFactory"],
                NotIntegrationGlobs = ["**/*Fixture.cs"],
            });
        }

        await db.SaveChangesAsync();
        return repository.Id;
    }

    /// <summary>
    /// A succeeded run carrying an example report under <paramref name="runId"/>. The example's fixed
    /// ids are rewritten (run id, previous run id, the stored delta's base) so tests don't collide and
    /// the stored-delta link points at <paramref name="previousRunId"/>.
    /// </summary>
    public async Task SeedSucceededRunAsync(Guid repositoryId, string fixture, Guid runId, Guid? previousRunId)
    {
        var report = ReportJsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
        report = report with
        {
            Meta = report.Meta with { RunId = runId },
            Summary = report.Summary with { PreviousRunId = previousRunId },
            Delta = report.Delta is null || previousRunId is null ? report.Delta : report.Delta with { BaseRunId = previousRunId.Value },
        };
        var meta = report.Meta;
        var run = new ScanRun(runId, repositoryId, meta.Branch, meta.CommitSha, meta.ModelId, meta.PromptSetVersion, meta.ProfileVersion);
        run.RecordReport(report);
        run.Succeed();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindSpotScannerDbContext>();
        db.ScanRuns.Add(run);
        await db.SaveChangesAsync();
    }
}

/// <summary>The api without a worker: scans stay queued (for cancel and validation tests).</summary>
public sealed class QueueOnlyApiFactory : ApiFactory
{
    protected override bool RunWorker => false;
}
