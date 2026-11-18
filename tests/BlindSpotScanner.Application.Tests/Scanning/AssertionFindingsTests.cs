using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Scanning;

public class AssertionFindingsTests
{
    private const string Test = "tests/Api.IntegrationTests/BooksTests.cs::BooksTests.Create_works";

    private static TestAssertion Row(int? level, bool errorPath = false, string[]? sent = null, string[]? missing = null, bool resolved = true) => new()
    {
        Test = Test,
        Level = level,
        LevelSource = level is null ? LevelSource.Unknown : LevelSource.Code,
        ErrorPath = errorPath,
        SentFields = sent ?? [],
        CheckedFields = [],
        UncheckedFields = missing ?? [],
        FieldsResolved = resolved,
    };

    [Fact]
    public void From_LevelZero_ReturnsNoAssertion()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(0)]);

        // Assert
        findings.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = FindingKind.NoAssertion,
            Severity = Severity.High,
            StableKey = $"no-assertion:{Test}",
            Headline = "Create_works checks nothing",
            Detail = new[] { $"{Test} passes whatever the code does." },
            Evidence = new[] { Test },
        });
    }

    [Fact]
    public void From_UncheckedFields_ReturnsUncheckedFieldsFinding()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(3, sent: ["title", "price"], missing: ["price"])]);

        // Assert
        findings.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = FindingKind.UncheckedFields,
            Severity = Severity.Medium,
            StableKey = $"unchecked-fields:{Test}",
            Headline = "Create_works sends fields it never checks",
            Detail = new[] { "Sends: title, price", "Not checked: price" },
            Evidence = new[] { Test },
        });
    }

    [Fact]
    public void From_ErrorPathLevelOne_ReturnsErrorWithoutStateCheck()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(1, errorPath: true)]);

        // Assert
        findings.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Kind = FindingKind.ErrorWithoutStateCheck,
            Severity = Severity.Medium,
            StableKey = $"error-without-state-check:{Test}",
            Headline = "Create_works checks only the error status",
            Detail = new[] { "Checks the error status but not the error body or that nothing was saved." },
            Evidence = new[] { Test },
        });
    }

    [Fact]
    public void From_ErrorPathWithUncheckedFields_ReturnsNoUncheckedFieldsFinding()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(3, errorPath: true, sent: ["title"], missing: ["title"])]);

        // Assert
        findings.Should().BeEmpty();
    }

    [Fact]
    public void From_UnresolvedFields_ReturnsNoUncheckedFieldsFinding()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(3, resolved: false)]);

        // Assert
        findings.Should().BeEmpty();
    }

    [Fact]
    public void From_ErrorPathLevelThree_ReturnsNothing()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(3, errorPath: true)]);

        // Assert
        findings.Should().BeEmpty();
    }

    [Fact]
    public void From_UnknownLevel_ReturnsNothing()
    {
        // Arrange
        // Act
        var findings = AssertionFindings.From([Row(null)]);

        // Assert
        findings.Should().BeEmpty();
    }
}
