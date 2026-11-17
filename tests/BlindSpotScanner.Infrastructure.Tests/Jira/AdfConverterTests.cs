using BlindSpotScanner.Infrastructure.Jira;
using FluentAssertions;

namespace BlindSpotScanner.Infrastructure.Tests.Jira;

public class AdfConverterTests
{
    private static string Doc(string content) => $$"""{"type":"doc","version":1,"content":[{{content}}]}""";

    private static string Text(string text) => $$"""{"type":"text","text":"{{text}}"}""";

    private static string Paragraph(params string[] inline) => $$"""{"type":"paragraph","content":[{{string.Join(",", inline)}}]}""";

    [Fact]
    public void ToAdf_Heading_ReturnsHeadingNode()
    {
        // Act
        var adf = AdfConverter.ToAdf("## Problem");

        // Assert
        adf.ToJsonString().Should().Be(Doc($$"""{"type":"heading","attrs":{"level":2},"content":[{{Text("Problem")}}]}"""));
    }

    [Fact]
    public void ToAdf_BulletRun_ReturnsOneList()
    {
        // Act
        var adf = AdfConverter.ToAdf("- one\n- two");

        // Assert
        adf.ToJsonString().Should().Be(Doc(
            $$"""{"type":"bulletList","content":[{"type":"listItem","content":[{{Paragraph(Text("one"))}}]},{"type":"listItem","content":[{{Paragraph(Text("two"))}}]}]}"""));
    }

    [Fact]
    public void ToAdf_TwoParagraphs_ReturnsTwoNodes()
    {
        // Act
        var adf = AdfConverter.ToAdf("first\n\nsecond");

        // Assert
        adf.ToJsonString().Should().Be(Doc($"{Paragraph(Text("first"))},{Paragraph(Text("second"))}"));
    }

    [Fact]
    public void ToAdf_LineBreakInParagraph_UsesHardBreak()
    {
        // Act
        var adf = AdfConverter.ToAdf("first\nsecond");

        // Assert
        adf.ToJsonString().Should().Be(Doc(Paragraph(Text("first"), """{"type":"hardBreak"}""", Text("second"))));
    }

    [Fact]
    public void ToAdf_Link_ReturnsLinkMark()
    {
        // Act
        var adf = AdfConverter.ToAdf("See [the report](https://example.test/runs/1) now");

        // Assert
        adf.ToJsonString().Should().Be(Doc(Paragraph(
            Text("See "),
            """{"type":"text","text":"the report","marks":[{"type":"link","attrs":{"href":"https://example.test/runs/1"}}]}""",
            Text(" now"))));
    }

    [Fact]
    public void ToAdf_InlineCode_ReturnsCodeMark()
    {
        // Act
        var adf = AdfConverter.ToAdf("Run `dotnet test`");

        // Assert
        adf.ToJsonString().Should().Be(Doc(Paragraph(Text("Run "), """{"type":"text","text":"dotnet test","marks":[{"type":"code"}]}""")));
    }

    [Fact]
    public void ToAdf_CrLf_SameAsLf()
    {
        // Act
        var adf = AdfConverter.ToAdf("## Problem\r\n\r\nfirst\r\n- one");

        // Assert
        adf.ToJsonString().Should().Be(AdfConverter.ToAdf("## Problem\n\nfirst\n- one").ToJsonString());
    }

    [Fact]
    public void ToAdf_Empty_ReturnsEmptyDoc()
    {
        // Act
        var adf = AdfConverter.ToAdf("");

        // Assert
        adf.ToJsonString().Should().Be("""{"type":"doc","version":1,"content":[]}""");
    }
}
