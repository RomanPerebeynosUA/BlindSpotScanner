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
        "**/bin/**", "**/obj/**", "**/node_modules/**", "**/vendor/**", "**/Generated/**", "**/*.Designer.cs", "**/*.g.cs",
    ];

    public async Task<RepositoryInventory> EnumerateAsync(RunContext run, RepoProfile profile, CancellationToken ct)
    {
        var (blobs, unverifiedReasons) = await ListBlobsAsync(run, ct);
        var candidates = Match(blobs.Keys, ["**/*"], DefaultExcludes);
        var testPaths = Match(candidates, profile.TestGlobs, []);
        var controllerPaths = Match(candidates, profile.ControllerGlobs, []).Except(testPaths);

        return new RepositoryInventory(
            Files(controllerPaths, blobs),
            Files(testPaths, blobs),
            unverifiedReasons);
    }

    /// <summary>
    /// One recursive read; if GitHub truncates it, walk tree by tree without recursion. A single
    /// tree that is still truncated is recorded, never silently counted as complete.
    /// </summary>
    private async Task<(Dictionary<string, GitTreeEntry> Blobs, List<string> UnverifiedReasons)> ListBlobsAsync(RunContext run, CancellationToken ct)
    {
        var blobs = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        var reasons = new List<string>();

        var recursive = await github.GetTreeAsync(run.Owner, run.Name, run.CommitSha, recursive: true, ct);
        if (!recursive.Truncated)
        {
            foreach (var entry in recursive.Entries.Where(e => e.Type == GitEntryType.Blob))
            {
                blobs[entry.Path] = entry;
            }

            return (blobs, reasons);
        }

        var pending = new Queue<(string Prefix, string Sha)>([(string.Empty, run.CommitSha)]);
        while (pending.TryDequeue(out var tree))
        {
            var level = await github.GetTreeAsync(run.Owner, run.Name, tree.Sha, recursive: false, ct);
            if (level.Truncated)
            {
                var at = tree.Prefix.Length == 0 ? "(root)" : tree.Prefix.TrimEnd('/');
                reasons.Add($"tree truncated at '{at}'; files below it were not enumerated");
            }

            foreach (var entry in level.Entries)
            {
                var path = tree.Prefix + entry.Path;
                switch (entry.Type)
                {
                    case GitEntryType.Blob:
                        blobs[path] = entry with { Path = path };
                        break;
                    case GitEntryType.Tree:
                        pending.Enqueue((path + "/", entry.Sha));
                        break;
                }
            }
        }

        return (blobs, reasons);
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
