using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BlindSpotScanner.Infrastructure.Jira;

/// <summary>
/// The small Markdown subset ticket descriptions use, as Atlassian Document Format (doc 04: Markdown
/// → ADF on the server): <c>#</c>–<c>###</c> headings, <c>- </c> bullet lists, paragraphs (lines
/// joined by hard breaks), <c>[text](url)</c> links and <c>`code`</c>. Anything else is plain text.
/// </summary>
public static partial class AdfConverter
{
    public static JsonObject ToAdf(string markdown)
    {
        var content = new JsonArray();
        var paragraph = new List<string>();
        JsonArray? list = null;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                content.Add(Paragraph(paragraph));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (list is not null)
            {
                content.Add(new JsonObject { ["type"] = "bulletList", ["content"] = list });
                list = null;
            }
        }

        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
            }
            else if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                FlushParagraph();
                FlushList();
                content.Add(new JsonObject
                {
                    ["type"] = "heading",
                    ["attrs"] = new JsonObject { ["level"] = heading.Groups[1].Length },
                    ["content"] = Inline(heading.Groups[2].Value),
                });
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                FlushParagraph();
                list ??= [];
                list.Add(new JsonObject { ["type"] = "listItem", ["content"] = new JsonArray(Paragraph([line[2..]])) });
            }
            else
            {
                FlushList();
                paragraph.Add(line);
            }
        }

        FlushParagraph();
        FlushList();
        return new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = content };
    }

    private static JsonObject Paragraph(IReadOnlyList<string> lines)
    {
        var inline = new JsonArray();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                inline.Add(new JsonObject { ["type"] = "hardBreak" });
            }

            foreach (var node in Inline(lines[i]).ToList())
            {
                inline.Add(node!.DeepClone());
            }
        }

        return new JsonObject { ["type"] = "paragraph", ["content"] = inline };
    }

    private static JsonArray Inline(string text)
    {
        var nodes = new JsonArray();
        var at = 0;
        foreach (Match m in InlinePattern().Matches(text))
        {
            AddText(nodes, text[at..m.Index]);
            nodes.Add(m.Groups[1].Success
                ? Marked(m.Groups[1].Value, new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = m.Groups[2].Value } })
                : Marked(m.Groups[3].Value, new JsonObject { ["type"] = "code" }));
            at = m.Index + m.Length;
        }

        AddText(nodes, text[at..]);
        return nodes;
    }

    // Jira rejects empty text nodes.
    private static void AddText(JsonArray nodes, string text)
    {
        if (text.Length > 0)
        {
            nodes.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        }
    }

    private static JsonObject Marked(string text, JsonObject mark) =>
        new() { ["type"] = "text", ["text"] = text, ["marks"] = new JsonArray(mark) };

    [GeneratedRegex(@"^(#{1,3}) (.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)|`([^`]+)`")]
    private static partial Regex InlinePattern();
}
