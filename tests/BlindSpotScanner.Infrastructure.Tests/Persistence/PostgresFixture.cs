using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BlindSpotScanner.Infrastructure.Tests.Persistence;

/// <summary>One PostgreSQL 16 container per test run, migrated once.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public BlindSpotScannerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<BlindSpotScannerDbContext>()
            .UseBlindSpotScannerNpgsql(_container.GetConnectionString())
            .Options);

    public static IntegrationReport LoadReport(string fixture, Guid runId)
    {
        var report = ReportJsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)));
        return report with { Meta = report.Meta with { RunId = runId } };
    }

    public async Task<Repository> AddRepositoryAsync()
    {
        var repository = new Repository
        {
            Id = Guid.NewGuid(),
            Owner = "example-org",
            Name = "bookstore-api",
            DefaultBranch = "main",
            HtmlUrl = "https://github.com/example-org/bookstore-api",
        };
        await using var context = CreateContext();
        context.Repositories.Add(repository);
        await context.SaveChangesAsync();
        return repository;
    }

    /// <summary>Saves a run carrying the fixture's report; succeeded when <paramref name="succeed"/> is true.</summary>
    public async Task<ScanRun> AddRunWithReportAsync(string fixture, bool succeed)
    {
        var repository = await AddRepositoryAsync();
        var report = LoadReport(fixture, Guid.NewGuid());
        var meta = report.Meta;
        var run = new ScanRun(meta.RunId, repository.Id, meta.Branch, meta.CommitSha, meta.ModelId, meta.PromptSetVersion, meta.ProfileVersion);
        run.RecordReport(report);
        if (succeed)
        {
            run.Succeed();
        }

        await using var context = CreateContext();
        context.ScanRuns.Add(run);
        await context.SaveChangesAsync();
        return run;
    }

    /// <summary>Empties every table; only for collections that own their container (the scan queue sees all runs).</summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE repositories, scan_runs, scan_steps, findings, remediation_actions, repo_profiles CASCADE");
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

/// <summary>Its own container: the queue takes any queued run, so these tests must not see runs from other collections.</summary>
[CollectionDefinition("scan-queue")]
public sealed class ScanQueueCollection : ICollectionFixture<PostgresFixture>;
