using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlindSpotScanner.Infrastructure.Tests.Persistence;

/// <summary>The M1 acceptance criterion (D10): a canonical, byte-equivalent round-trip that passes the schema.</summary>
[Collection("postgres")]
public class ReportRoundTripTests(PostgresFixture db)
{
    [Theory]
    [InlineData("sample-report.json")]
    [InlineData("sample-report-previous.json")]
    public async Task RoundTrip_ExampleReport_IsByteEquivalentInCanonicalForm(string fixture)
    {
        // Arrange
        var saved = await db.AddRunWithReportAsync(fixture, succeed: true);

        // Act
        await using var context = db.CreateContext();
        var loaded = await context.ScanRuns.AsNoTracking().SingleAsync(run => run.Id == saved.Id);
        var reloaded = ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(loaded.ReportJson!));

        // Assert
        reloaded.Should().Be(saved.ReportJson);
    }

    [Theory]
    [InlineData("sample-report.json")]
    [InlineData("sample-report-previous.json")]
    public async Task RoundTrip_ExampleReport_PassesSchemaValidation(string fixture)
    {
        // Arrange
        var saved = await db.AddRunWithReportAsync(fixture, succeed: true);

        // Act
        await using var context = db.CreateContext();
        var loaded = await context.ScanRuns.AsNoTracking().SingleAsync(run => run.Id == saved.Id);
        var reloaded = ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(loaded.ReportJson!));

        // Assert
        new ReportSchemaValidator().Validate(reloaded).Should().BeEmpty();
    }
}
