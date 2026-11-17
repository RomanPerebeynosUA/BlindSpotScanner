using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Infrastructure.Repositories;
using BlindSpotScanner.Infrastructure.Scanning;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Repositories;

[Collection("postgres")]
public class EfRepositoryStoreTests(PostgresFixture db)
{
    private static Repository NewRepository(string name) => new()
    {
        Id = Guid.NewGuid(),
        Owner = "example-org",
        Name = name,
        DefaultBranch = "main",
        HtmlUrl = $"https://github.com/example-org/{name}",
    };

    private static string UniqueName() => $"store-{Guid.NewGuid():N}";

    private async Task<Repository> AddAsync(string name)
    {
        var repository = NewRepository(name);
        await using var context = db.CreateContext();
        await new EfRepositoryStore(context).TryAddAsync(repository, ProfileTemplate.Default(repository.Id, name), CancellationToken.None);
        return repository;
    }

    private async Task<ScanRun> EnqueueAsync(Guid repositoryId, DateTimeOffset queuedAt)
    {
        var run = ScanRun.Enqueue(Guid.NewGuid(), repositoryId, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", queuedAt);
        await using var context = db.CreateContext();
        await new PostgresScanQueue(context).EnqueueAsync(run, CancellationToken.None);
        return run;
    }

    [Fact]
    public async Task TryAddAsync_DuplicateName_ReturnsFalse()
    {
        // Arrange
        var name = UniqueName();
        await AddAsync(name);
        var duplicate = NewRepository(name);
        await using var context = db.CreateContext();

        // Act
        var added = await new EfRepositoryStore(context).TryAddAsync(duplicate, ProfileTemplate.Default(duplicate.Id, name), CancellationToken.None);

        // Assert
        added.Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_SucceededAndQueuedRuns_UsesLatestSucceededAndActive()
    {
        // Arrange
        var repository = await AddAsync(UniqueName());
        var succeeded = await db.AddRunWithReportAsync("sample-report.json", succeed: true, repository.Id);
        var queued = await EnqueueAsync(repository.Id, DateTimeOffset.UtcNow);
        await using var context = db.CreateContext();

        // Act
        var list = await new EfRepositoryStore(context).ListAsync(CancellationToken.None);

        // Assert
        list.Should().ContainSingle(r => r.Id == repository.Id).Which.Should().BeEquivalentTo(new
        {
            FullName = $"example-org/{repository.Name}",
            DefaultBranch = "main",
            LastRunId = (Guid?)succeeded.Id,
            LastScore = (decimal?)5.1m,
            LastCoveredShare = (decimal?)(3m / 5m),
            LastScanAt = succeeded.GeneratedAt,
            ActiveRunId = (Guid?)queued.Id,
        });
    }

    [Fact]
    public async Task ListAsync_NoRuns_HeadlineIsNull()
    {
        // Arrange
        var repository = await AddAsync(UniqueName());
        await using var context = db.CreateContext();

        // Act
        var list = await new EfRepositoryStore(context).ListAsync(CancellationToken.None);

        // Assert
        list.Should().ContainSingle(r => r.Id == repository.Id).Which.Should().BeEquivalentTo(new
        {
            LastRunId = (Guid?)null,
            LastScore = (decimal?)null,
            LastCoveredShare = (decimal?)null,
            LastScanAt = (DateTimeOffset?)null,
            ActiveRunId = (Guid?)null,
        });
    }

    [Fact]
    public async Task TryAddAsync_OtherUniqueViolation_Throws()
    {
        // Arrange (the profile key clashes with an existing repository's profile; only a duplicate name means "already registered")
        var existing = await AddAsync(UniqueName());
        var repository = NewRepository(UniqueName());
        await using var context = db.CreateContext();

        // Act
        var act = () => new EfRepositoryStore(context).TryAddAsync(repository, ProfileTemplate.Default(existing.Id, "x"), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task ListAsync_SucceededRunsAtSameTime_PicksTheHigherId()
    {
        // Arrange (same GeneratedAt; the lower id is saved first)
        var repository = await AddAsync(UniqueName());
        var low = Guid.Parse($"10000000-0000-4000-8000-{Guid.NewGuid():N}"[..36]);
        var high = Guid.Parse($"f0000000-0000-4000-8000-{Guid.NewGuid():N}"[..36]);
        await AddSucceededRunAsync(repository.Id, low);
        await AddSucceededRunAsync(repository.Id, high);
        await using var context = db.CreateContext();

        // Act
        var list = await new EfRepositoryStore(context).ListAsync(CancellationToken.None);

        // Assert
        list.Single(r => r.Id == repository.Id).LastRunId.Should().Be(high);
    }

    [Fact]
    public async Task ListAsync_OnlyFailedRun_ReportsLatestRunStatus()
    {
        // Arrange
        var repository = await AddAsync(UniqueName());
        var run = ScanRun.Enqueue(Guid.NewGuid(), repository.Id, null, 5m, 1, "claude-sonnet-5-5", "1.0.0", DateTimeOffset.UtcNow);
        run.Fail(["Unresolved citation"]);
        await using (var write = db.CreateContext())
        {
            write.ScanRuns.Add(run);
            await write.SaveChangesAsync();
        }

        await using var context = db.CreateContext();

        // Act
        var list = await new EfRepositoryStore(context).ListAsync(CancellationToken.None);

        // Assert
        list.Single(r => r.Id == repository.Id).Should().BeEquivalentTo(new { LastRunId = (Guid?)null, LatestRunStatus = (RunStatus?)RunStatus.Failed });
    }

    private async Task AddSucceededRunAsync(Guid repositoryId, Guid runId)
    {
        var report = PostgresFixture.LoadReport("sample-report.json", runId);
        var meta = report.Meta;
        var run = new ScanRun(runId, repositoryId, meta.Branch, meta.CommitSha, meta.ModelId, meta.PromptSetVersion, meta.ProfileVersion);
        run.RecordReport(report);
        run.Succeed();
        await using var context = db.CreateContext();
        context.ScanRuns.Add(run);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task RunsAsync_MixedRuns_NewestFirst()
    {
        // Arrange
        var repository = await AddAsync(UniqueName());
        var succeeded = await db.AddRunWithReportAsync("sample-report.json", succeed: true, repository.Id); // generated 2026-11-15
        var older = await EnqueueAsync(repository.Id, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = await EnqueueAsync(repository.Id, new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        await using var context = db.CreateContext();

        // Act
        var runs = await new EfRepositoryStore(context).RunsAsync(repository.Id, CancellationToken.None);

        // Assert
        runs.Select(r => r.Id).Should().Equal(newer.Id, succeeded.Id, older.Id);
    }
}
