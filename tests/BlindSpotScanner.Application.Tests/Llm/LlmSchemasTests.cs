using System.Text.RegularExpressions;
using BlindSpotScanner.Application.Llm;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Llm;

public class LlmSchemasTests
{
    [Theory]
    [InlineData(Prompts.ClassifyTests)]
    [InlineData(Prompts.MapCoverage)]
    [InlineData(Prompts.GradeQuality)]
    [InlineData(Prompts.DraftProfile)]
    public void Get_AnswerSchema_UsesNoTypeArrays(string prompt)
    {
        // Act (structured outputs documents anyOf for nullable values, not "type": [ ... ])
        var schema = LlmSchemas.Get(prompt);

        // Assert
        Regex.IsMatch(schema, @"""type""\s*:\s*\[").Should().BeFalse();
    }


    [Fact]
    public void Get_DraftProfile_RequiresRules()
    {
        // Act
        using var schema = System.Text.Json.JsonDocument.Parse(LlmSchemas.Get(Prompts.DraftProfile));

        // Assert
        schema.RootElement.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Contain(["integrationMappedTo", "minimalApiGlobs"]);
    }
}
