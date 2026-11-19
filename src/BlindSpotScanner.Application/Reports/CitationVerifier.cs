using System.Text.RegularExpressions;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Scanning;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <param name="Failures">One line per citation that didn't resolve; any failure fails the run (doc 05).</param>
public sealed record CitationCheck(int Checked, int Resolved, IReadOnlyList<string> Failures);

/// <summary>
/// Principle 1: re-reads every cited file at the report's pinned commit and confirms the cited name
/// is declared there. Coverage evidence must name a test method; other citations (scorecard,
/// findings, risks, evidence bullets) may name any method or class, since fixtures are evidence too
/// (D21, D32).
/// </summary>
public sealed partial class CitationVerifier(IGitHubClient github, ITestSyntaxReader reader)
{
    public async Task<CitationCheck> VerifyAsync(IntegrationReport report, CancellationToken ct)
    {
        var mustBeTest = report.EndpointCoverage
            .SelectMany(c => c.Endpoints)
            .Where(e => e.Status is CoverageStatus.Covered or CoverageStatus.Weak or CoverageStatus.Skipped)
            .SelectMany(e => e.Evidence)
            .ToHashSet(StringComparer.Ordinal);
        var other = report.Scorecard.SelectMany(d => (d.Evidence ?? []).Concat(d.JustificationBullets.SelectMany(CitationsIn)))
            .Concat(report.Findings.SelectMany(f => f.Evidence))
            .Concat(report.TopRisks.SelectMany(r => r.Evidence ?? []));
        var citations = mustBeTest.Concat(other).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var files = new Dictionary<string, TestFileFacts?>(StringComparer.Ordinal);
        foreach (var path in citations.Select(c => Split(c).Path).Where(IsRepositoryRelative).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var text = await github.GetFileTextAsync(report.Meta.Repo.Owner, report.Meta.Repo.Name, path, report.Meta.CommitSha, ct);
            files[path] = text is null ? null : reader.Read(text);
        }

        var failures = new List<string>();
        foreach (var citation in citations)
        {
            var (path, name) = Split(citation);
            var test = mustBeTest.Contains(citation);
            if (!IsRepositoryRelative(path))
            {
                failures.Add($"{citation}: not a repository-relative path");
            }
            else if (files[path] is not { } facts)
            {
                failures.Add($"{citation}: file not found at {report.Meta.CommitSha[..Math.Min(7, report.Meta.CommitSha.Length)]}");
            }
            else if (!Declares(test ? facts.TestMethods : [.. facts.Methods, .. facts.Classes], name))
            {
                failures.Add($"{citation}: no {(test ? "test" : "method or class")} named {name}");
            }
        }

        return new CitationCheck(citations.Count, citations.Count - failures.Count, failures);
    }

    /// <summary>LLM-written paths are never trusted to stay inside the repository: no root, no "." or ".." segments.</summary>
    private static bool IsRepositoryRelative(string path) =>
        path.Length > 0 && !path.StartsWith('/') && !path.Contains('\\', StringComparison.Ordinal)
        && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    /// <summary>The citations written inside a free-text bullet.</summary>
    internal static IEnumerable<string> CitationsIn(string text) => CitationInText().Matches(text).Select(m => m.Value);

    /// <summary>Rewrites each citation inside a free-text bullet.</summary>
    internal static string RewriteCitations(string text, Func<string, string> map) => CitationInText().Replace(text, m => map(m.Value));

    /// <summary>A cited name resolves to a declared method or class by its simple or (partly) qualified name.</summary>
    internal static bool Declares(IEnumerable<string> methods, string name) =>
        methods.Any(m => m == name || m.EndsWith("." + name, StringComparison.Ordinal));

    internal static (string Path, string Name) Split(string citation)
    {
        var at = citation.IndexOf("::", StringComparison.Ordinal);
        return at < 0 ? (citation, string.Empty) : (citation[..at], citation[(at + 2)..]);
    }

    /// <summary>
    /// A citation inside free text, such as "Evidence: tests/A.cs::Test, tests/B.cs::Other.". The name
    /// ends with a word character, so a sentence's closing period is not part of it.
    /// </summary>
    [GeneratedRegex(@"[^\s:,;()]+\.[A-Za-z0-9]+::[A-Za-z_](?:[\w.]*\w)?")]
    private static partial Regex CitationInText();
}
