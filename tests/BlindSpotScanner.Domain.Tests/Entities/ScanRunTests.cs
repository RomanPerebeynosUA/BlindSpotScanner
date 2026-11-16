using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Domain.Tests.Entities;

public class ScanRunTests
{
    private static readonly Guid SampleRunId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static IntegrationReport SampleReport() =>
        ReportJsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-report.json")));

    private static ScanRun NewRun(Guid id) =>
        new(id, Guid.NewGuid(), "main", "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "claude-sonnet-x", "1.0.0", 1);

    [Fact]
    public void RecordReport_BeforeSucceeded_SetsHeadlineColumns()
    {
        // Arrange
        var run = NewRun(SampleRunId);

        // Act
        run.RecordReport(SampleReport());

        // Assert
        new { run.Score, run.WeightAssessablePct, run.EndpointsTotal, run.EndpointsCovered, run.EndpointsWeak, run.EndpointsNone, run.EndpointsUnverified }
            .Should().BeEquivalentTo(new { Score = 5.1m, WeightAssessablePct = 90m, EndpointsTotal = 5, EndpointsCovered = 3, EndpointsWeak = 1, EndpointsNone = 1, EndpointsUnverified = 0 });
    }

    [Fact]
    public void RecordReport_BeforeSucceeded_StoresCanonicalJson()
    {
        // Arrange
        var run = NewRun(SampleRunId);
        var report = SampleReport();

        // Act
        run.RecordReport(report);

        // Assert
        run.ReportJson.Should().Be(ReportJsonSerializer.Serialize(report));
    }

    [Fact]
    public void RecordReport_WhenAlreadySucceeded_Throws()
    {
        // Arrange
        var run = NewRun(SampleRunId);
        run.RecordReport(SampleReport());
        run.Succeed();

        // Act
        var act = () => run.RecordReport(SampleReport());

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordReport_ReportOfAnotherRun_Throws()
    {
        // Arrange
        var run = NewRun(Guid.NewGuid());

        // Act
        var act = () => run.RecordReport(SampleReport());

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordReport_ReportAtAnotherCommit_Throws()
    {
        // Arrange
        var run = NewRun(SampleRunId);
        var report = SampleReport();
        var atOtherCommit = report with { Meta = report.Meta with { CommitSha = "0f1e2d3c4b5a69788796a5b4c3d2e1f001234567" } };

        // Act
        var act = () => run.RecordReport(atOtherCommit);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordReport_GeneratedAtWithOffset_StoresSameInstant()
    {
        // Arrange
        var run = NewRun(SampleRunId);
        var report = SampleReport();
        var withOffset = report with { Meta = report.Meta with { GeneratedAt = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(2)) } };

        // Act
        run.RecordReport(withOffset);

        // Assert
        run.GeneratedAt.Should().Be(new DateTimeOffset(2026, 11, 15, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Succeed_WithoutReport_Throws()
    {
        // Arrange
        var run = NewRun(SampleRunId);

        // Act
        var act = run.Succeed;

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }
}
