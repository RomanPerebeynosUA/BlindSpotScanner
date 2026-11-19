using BlindSpotScanner.Application.Scanning;
using Microsoft.Extensions.FileSystemGlobbing;

namespace BlindSpotScanner.Application.Repositories;

/// <summary>A profile's typed classification rules (D18), as the AI draft proposes them (D29).</summary>
public sealed record ProfileRules(
    string IntegrationMappedTo,
    IReadOnlyList<string> TestGlobs,
    IReadOnlyList<string> ControllerGlobs,
    IReadOnlyList<string> MinimalApiGlobs,
    IReadOnlyList<string> IntegrationGlobs,
    IReadOnlyList<string> IntegrationAttributes,
    IReadOnlyList<string> IntegrationBaseTypes,
    IReadOnlyList<string> NotIntegrationGlobs);

/// <param name="Field">The rule's JSON name, e.g. <c>minimalApiGlobs</c>.</param>
public sealed record PatternMatch(string Field, string Pattern, int Files);

/// <summary>
/// Checks a profile's glob patterns (D29): each entry must be one pattern, and its match count is
/// taken over the same files a scan enumerates (tree blobs minus the fixed excludes, case-sensitive).
/// Attribute and base-type entries are names, not globs, so they are neither checked nor counted.
/// </summary>
public static class ProfilePatterns
{
    public static IReadOnlyList<string> SyntaxErrors(ProfileRules rules) =>
        Globs(rules)
            .SelectMany(g => g.Patterns.Where(p => p.Any(c => char.IsWhiteSpace(c) || c == ',')).Select(p => (g.Field, Pattern: p)))
            .Select(x => $"{x.Field}: '{x.Pattern}' is not one pattern; put one pattern per line with no spaces or commas")
            .ToList();

    public static IReadOnlyList<PatternMatch> Count(IEnumerable<string> treePaths, ProfileRules rules)
    {
        var paths = treePaths.Where(p => !EnumeratePhase.IsExcluded(p)).ToList();
        return Globs(rules)
            .SelectMany(g => g.Patterns.Select(p => new PatternMatch(g.Field, p, Files(paths, p))))
            .ToList();
    }

    private static int Files(List<string> paths, string pattern)
    {
        // Git paths are case-sensitive.
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);
        return matcher.Match(paths).Files.Count();
    }

    private static (string Field, IReadOnlyList<string> Patterns)[] Globs(ProfileRules rules) =>
    [
        ("testGlobs", rules.TestGlobs),
        ("controllerGlobs", rules.ControllerGlobs),
        ("minimalApiGlobs", rules.MinimalApiGlobs),
        ("integrationGlobs", rules.IntegrationGlobs),
        ("notIntegrationGlobs", rules.NotIntegrationGlobs),
    ];
}
