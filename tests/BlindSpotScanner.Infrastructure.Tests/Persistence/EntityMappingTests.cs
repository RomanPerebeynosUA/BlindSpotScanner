using System.Text.Json.Nodes;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Tests.Persistence;

[Collection("postgres")]
public class EntityMappingTests(PostgresFixture db)
{
    [Fact]
    public async Task Save_RepositoryWithProfile_RoundTripsGlobArrays()
    {
        // Arrange
        var repository = await db.AddRepositoryAsync();
        var profile = new RepoProfile
        {
            RepositoryId = repository.Id,
            Version = 1,
            Markdown = "# Profile",
            IntegrationMappedTo = IntegrationMappedTo.RealSeam,
            TestGlobs = ["tests/**/*IntegrationTests*/**/*.cs"],
            ControllerGlobs = ["src/**/Controllers/*.cs", "src/**/Endpoints/*.cs"],
        };
        await using (var write = db.CreateContext())
        {
            write.RepoProfiles.Add(profile);
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = db.CreateContext();
        var loaded = await read.RepoProfiles.AsNoTracking()
            .SingleAsync(p => p.RepositoryId == repository.Id && p.Version == 1);

        // Assert
        loaded.Should().BeEquivalentTo(profile);
    }

    [Fact]
    public async Task Save_RunWithStepAndFinding_RoundTripsChildren()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: false);
        var step = new ScanStep
        {
            Id = Guid.NewGuid(),
            ScanRunId = run.Id,
            Phase = "P1",
            Status = RunStatus.Succeeded,
            StartedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 11, 15, 9, 0, 5, TimeSpan.Zero),
            InputTokens = 1200,
            OutputTokens = 300,
            CostUsd = 0.012m,
            ReadInFull = 3,
            Sampled = 0,
            Notes = "tree walked",
        };
        var finding = new Finding
        {
            Id = Guid.NewGuid(),
            ScanRunId = run.Id,
            ReportFindingId = "f2",
            Kind = FindingKind.UncoveredEndpoint,
            Severity = Severity.High,
            StableKey = "uncovered-endpoint:POST api/orders/{}/cancel",
            Headline = "Cancelling an order has no integration test",
            Detail = ["POST api/orders/{id}/cancel"],
            Evidence = [],
        };
        await using (var write = db.CreateContext())
        {
            write.ScanSteps.Add(step);
            write.Findings.Add(finding);
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = db.CreateContext();
        var children = new
        {
            Steps = await read.ScanSteps.AsNoTracking().Where(s => s.ScanRunId == run.Id).ToListAsync(),
            Findings = await read.Findings.AsNoTracking().Where(f => f.ScanRunId == run.Id).ToListAsync(),
        };

        // Assert
        children.Should().BeEquivalentTo(new { Steps = new[] { step }, Findings = new[] { finding } });
    }

    [Fact]
    public async Task Save_RemediationAction_IsFoundByRepositoryAndStableKey()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: false);
        var finding = new Finding
        {
            Id = Guid.NewGuid(),
            ScanRunId = run.Id,
            ReportFindingId = "f4",
            Kind = FindingKind.UncoveredSeam,
            Severity = Severity.High,
            StableKey = "uncovered-seam:payment-gateway",
            Headline = "The payment provider is never called for real in tests",
            Detail = [],
            Evidence = [],
        };
        var action = new RemediationAction
        {
            Id = Guid.NewGuid(),
            RepositoryId = run.RepositoryId,
            StableKey = finding.StableKey,
            CreatedFromFindingId = finding.Id,
            Type = ActionType.JiraTicket,
            ExternalKey = "BSS-12",
            Url = "https://example.atlassian.net/browse/BSS-12",
            Status = "Open",
            CreatedAt = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero),
            CreatedBy = "dev@example.com",
        };
        await using (var write = db.CreateContext())
        {
            write.Findings.Add(finding);
            write.RemediationActions.Add(action);
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = db.CreateContext();
        var found = await read.RemediationActions.AsNoTracking()
            .SingleAsync(a => a.RepositoryId == run.RepositoryId && a.StableKey == "uncovered-seam:payment-gateway");

        // Assert
        found.Should().BeEquivalentTo(action);
    }

    [Fact]
    public async Task Save_CacheEntries_RoundTrip()
    {
        // Arrange
        var file = new FileCacheEntry { BlobSha = Guid.NewGuid().ToString("N"), Content = "public class BooksController {}" };
        var llm = new LlmCacheEntry { Key = Guid.NewGuid().ToString("N"), ResponseJson = "{\"items\":[1,2]}" };
        await using (var write = db.CreateContext())
        {
            write.FileCache.Add(file);
            write.LlmCache.Add(llm);
            await write.SaveChangesAsync();
        }

        // Act
        await using var read = db.CreateContext();
        var loaded = new
        {
            File = await read.FileCache.AsNoTracking().SingleAsync(f => f.BlobSha == file.BlobSha),
            Llm = await read.LlmCache.AsNoTracking().SingleAsync(l => l.Key == llm.Key),
        };

        // Assert (jsonb keeps the JSON value, not its text, so the response is compared semantically)
        new { loaded.File, LlmKey = loaded.Llm.Key, LlmJsonEqual = JsonNode.DeepEquals(JsonNode.Parse(loaded.Llm.ResponseJson), JsonNode.Parse(llm.ResponseJson)) }
            .Should().BeEquivalentTo(new { File = file, LlmKey = llm.Key, LlmJsonEqual = true });
    }
}
