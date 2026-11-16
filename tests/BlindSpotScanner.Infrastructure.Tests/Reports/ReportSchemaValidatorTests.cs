using System.Text.Json.Nodes;
using BlindSpotScanner.Infrastructure.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Reports;

public class ReportSchemaValidatorTests
{
    private readonly ReportSchemaValidator _validator = new();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static JsonNode Sample() => JsonNode.Parse(Fixture("sample-report.json"))!;

    [Theory]
    [InlineData("sample-report.json")]
    [InlineData("sample-report-previous.json")]
    public void Validate_ExampleReports_ReturnsNoErrors(string fixture)
    {
        // Arrange
        var json = Fixture(fixture);

        // Act
        var errors = _validator.Validate(json);

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WrongSchemaVersion_ReturnsError()
    {
        // Arrange
        var node = Sample();
        node["meta"]!["schemaVersion"] = "2.0";

        // Act
        var errors = _validator.Validate(node.ToJsonString());

        // Assert
        errors.Should().ContainMatch("*/meta/schemaVersion*");
    }

    [Fact]
    public void Validate_NonHexCommitSha_ReturnsError()
    {
        // Arrange
        var node = Sample();
        node["meta"]!["commitSha"] = "xyz";

        // Act
        var errors = _validator.Validate(node.ToJsonString());

        // Assert
        errors.Should().ContainMatch("*/meta/commitSha*");
    }

    [Fact]
    public void Validate_RunIdNotUuid_ReturnsError()
    {
        // Arrange
        var node = Sample();
        node["meta"]!["runId"] = "not-a-uuid";

        // Act
        var errors = _validator.Validate(node.ToJsonString());

        // Assert
        errors.Should().ContainMatch("*/meta/runId*");
    }

    [Fact]
    public void Validate_MissingSummary_ReturnsError()
    {
        // Arrange
        var node = Sample();
        node.AsObject().Remove("summary");

        // Act
        var errors = _validator.Validate(node.ToJsonString());

        // Assert
        errors.Should().ContainMatch("*summary*");
    }

    [Fact]
    public void Validate_ValidationMethodNotScript_ReturnsError()
    {
        // Arrange
        var node = Sample();
        node["validation"]!["method"] = "llm";

        // Act
        var errors = _validator.Validate(node.ToJsonString());

        // Assert
        errors.Should().ContainMatch("*/validation/method*");
    }

    [Fact]
    public void EmbeddedSchema_MatchesBlueprintContract()
    {
        // Arrange
        var blueprint = Fixture("integration-report.schema.json").ReplaceLineEndings("\n");

        // Act
        var embedded = ReportSchemaValidator.ReadEmbeddedSchema().ReplaceLineEndings("\n");

        // Assert
        embedded.Should().Be(blueprint);
    }
}
