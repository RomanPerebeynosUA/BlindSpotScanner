using System.Text.RegularExpressions;

namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>
/// The prompts from the blueprint's <c>prompts/</c> folder, embedded at build time. The grading
/// prompt gets the scoring rubric (doc 06) appended, as it says the rubric is "embedded at runtime".
/// </summary>
public sealed partial class PromptLibrary
{
    private const string SystemName = "_system";

    private readonly Dictionary<string, string> _texts;

    public PromptLibrary()
    {
        var assembly = typeof(PromptLibrary).Assembly;
        _texts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("prompts.", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .ToDictionary(n => n["prompts.".Length..^".md".Length], n => Read(assembly, n), StringComparer.Ordinal);
        _texts[Application.Llm.Prompts.GradeQuality] += "\n\n" + Read(assembly, "rubric.md");
    }

    public string System => _texts[SystemName];

    public string Task(string name) =>
        name != SystemName && _texts.TryGetValue(name, out var text) ? text : throw new KeyNotFoundException($"Unknown prompt '{name}'.");

    /// <summary>The version from the prompt's first line, e.g. "# Task: … — v1.0.0".</summary>
    public string Version(string name) => VersionPattern().Match(Task(name).Split('\n')[0]).Groups[1].Value;

    private static string Read(System.Reflection.Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Missing embedded resource {resource}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"v(\d+\.\d+\.\d+)\s*$")]
    private static partial Regex VersionPattern();
}
