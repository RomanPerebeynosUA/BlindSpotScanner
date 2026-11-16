using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Tests.Scanning;

/// <summary>
/// An in-memory repository: a flat list of file paths at one commit. Trees are derived from the
/// paths, so recursive and non-recursive reads stay consistent. Chosen trees can be marked truncated.
/// </summary>
internal sealed class FakeGitHubClient(string defaultBranch, IReadOnlyDictionary<string, string> branchShas, IReadOnlyList<string> files) : IGitHubClient
{
    public const string Owner = "example-org";
    public const string Name = "bookstore-api";

    /// <summary>Tree "paths" ("" = root) whose recursive read reports truncated:true.</summary>
    public HashSet<string> TruncatedRecursive { get; } = [];

    /// <summary>Tree paths whose non-recursive read reports truncated:true.</summary>
    public HashSet<string> TruncatedFlat { get; } = [];

    /// <summary>Paths of submodules (reported as "commit" entries).</summary>
    public List<string> Submodules { get; } = [];

    /// <summary>How many tree reads were made (to keep the request count bounded on huge repos).</summary>
    public int TreeRequests { get; private set; }

    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) =>
        Task.FromResult(new GitHubRepository(Owner, Name, defaultBranch, $"https://github.com/{Owner}/{Name}"));

    public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) =>
        Task.FromResult(branchShas[branch]);

    public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct)
    {
        // Tree SHAs are "tree:{path}"; any commit SHA means the root.
        var root = treeSha.StartsWith("tree:", StringComparison.Ordinal) ? treeSha["tree:".Length..] : string.Empty;
        var prefix = root.Length == 0 ? string.Empty : root + "/";
        var below = files.Where(f => f.StartsWith(prefix, StringComparison.Ordinal)).Select(f => f[prefix.Length..]).ToList();

        IEnumerable<GitTreeEntry> entries = recursive
            ? below.SelectMany(AllEntries).DistinctBy(e => e.Path)
            : below.Select(f => f.Split('/')).Select(parts => parts.Length == 1
                    ? new GitTreeEntry(parts[0], GitEntryType.Blob, $"blob:{prefix}{parts[0]}", 10)
                    : new GitTreeEntry(parts[0], GitEntryType.Tree, $"tree:{prefix}{parts[0]}", null))
                .DistinctBy(e => e.Path);

        TreeRequests++;
        var submodules = Submodules
            .Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
            .Select(s => s[prefix.Length..])
            .Where(s => recursive || !s.Contains('/', StringComparison.Ordinal))
            .Select(s => new GitTreeEntry(s, GitEntryType.Commit, $"commit:{prefix}{s}", null));
        var truncated = recursive ? TruncatedRecursive.Contains(root) : TruncatedFlat.Contains(root);
        return Task.FromResult(new GitTree(entries.Concat(submodules).ToList(), truncated));

        IEnumerable<GitTreeEntry> AllEntries(string relative)
        {
            var parts = relative.Split('/');
            for (var i = 1; i < parts.Length; i++)
            {
                var dir = string.Join('/', parts[..i]);
                yield return new GitTreeEntry(dir, GitEntryType.Tree, $"tree:{prefix}{dir}", null);
            }

            yield return new GitTreeEntry(relative, GitEntryType.Blob, $"blob:{prefix}{relative}", 10);
        }
    }

    public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct) =>
        throw new NotSupportedException("Enumeration never reads blobs.");
}
