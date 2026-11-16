using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlindSpotScanner.Infrastructure.Tests.Persistence;

/// <summary>The DB trigger keeps a succeeded run's report immutable, even against raw SQL.</summary>
[Collection("postgres")]
public class ScanRunImmutabilityTests(PostgresFixture db)
{
    [Fact]
    public async Task Update_ReportOfSucceededRun_IsRejectedByDatabase()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: true);
        await using var context = db.CreateContext();

        // Act
        var act = () => context.Database.ExecuteSqlAsync($"UPDATE scan_runs SET report_json = '{{}}'::jsonb WHERE id = {run.Id}");

        // Assert
        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("immutable");
    }

    [Fact]
    public async Task Update_StatusOfSucceededRun_IsRejectedByDatabase()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: true);
        await using var context = db.CreateContext();

        // Act
        var act = () => context.Database.ExecuteSqlAsync($"UPDATE scan_runs SET status = 'Running' WHERE id = {run.Id}");

        // Assert
        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("immutable");
    }

    [Fact]
    public async Task Update_HeadlineScoreOfSucceededRun_IsRejectedByDatabase()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: true);
        await using var context = db.CreateContext();

        // Act
        var act = () => context.Database.ExecuteSqlAsync($"UPDATE scan_runs SET score = 9.9 WHERE id = {run.Id}");

        // Assert
        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("immutable");
    }

    [Fact]
    public async Task Update_ReportOfRunningRun_IsAllowed()
    {
        // Arrange
        var run = await db.AddRunWithReportAsync("sample-report.json", succeed: false);
        await using var context = db.CreateContext();

        // Act
        var rows = await context.Database.ExecuteSqlAsync($"UPDATE scan_runs SET report_json = '{{}}'::jsonb WHERE id = {run.Id}");

        // Assert
        rows.Should().Be(1);
    }
}
