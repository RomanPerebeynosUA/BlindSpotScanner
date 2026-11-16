using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Reports;

public class ReportValidatorTests
{
    private readonly ReportValidator _validator = new(new StubSchemaValidator([]));

    [Fact]
    public void Validate_CurrentExampleWithPrevious_ReturnsNoViolations()
    {
        // Arrange
        var report = Fixtures.Current;

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Validate_PreviousExampleAsFirstScan_ReturnsNoViolations()
    {
        // Arrange
        var report = Fixtures.Previous;

        // Act
        var violations = _validator.Validate(report, previous: null);

        // Assert
        violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("endpoints/total")]
    [InlineData("endpoints/covered")]
    [InlineData("endpoints/weak")]
    [InlineData("endpoints/skipped")]
    [InlineData("endpoints/none")]
    [InlineData("endpoints/unverified")]
    [InlineData("controllers/covered")]
    [InlineData("controllers/weak")]
    [InlineData("controllers/none")]
    [InlineData("qualityScore")]
    [InlineData("weightAssessablePct")]
    [InlineData("falsePositiveCount")]
    [InlineData("skippedTestCount")]
    [InlineData("uncoveredRealSeams")]
    public void Validate_TamperedSummaryNumber_ReportsThatField(string field)
    {
        // Arrange
        var report = Tampering.AddOne(Fixtures.Current, field);

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().ContainMatch($"summary/{field}: *");
    }

    [Fact]
    public void Validate_CompensatingTamper_IsCaught()
    {
        // Arrange
        var original = Fixtures.Current;
        var counts = original.Summary.Endpoints;
        var report = original with { Summary = original.Summary with { Endpoints = counts with { Covered = counts.Covered + 1, None = counts.None - 1 } } };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain(["summary/endpoints/covered: expected 3, found 4", "summary/endpoints/none: expected 1, found 0"]);
    }

    [Fact]
    public void Validate_TamperedBullet_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var bullets = original.Summary.Bullets.ToList();
        bullets[0] = bullets[0] with { Text = "Integration testing is strong." };
        var report = original with { Summary = original.Summary with { Bullets = bullets } };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().ContainMatch("summary/bullets/0/text: *");
    }

