using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain.Entities;
using Microsoft.Extensions.FileSystemGlobbing;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P2 (doc 05): list every file at the pinned commit with the Git Trees API, then apply the
/// profile's globs. Test files are never part of the denominator (principle 3).
/// </summary>
public sealed class EnumeratePhase(IGitHubClient github)
{
    /// <summary>Vendored, generated and build-output paths are never production or test code.</summary>
    private static readonly string[] DefaultExcludes =
    [
        "**/bin/**", "**/obj/**", "**/node_modules/**", "**/vendor/**", "**/third_party/**", "**/dist/**", "**/wwwroot/lib/**",
        "**/Generated/**", "**/generated/**", "**/*.Designer.cs", "**/*.g.cs",
    ];

    private static readonly Microsoft.Extensions.FileSystemGlobbing.Matcher ExcludedPaths = CreateExcludedPaths();

    /// <summary>Whether a path is vendored, generated or build output (the fixed excludes, D17).</summary>
    public static bool IsExcluded(string path) => ExcludedPaths.Match(path).HasMatches;

    private static Microsoft.Extensions.FileSystemGlobbing.Matcher CreateExcludedPaths()
    {
        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(DefaultExcludes);
        return matcher;
    }

    public async Task<RepositoryInventory> EnumerateAsync(RunContext run, RepoProfile profile, CancellationToken ct)
    {
        var (blobs, unverifiedReasons) = await ListBlobsAsync(run, ct);
        var candidates = Match(blobs.Keys, ["**/*"], DefaultExcludes);
        var testPaths = Match(candidates, profile.TestGlobs, []);
        var controllerPaths = Match(candidates, [.. profile.ControllerGlobs, .. profile.MinimalApiGlobs], []).Except(testPaths);

        return new RepositoryInventory(
            Files(controllerPaths, blobs),
            Files(testPaths, blobs),
            unverifiedReasons);
    }

    /// <summary>
    /// Recursive read first. Only a tree whose recursive read is truncated is opened one level
    /// (its children are each tried recursively again), so a huge repo costs requests only along the
    /// truncated paths, not one per directory. A tree that is still truncated, and every submodule,
    /// is recorded: never silently counted as complete.
    /// </summary>
    private async Task<(Dictionary<string, GitTreeEntry> Blobs, List<string> UnverifiedReasons)> ListBlobsAsync(RunContext run, CancellationToken ct)
    {
        var blobs = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        var reasons = new List<string>();
        await ReadTreeAsync(run, prefix: string.Empty, run.CommitSha, blobs, reasons, ct);
        return (blobs, reasons);
    }

    private async Task ReadTreeAsync(RunContext run, string prefix, string sha, Dictionary<string, GitTreeEntry> blobs, List<string> reasons, CancellationToken ct)
    {
        var recursive = await github.GetTreeAsync(run.Owner, run.Name, sha, recursive: true, ct);
        if (!recursive.Truncated)
        {
            Collect(prefix, recursive.Entries, blobs, reasons);
            return;
        }

        var level = await github.GetTreeAsync(run.Owner, run.Name, sha, recursive: false, ct);
        if (level.Truncated)
        {
            var at = prefix.Length == 0 ? "(root)" : prefix.TrimEnd('/');
            reasons.Add($"tree truncated at '{at}'; files below it were not enumerated");
        }

        Collect(prefix, level.Entries, blobs, reasons);
        foreach (var subtree in level.Entries.Where(e => e.Type == GitEntryType.Tree))
        {
            await ReadTreeAsync(run, $"{prefix}{subtree.Path}/", subtree.Sha, blobs, reasons, ct);
        }
    }

    private static void Collect(string prefix, IEnumerable<GitTreeEntry> entries, Dictionary<string, GitTreeEntry> blobs, List<string> reasons)
    {
        foreach (var entry in entries)
        {
            var path = prefix + entry.Path;
            switch (entry.Type)
            {
                case GitEntryType.Blob:
                    blobs[path] = entry with { Path = path };
                    break;
                case GitEntryType.Commit:
                    // Submodule content lives in another repository; it is not read (principle 1: say so).
                    reasons.Add($"submodule at '{path}' was not enumerated");
                    break;
            }
        }
    }

    private static HashSet<string> Match(IEnumerable<string> paths, IEnumerable<string> includes, IEnumerable<string> excludes)
    {
        // Git paths are case-sensitive.
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(includes);
        matcher.AddExcludePatterns(excludes);
        return matcher.Match(paths).Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
    }

    private static List<RepoFile> Files(IEnumerable<string> paths, Dictionary<string, GitTreeEntry> blobs) =>
        paths.Order(StringComparer.Ordinal).Select(p => new RepoFile(p, blobs[p].Sha, blobs[p].Size)).ToList();
}
