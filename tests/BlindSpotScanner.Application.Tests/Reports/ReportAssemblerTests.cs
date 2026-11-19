using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Reports;

public class ReportAssemblerTests
{
    private static ReportDetail CurrentDetail => ReportDetail.From(Fixtures.Current);

    [Fact]
    public void Assemble_CurrentExampleDetail_ReproducesWholeReport()
    {
        // Arrange
        var expected = Fixtures.Current;

        // Act
        var assembled = ReportAssembler.Assemble(ReportDetail.From(expected), Fixtures.Previous);

        // Assert
        ReportJsonSerializer.Serialize(assembled with { Validation = expected.Validation })
            .Should().Be(ReportJsonSerializer.Serialize(expected));
    }

    [Fact]
    public void Assemble_PreviousExampleDetail_ReproducesWholeReport()
    {
        // Arrange
        var expected = Fixtures.Previous;

        // Act
        var assembled = ReportAssembler.Assemble(ReportDetail.From(expected), previous: null);

        // Assert
        ReportJsonSerializer.Serialize(assembled with { Validation = expected.Validation })
            .Should().Be(ReportJsonSerializer.Serialize(expected));
    }

    [Fact]
    public void Assemble_ControllerStatusesAuthoredWrongly_AreRecomputed()
    {
        // Arrange
        var detail = CurrentDetail;
        detail = detail with { EndpointCoverage = detail.EndpointCoverage.Select(c => c with { Status = CoverageStatus.Covered }).ToList() };

        // Act
        var assembled = ReportAssembler.Assemble(detail, Fixtures.Previous);

        // Assert
        assembled.EndpointCoverage.Select(c => c.Status).Should().Equal(CoverageStatus.Weak, CoverageStatus.Weak);
    }

    [Fact]
    public void Assemble_ControllerWithUnverifiedAndNone_IsUnverified()
    {
        // Arrange
        var detail = CurrentDetail.WithStatusCounts(covered: 0, weak: 0, none: 1, unverified: 1);

        // Act
        var assembled = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        assembled.EndpointCoverage.Single().Status.Should().Be(CoverageStatus.Unverified);
    }

    [Fact]
    public void Assemble_NoScorableDimension_ScoreIsNullAndLabelNotScorable()
    {
        // Arrange
        var detail = CurrentDetail;
        detail = detail with { Scorecard = detail.Scorecard.Select(d => d with { Scorable = false, Score = null }).ToList() };

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        new { summary.QualityScore, summary.WeightAssessablePct, summary.ScoreLabel, TestQuality = summary.Bullets[2].Text }
            .Should().BeEquivalentTo(new { QualityScore = (decimal?)null, WeightAssessablePct = 0m, ScoreLabel = "Not scorable", TestQuality = "Test quality could not be scored." });
    }

    [Fact]
    public void Assemble_ZeroEndpoints_VerdictIsWeakAndNothingThrows()
    {
        // Arrange
        var detail = CurrentDetail.WithStatusCounts(covered: 0, weak: 0, none: 0, unverified: 0);

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        new { summary.Verdict, summary.Endpoints.Total }.Should().BeEquivalentTo(new { Verdict = Verdict.Weak, Total = 0 });
    }

    [Fact]
    public void Assemble_AllEndpointsUnverified_OverallSaysNotAssessed()
    {
        // Arrange
        var detail = CurrentDetail.WithStatusCounts(covered: 0, weak: 0, none: 0, unverified: 3);

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.Bullets[0].Text.Should().Be("Integration testing could not be assessed: none of the 3 API operations could be checked.");
    }

    [Theory]
    [InlineData(80, Verdict.Strong)]
    [InlineData(79, Verdict.Moderate)]
    [InlineData(60, Verdict.Moderate)]
    [InlineData(35, Verdict.Partial)]
    [InlineData(34, Verdict.Weak)]
    public void Assemble_VerdictThresholds_MatchLadder(int coveredOfHundred, Verdict expected)
    {
        // Arrange
        var detail = CurrentDetail.WithStatusCounts(covered: coveredOfHundred, weak: 0, none: 100 - coveredOfHundred, unverified: 0);

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.Verdict.Should().Be(expected);
    }

    [Fact]
    public void Assemble_ManyUnverified_AppendsLowConfidence()
    {
        // Arrange
        var detail = CurrentDetail.WithStatusCounts(covered: 2, weak: 0, none: 0, unverified: 2);

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.Bullets[0].Text.Should().Be(
            "Integration testing is strong: 2 of 4 API operations are properly tested; 0 more lightly tested. (low confidence: many endpoints unverified)");
    }

    [Fact]
    public void Assemble_NonRealSeamMapping_LabelSaysNotComparable()
    {
        // Arrange
        var detail = CurrentDetail;
        detail = detail with { Meta = detail.Meta with { IntegrationMappedTo = IntegrationMappedTo.E2eMockedData } };

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.ScoreLabel.Should().Be(
            "5.1 / 10 across scored dimensions (90% of weight assessable) — not comparable to real-seam scores (e2e-mocked-data)");
    }

    [Fact]
    public void Assemble_RisksOutOfOrder_AreRankedByPosition()
    {
        // Arrange
        var detail = CurrentDetail;
        detail = detail with { TopRisks = detail.TopRisks.Reverse().ToList() };

        // Act
        var assembled = ReportAssembler.Assemble(detail, Fixtures.Previous);

        // Assert
        assembled.TopRisks.Select(r => (r.Rank, r.Headline)).Should().Equal(
            (1, "Order cancellation is not tested at all"),
            (2, "Some tests pass even when the wrong data comes back"),
            (3, "Nothing tests a real charge with the payment provider"));
    }

    [Fact]
    public void Assemble_SeamsNotDetected_UncoveredRealSeamsNull()
    {
        // Arrange
        var detail = CurrentDetail;
        detail = detail with { Inventory = detail.Inventory with { ProductionSeams = null } };

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.UncoveredRealSeams.Should().BeNull();
    }

    [Fact]
    public void Assemble_SeamsDetected_CountsUncoveredSeamFindings()
    {
        // Arrange
        var detail = CurrentDetail;

        // Act
        var summary = ReportAssembler.Assemble(detail, previous: null).Summary;

        // Assert
        summary.UncoveredRealSeams.Should().Be(1);
    }
}
