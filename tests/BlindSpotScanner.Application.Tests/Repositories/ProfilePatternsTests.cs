using BlindSpotScanner.Application.Repositories;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Repositories;

public class ProfilePatternsTests
{
    private static readonly string[] TodoTree =
    [
        "Todo.Api/Todos/TodoApi.cs",
        "Todo.Api/Users/UsersApi.cs",
        "Todo.Api.Tests/TodoApiTests.cs",
    ];

    private static ProfileRules Rules(
        string[]? testGlobs = null, string[]? minimalApiGlobs = null, string[]? attributes = null, string[]? notIntegrationGlobs = null) =>
        new("real-seam", testGlobs ?? [], [], minimalApiGlobs ?? [], [], attributes ?? [], [], notIntegrationGlobs ?? []);

    [Fact]
    public void SyntaxErrors_PatternWithSpace_NamesFieldAndPattern()
    {
        // Act
        var errors = ProfilePatterns.SyntaxErrors(Rules(minimalApiGlobs: ["**/*Api.cs (matches TodoApi.cs)"]));

        // Assert
        errors.Should().Equal("minimalApiGlobs: '**/*Api.cs (matches TodoApi.cs)' is not one pattern; put one pattern per line with no spaces or commas");
    }

    [Fact]
    public void SyntaxErrors_TwoPatternsJoinedByComma_IsRejected()
    {
        // Act
        var errors = ProfilePatterns.SyntaxErrors(Rules(notIntegrationGlobs: ["**/A.cs,**/B.cs"]));

        // Assert
        errors.Should().Equal("notIntegrationGlobs: '**/A.cs,**/B.cs' is not one pattern; put one pattern per line with no spaces or commas");
    }

    [Fact]
    public void SyntaxErrors_AttributeWithSpaces_IsNotChecked()
    {
        // Act
        var errors = ProfilePatterns.SyntaxErrors(Rules(attributes: ["Trait(\"Category\", \"Integration\")"]));

        // Assert
        errors.Should().BeEmpty();
    }

    [Fact]
    public void Count_MinimalApiPattern_CountsMatchingTreeFiles()
    {
        // Act
        var matches = ProfilePatterns.Count(TodoTree, Rules(minimalApiGlobs: ["**/*Api.cs"]));

        // Assert
        matches.Should().Equal(new PatternMatch("minimalApiGlobs", "**/*Api.cs", 2));
    }

    [Fact]
    public void Count_ExcludedPaths_AreNotCounted()
    {
        // Act
        var matches = ProfilePatterns.Count([.. TodoTree, "Todo.Api/obj/Gen/FooApi.cs"], Rules(minimalApiGlobs: ["**/*Api.cs"]));

        // Assert
        matches.Single().Files.Should().Be(2);
    }

    [Fact]
    public void Count_BackslashPattern_CountsAsTheScanWould()
    {
        // Act (the matcher normalizes "./" and "\\", in scans as here)
        var matches = ProfilePatterns.Count(TodoTree, Rules(testGlobs: [".\\Todo.Api.Tests\\**"]));

        // Assert
        matches.Single().Files.Should().Be(1);
    }

    [Fact]
    public void Count_SeveralFields_ListsInFieldOrder()
    {
        // Act
        var matches = ProfilePatterns.Count(TodoTree, Rules(testGlobs: ["Todo.Api.Tests/**/*.cs"], minimalApiGlobs: ["**/*Api.cs"], notIntegrationGlobs: ["**/Nope.cs"]));

        // Assert
        matches.Select(m => m.Field).Should().Equal("testGlobs", "minimalApiGlobs", "notIntegrationGlobs");
    }
}
