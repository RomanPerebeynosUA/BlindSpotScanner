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
        // Without assertion rows dimension 5 is taken as authored, like every other dimension.
        detail = detail with { Scorecard = detail.Scorecard.Select(d => d with { Scorable = false, Score = null }).ToList(), TestAssertions = null };

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
            "4.9 / 10 across scored dimensions (90% of weight assessable) — not comparable to real-seam scores (e2e-mocked-data)");
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

    /// <summary>One row per level, named tests/A.cs::A.T0, T1, …</summary>
    private static ReportDetail WithLevels(params int?[] levels) => CurrentDetail with
    {
        TestAssertions = levels.Select((level, i) => new TestAssertion
        {
            Test = $"tests/A.cs::A.T{i}",
            Level = level,
            LevelSource = level is null ? LevelSource.Unknown : LevelSource.Code,
            ErrorPath = false,
            SentFields = [],
            CheckedFields = [],
            UncheckedFields = [],
            FieldsResolved = true,
        }).ToList(),
    };

    private static ScoreDimension DimensionFive(IntegrationReport report) => report.Scorecard.Single(d => d.Id == 5);

    [Fact]
    public void Assemble_TestAssertions_ComputesDimensionFive()
    {
        // Arrange
        var detail = WithLevels(1, 2, 4, 1, 3);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Should().BeEquivalentTo(new { Score = (int?)4, Scorable = true, Confidence = (Confidence?)Confidence.High, Weight = 0.15m });
    }

    [Fact]
    public void Assemble_TestAssertions_WritesComputedBullets()
    {
        // Arrange
        var detail = WithLevels(1, 2, 4, 1, 3);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).JustificationBullets.Should().Equal(
            "What is tested: 2 of 5 integration tests check response values or saved state (L3 or higher).",
            "What is missing: 3 test(s) check only the status code or shape (L0–L2); 0 could not be classified.",
            "Evidence: tests/A.cs::A.T0, tests/A.cs::A.T3, tests/A.cs::A.T1",
            "Why this score: the mean of the per-test points (L0 1, L1 2, L2 4, L3 6, L4 8, L5 10) over 5 test(s) is 4.4.");
    }

    [Fact]
    public void Assemble_HalfPointMean_RoundsAwayFromZero()
    {
        // Arrange
        var detail = WithLevels(0, 1);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Score.Should().Be(2);
    }

    [Fact]
    public void Assemble_TestAssertions_CountsAssertionLevels()
    {
        // Arrange
        var detail = WithLevels(1, 2, null, 1);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        report.Summary.AssertionLevels.Should().BeEquivalentTo(new AssertionLevels { L0 = 0, L1 = 2, L2 = 1, L3 = 0, L4 = 0, L5 = 0, Unknown = 1 });
    }

    [Fact]
    public void Assemble_SomeUnknown_ConfidenceMed()
    {
        // Arrange
        var detail = WithLevels(3, null);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Confidence.Should().Be(Confidence.Med);
    }

    [Fact]
    public void Assemble_AllUnknown_DimensionFiveUnscorable()
    {
        // Arrange
        var detail = WithLevels(null, null);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Should().BeEquivalentTo(new
        {
            Score = (int?)null,
            Scorable = false,
            Confidence = (Confidence?)Confidence.Low,
            Evidence = Array.Empty<string>(),
            JustificationBullets = new[] { "What is tested: N/A — insufficient input", "What is missing: N/A — insufficient input", "Evidence: none", "Why this score: N/A — insufficient input" },
        });
    }

    [Fact]
    public void Assemble_EmptyTestAssertions_DimensionFiveUnscorable()
    {
        // Arrange
        var detail = WithLevels();

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Scorable.Should().BeFalse();
    }

    [Fact]
    public void Assemble_NoTestAssertions_KeepsDimensionFive()
    {
        // Arrange
        var detail = CurrentDetail with { TestAssertions = null };

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Should().BeEquivalentTo(detail.Scorecard.Single(d => d.Id == 5));
    }

    [Fact]
    public void Assemble_EvidenceIsThreeWeakestByPointsThenCitation()
    {
        // Arrange
        var detail = WithLevels(3, 1, 2, 1);

        // Act
        var report = ReportAssembler.Assemble(detail, previous: null);

        // Assert
        DimensionFive(report).Evidence.Should().Equal("tests/A.cs::A.T1", "tests/A.cs::A.T3", "tests/A.cs::A.T2");
    }
}