    [Fact]
    public void Validate_TamperedDeltaMetric_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var metrics = original.Delta!.Metrics.ToList();
        metrics[0] = metrics[0] with { Curr = 9.9m };
        var report = original with { Delta = original.Delta with { Metrics = metrics } };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().ContainMatch("delta/metrics/0/curr: *");
    }

    [Fact]
    public void Validate_ControllerStatusTampered_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var coverage = original.EndpointCoverage.ToList();
        coverage[0] = coverage[0] with { Status = CoverageStatus.Covered };
        var report = original with { EndpointCoverage = coverage };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("endpointCoverage/0/status: expected \"weak\", found \"covered\"");
    }

    [Fact]
    public void Validate_WeightsNotSummingToOne_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var scorecard = original.Scorecard.ToList();
        scorecard[0] = scorecard[0] with { Weight = 0.25m };
        var report = original with { Scorecard = scorecard };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("scorecard: weights sum to 1.05, not 1");
    }

    [Fact]
    public void Validate_CoveredEndpointNotVerified_IsReported()
    {
        // Arrange
        var report = Fixtures.Current.WithEndpoint("GET", "api/books", e => e with { Verified = false });

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("endpointCoverage/0/endpoints/0: covered endpoint is not verified");
    }

    [Fact]
    public void Validate_MalformedCitation_IsReported()
    {
        // Arrange
        var report = Fixtures.Current.WithEndpoint("GET", "api/books", e => e with { Evidence = ["BooksApiTests"] });

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("endpointCoverage/0/endpoints/0/evidence/0: malformed citation 'BooksApiTests'");
    }

    [Fact]
    public void Validate_RiskHeadlineWithFileName_IsReported()
    {
        // Arrange
        var report = WithFirstRiskHeadline(Fixtures.Current, "OrdersController.cs has no cancellation test");

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("topRisks/0/headline: contains a path, citation or file name");
    }

    [Fact]
    public void Validate_RiskHeadlineTooLong_IsReported()
    {
        // Arrange
        var report = WithFirstRiskHeadline(Fixtures.Current, string.Join(' ', Enumerable.Repeat("word", 26)));

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("topRisks/0/headline: more than 25 words");
    }

    [Fact]
    public void Validate_WhyItMattersWithSlash_IsAccepted()
    {
        // Arrange
        var original = Fixtures.Current;
        var risks = original.TopRisks.ToList();
        risks[0] = risks[0] with { WhyItMatters = "Checkout must work 24/7, and a broken payment call would reach customers." };
        var report = original with { TopRisks = risks };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ScorableDimensionWithoutScore_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var scorecard = original.Scorecard.ToList();
        scorecard[0] = scorecard[0] with { Score = null };
        var report = original with { Scorecard = scorecard };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("scorecard/0: scorable dimension has no score");
    }

    [Fact]
    public void Validate_DuplicateScorecardId_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var scorecard = original.Scorecard.ToList();
        scorecard[1] = scorecard[1] with { Id = 1 };
        var report = original with { Scorecard = scorecard };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("scorecard/1/id: duplicate dimension id 1");
    }

    [Fact]
    public void Validate_MethodNotScript_IsReported()
    {
        // Arrange
        var original = Fixtures.Current;
        var report = original with { Validation = original.Validation with { Method = "llm" } };

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("validation/method: must be \"script\", found \"llm\"");
    }

    [Fact]
    public void Validate_NulCharacterInNotes_IsReported()
    {
        // Arrange
        var report = Fixtures.Current.WithEndpoint("GET", "api/books", e => e with { Notes = "a\u0000b" });

        // Act
        var violations = _validator.Validate(report, Fixtures.Previous);

        // Assert
        violations.Should().Contain("endpointCoverage/0/endpoints/0/notes: contains a NUL character");
    }

    [Fact]
    public void Validate_SchemaViolations_ArePassedThrough()
    {
        // Arrange
        var validator = new ReportValidator(new StubSchemaValidator(["meta/runId: bad"]));

        // Act
        var violations = validator.Validate(Fixtures.Current, Fixtures.Previous);

        // Assert
        violations.Should().Equal("schema: meta/runId: bad");
    }

    private static IntegrationReport WithFirstRiskHeadline(IntegrationReport report, string headline)
    {
        var risks = report.TopRisks.ToList();
        risks[0] = risks[0] with { Headline = headline };
        return report with { TopRisks = risks };
    }

    private sealed class StubSchemaValidator(IReadOnlyList<string> violations) : IReportSchemaValidator
    {
        public IReadOnlyList<string> Validate(string reportJson) => violations;
    }
}

/// <summary>Adds one to a named summary number (the switch lives here, not in the test).</summary>
internal static class Tampering
{
    public static IntegrationReport AddOne(IntegrationReport report, string field)
    {
        var s = report.Summary;
        var summary = field switch
        {
            "endpoints/total" => s with { Endpoints = s.Endpoints with { Total = s.Endpoints.Total + 1 } },
            "endpoints/covered" => s with { Endpoints = s.Endpoints with { Covered = s.Endpoints.Covered + 1 } },
            "endpoints/weak" => s with { Endpoints = s.Endpoints with { Weak = s.Endpoints.Weak + 1 } },
            "endpoints/skipped" => s with { Endpoints = s.Endpoints with { Skipped = s.Endpoints.Skipped + 1 } },
            "endpoints/none" => s with { Endpoints = s.Endpoints with { None = s.Endpoints.None + 1 } },
            "endpoints/unverified" => s with { Endpoints = s.Endpoints with { Unverified = s.Endpoints.Unverified + 1 } },
            "controllers/covered" => s with { Controllers = s.Controllers with { Covered = s.Controllers.Covered + 1 } },
            "controllers/weak" => s with { Controllers = s.Controllers with { Weak = s.Controllers.Weak + 1 } },
            "controllers/none" => s with { Controllers = s.Controllers with { None = s.Controllers.None + 1 } },
            "qualityScore" => s with { QualityScore = s.QualityScore + 1 },
            "weightAssessablePct" => s with { WeightAssessablePct = s.WeightAssessablePct + 1 },
            "falsePositiveCount" => s with { FalsePositiveCount = s.FalsePositiveCount + 1 },
            "skippedTestCount" => s with { SkippedTestCount = s.SkippedTestCount + 1 },
            "uncoveredRealSeams" => s with { UncoveredRealSeams = s.UncoveredRealSeams + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown summary field."),
        };
        return report with { Summary = summary };
    }
}
