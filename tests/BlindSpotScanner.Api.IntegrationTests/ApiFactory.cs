using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Persistence;
using BlindSpotScanner.Infrastructure.Scanning;
using Microsoft.AspNetCore.Authentication;
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

    public FakeJiraServer Jira { get; } = new();

    protected virtual bool RunWorker => true;

    /// <summary>False leaves the Jira API token unset, so Jira counts as not configured (D24).</summary>
    protected virtual bool JiraConfigured => true;

    /// <summary>False is read-only mode: no fix PRs (D25).</summary>
    protected virtual bool PullRequests => true;

    public Task InitializeAsync() => _db.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        GitHub.Dispose();
        Claude.Dispose();
        Jira.Dispose();
        await _db.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _db.GetConnectionString());
        builder.UseSetting("GitHub:ApiBaseUrl", GitHub.Url);
        builder.UseSetting("GitHub:Token", "ghp_test-token");
        builder.UseSetting("Features:PullRequests", PullRequests ? "true" : "false");
        builder.UseSetting("Llm:BaseUrl", Claude.Url);
        builder.UseSetting("Llm:ApiKey", "test-key");
        builder.UseSetting("Scan:PollMilliseconds", "500");
        builder.UseSetting("Scan:HeartbeatSeconds", "1");
        builder.UseSetting("Jira:BaseUrl", Jira.Url);
        builder.UseSetting("Jira:Email", "dev@example.com");
        builder.UseSetting("Jira:ApiToken", JiraConfigured ? "jira-test-token" : string.Empty);
        builder.UseSetting("Jira:ProjectKey", "BSS");
        builder.ConfigureTestServices(services =>
        {
            // The real policies, with the role chosen per request (TestAuthHandler).
            services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
            if (RunWorker)
            {
                services.AddHostedService<ScanWorker>();
            }
        });
    }

    /// <summary>A client signed in with <paramref name="role"/> ("none" = anonymous).</summary>
    public HttpClient CreateClient(string role, string? user = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
        }

        return client;
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
    /// <param name="repoName">Rewrites the report's repository name (the fake GitHub serves any name), so tests get separate GitHub call logs.</param>
    public async Task SeedSucceededRunAsync(Guid repositoryId, string fixture, Guid runId, Guid? previousRunId, string? repoName = null)
    {
        var report = ReportJsonSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
        report = report with
        {
            Meta = report.Meta with
            {
                RunId = runId,
                Repo = repoName is null ? report.Meta.Repo : report.Meta.Repo with { Name = repoName, Url = $"https://github.com/{report.Meta.Repo.Owner}/{repoName}" },
            },
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

/// <summary>The api without a worker and without a Jira API token.</summary>
public sealed class NoJiraApiFactory : ApiFactory
{
    protected override bool RunWorker => false;

    protected override bool JiraConfigured => false;
}

/// <summary>The api without a worker in read-only mode (Features:PullRequests=false).</summary>
public sealed class ReadOnlyApiFactory : ApiFactory
{
    protected override bool RunWorker => false;

    protected override bool PullRequests => false;
}
