using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Remediation;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Tests.Remediation;

[Collection("postgres")]
public class EfRemediationStoreTests(PostgresFixture db)
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private async Task<(ScanRun Run, ReportFinding Finding)> SeedRunAsync(Guid? repositoryId = null)
    {
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: true, repositoryId);
        var finding = PostgresFixture.LoadReport("sample-report.json", run.Id).Findings.Single(f => f.Id == "f2");
        return (run, finding);
    }

    private static RemediationAction Action(Guid repositoryId, Guid findingId, string key, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = repositoryId,
        StableKey = "uncovered-endpoint:POST api/orders/{}/cancel",
        CreatedFromFindingId = findingId,
        Type = ActionType.JiraTicket,
        ExternalKey = key,
        Url = $"https://example.atlassian.net/browse/{key}",
        Status = "To Do",
        StatusCheckedAt = createdAt,
        CreatedAt = createdAt,
        CreatedBy = "anonymous",
    };

    [Fact]
    public async Task EnsureFindingAsync_FirstCall_InsertsRowFromReport()
    {
        // Arrange
        var (run, finding) = await SeedRunAsync();
        await using var context = db.CreateContext();

        // Act
        var row = await new EfRemediationStore(context).EnsureFindingAsync(run.Id, finding, CancellationToken.None);

        // Assert
        new { row.ReportFindingId, row.StableKey, row.Kind }.Should().Be(new
        {
            ReportFindingId = "f2",
            StableKey = "uncovered-endpoint:POST api/orders/{}/cancel",
            Kind = FindingKind.UncoveredEndpoint,
        });
    }

    [Fact]
    public async Task EnsureFindingAsync_SecondCall_ReturnsSameRow()
    {
        // Arrange
        var (run, finding) = await SeedRunAsync();
        Guid first;
        await using (var context = db.CreateContext())
        {
            first = (await new EfRemediationStore(context).EnsureFindingAsync(run.Id, finding, CancellationToken.None)).Id;
        }

        // Act
        await using var again = db.CreateContext();
        var second = await new EfRemediationStore(again).EnsureFindingAsync(run.Id, finding, CancellationToken.None);

        // Assert
        new { second.Id, Rows = await again.Findings.CountAsync(f => f.ScanRunId == run.Id) }.Should().Be(new { Id = first, Rows = 1 });
    }

    [Fact]
    public async Task EnsureFindingAsync_ConcurrentCalls_AllReturnTheOneRow()
    {
        // Arrange (two tabs creating a ticket for the same finding at once race on the unique index)
        var (run, finding) = await SeedRunAsync();

        // Act
        var rows = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = db.CreateContext();
            return await new EfRemediationStore(context).EnsureFindingAsync(run.Id, finding, CancellationToken.None);
        }));

        // Assert
        rows.Select(r => r.Id).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task FindActionAsync_TwoActions_ReturnsNewest()
    {
        // Arrange
        var (run, finding) = await SeedRunAsync();
        await using (var context = db.CreateContext())
        {
            var store = new EfRemediationStore(context);
            var row = await store.EnsureFindingAsync(run.Id, finding, CancellationToken.None);
            await store.AddActionAsync(Action(run.RepositoryId, row.Id, "BSS-1", At), CancellationToken.None);
            await store.AddActionAsync(Action(run.RepositoryId, row.Id, "BSS-2", At.AddHours(1)), CancellationToken.None);
        }

        // Act
        await using var read = db.CreateContext();
        var found = await new EfRemediationStore(read).FindActionAsync(run.RepositoryId, finding.StableKey, ActionType.JiraTicket, CancellationToken.None);

        // Assert
        found!.ExternalKey.Should().Be("BSS-2");
    }

    [Fact]
    public async Task ActionsAsync_OtherRepository_Excluded()
    {
        // Arrange
        var (run, finding) = await SeedRunAsync();
        var (other, otherFinding) = await SeedRunAsync();
        await using (var context = db.CreateContext())
        {
            var store = new EfRemediationStore(context);
            var row = await store.EnsureFindingAsync(run.Id, finding, CancellationToken.None);
            var otherRow = await store.EnsureFindingAsync(other.Id, otherFinding, CancellationToken.None);
            await store.AddActionAsync(Action(run.RepositoryId, row.Id, "BSS-1", At), CancellationToken.None);
            await store.AddActionAsync(Action(other.RepositoryId, otherRow.Id, "BSS-9", At), CancellationToken.None);
        }

        // Act
        await using var read = db.CreateContext();
        var actions = await new EfRemediationStore(read).ActionsAsync(run.RepositoryId, CancellationToken.None);

        // Assert
        actions.Select(a => a.ExternalKey).Should().Equal("BSS-1");
    }

    [Fact]
    public async Task SaveChangesAsync_StatusChanged_Persists()
    {
        // Arrange
        var (run, finding) = await SeedRunAsync();
        await using (var context = db.CreateContext())
        {
            var store = new EfRemediationStore(context);
            var row = await store.EnsureFindingAsync(run.Id, finding, CancellationToken.None);
            await store.AddActionAsync(Action(run.RepositoryId, row.Id, "BSS-1", At), CancellationToken.None);
        }

        await using (var context = db.CreateContext())
        {
            var store = new EfRemediationStore(context);
            var action = (await store.ActionsAsync(run.RepositoryId, CancellationToken.None)).Single();
            action.Status = "Done";
            action.StatusCheckedAt = At.AddHours(2);

            // Act
            await store.SaveChangesAsync(CancellationToken.None);
        }

        // Assert
        await using var read = db.CreateContext();
        var saved = await read.RemediationActions.AsNoTracking().SingleAsync(a => a.RepositoryId == run.RepositoryId);
        new { saved.Status, saved.StatusCheckedAt }.Should().Be(new { Status = "Done", StatusCheckedAt = At.AddHours(2) });
    }
}
