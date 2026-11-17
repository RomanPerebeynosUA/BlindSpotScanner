using System.Text;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain.Entities;
using Microsoft.Extensions.FileSystemGlobbing;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P4 (doc 05), deterministic part: the profile's rules decide what they can (D18), first match
/// wins. Files no rule decides are Ambiguous and wait for the LLM step (M5); they are never guessed.
/// </summary>
public sealed class ClassifyPhase(IBlobReader blobs, ITestSyntaxReader reader)
{
    public async Task<TestClassification> ClassifyAsync(RunContext run, RepositoryInventory inventory, RepoProfile profile, CancellationToken ct)
    {
        var files = new List<ClassifiedTestFile>();
        foreach (var file in inventory.TestFiles)
        {
            var facts = reader.Read(await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct));
            var (kind, reason) = Decide(file.Path, facts, profile);
            files.Add(new ClassifiedTestFile(file.Path, file.BlobSha, kind, reason, facts.TestMethods));
        }

        return new TestClassification(files);
    }

    private static (TestFileKind Kind, string Reason) Decide(string path, TestFileFacts facts, RepoProfile profile)
    {
        if (FirstGlob(path, profile.NotIntegrationGlobs) is { } notIntegration)
        {
            return (TestFileKind.Other, $"matches not-integration glob '{notIntegration}'");
        }

        if (facts.TestMethods.Count == 0)
        {
            return (TestFileKind.Other, "no test methods");
        }

        if (FirstGlob(path, profile.IntegrationGlobs) is { } integration)
        {
            return (TestFileKind.Integration, $"matches integration glob '{integration}'");
        }

        var attribute = profile.IntegrationAttributes.Select(NormalizeAttribute).FirstOrDefault(entry => facts.Attributes.Any(a => AttributeMatches(entry, a)));
        if (attribute is not null)
        {
            return (TestFileKind.Integration, $"has attribute [{attribute}]");
        }

        var baseType = profile.IntegrationBaseTypes.Select(RemoveWhitespace).FirstOrDefault(entry => facts.BaseTypes.Any(b => BaseTypeMatches(entry, b)));
        if (baseType is not null)
        {
            return (TestFileKind.Integration, $"derives from {baseType}");
        }

        return (TestFileKind.Ambiguous, "no profile rule matched");
    }

    private static string? FirstGlob(string path, IEnumerable<string> globs) =>
        globs.FirstOrDefault(glob =>
        {
            // Git paths are case-sensitive.
            var matcher = new Matcher(StringComparison.Ordinal);
            matcher.AddInclude(glob);
            return matcher.Match(path).HasMatches;
        });

    /// <summary>An entry without arguments matches the attribute by name.</summary>
    private static bool AttributeMatches(string entry, string attribute) =>
        entry.Contains('(', StringComparison.Ordinal)
            ? string.Equals(entry, attribute, StringComparison.Ordinal)
            : string.Equals(entry, NameBefore(attribute, '('), StringComparison.Ordinal);

    /// <summary>An entry without type arguments matches any generic arity.</summary>
    private static bool BaseTypeMatches(string entry, string baseType) =>
        entry.Contains('<', StringComparison.Ordinal)
            ? string.Equals(entry, baseType, StringComparison.Ordinal)
            : string.Equals(entry, NameBefore(baseType, '<'), StringComparison.Ordinal);

    private static string NameBefore(string text, char separator) =>
        text.IndexOf(separator, StringComparison.Ordinal) is var at and >= 0 ? text[..at] : text;

    private static string NormalizeAttribute(string entry)
    {
        var text = RemoveWhitespace(entry);
        return text.StartsWith('[') && text.EndsWith(']') ? text[1..^1] : text;
    }

    /// <summary>Whitespace outside string literals, matching how the syntax reader rebuilds attributes from tokens.</summary>
    private static string RemoveWhitespace(string text)
    {
        var result = new StringBuilder(text.Length);
        var inString = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inString = !inString;
            }

            if (inString || !char.IsWhiteSpace(c))
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
