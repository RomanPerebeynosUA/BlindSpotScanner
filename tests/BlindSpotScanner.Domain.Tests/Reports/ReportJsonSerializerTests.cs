using System.Text.Json;
using System.Text.Json.Nodes;
using BlindSpotScanner.Domain.Reports;
using FluentAssertions;

namespace BlindSpotScanner.Domain.Tests.Reports;

public class ReportJsonSerializerTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string CanonicalSample() =>
        ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(Fixture("sample-report.json")));

    [Theory]
    [InlineData("sample-report.json")]
    [InlineData("sample-report-previous.json")]
    public void Serialize_RoundTripOfExample_IsSemanticallyEqualToFile(string fixture)
    {
        // Arrange
        var file = Fixture(fixture);

        // Act
        var canonical = ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(file));

        // Assert
        JsonNode.DeepEquals(JsonNode.Parse(file), JsonNode.Parse(canonical)).Should().BeTrue();
    }

    [Fact]
    public void Serialize_UnscorableDimension_WritesExplicitNullScore()
    {
        // Arrange
        // Act
        var canonical = CanonicalSample();

        // Assert
        canonical.Should().Contain("\"score\":null");
    }

    [Fact]
    public void Serialize_NonAsciiText_IsWrittenLiterally()
    {
        // Arrange
        // Act
        var canonical = CanonicalSample();

        // Assert
        canonical.Should().Contain("→");
    }

    [Fact]
    public void Serialize_ClassificationKeysInAnotherOrder_ProducesSameBytes()
    {
        // Arrange
        var node = JsonNode.Parse(Fixture("sample-report.json"))!;
        node["inventory"]!["classification"] = new JsonObject { ["unit"] = 6, ["other"] = 1, ["integration"] = 3 };

        // Act
        var reordered = ReportJsonSerializer.Serialize(ReportJsonSerializer.Deserialize(node.ToJsonString()));

        // Assert
        reordered.Should().Be(CanonicalSample());
    }

    [Fact]
    public void Deserialize_UnknownProperty_Throws()
    {
        // Arrange
        var node = JsonNode.Parse(Fixture("sample-report.json"))!;
        node["meta"]!["extra"] = 1;

        // Act
        var act = () => ReportJsonSerializer.Deserialize(node.ToJsonString());

        // Assert
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_MissingRequiredProperty_Throws()
    {
        // Arrange
        var node = JsonNode.Parse(Fixture("sample-report.json"))!;
        node["meta"]!.AsObject().Remove("commitSha");

        // Act
        var act = () => ReportJsonSerializer.Deserialize(node.ToJsonString());

        // Assert
        act.Should().Throw<JsonException>();
    }
}
