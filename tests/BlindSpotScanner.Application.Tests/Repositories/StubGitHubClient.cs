using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Tests.Repositories;

/// <summary>A GitHub that knows one repository (or fails with a chosen exception) and a set of branches.</summary>
internal sealed class StubGitHubClient : IGitHubClient
{
    public GitHubRepository Repository { get; set; } = new("Example-Org", "Bookstore", "main", "https://github.com/Example-Org/Bookstore");

    public Exception? RepositoryError { get; set; }

    public HashSet<string> Branches { get; } = ["main", "dev", "feat/x"];

    public string? RequestedBranch { get; private set; }

    /// <summary>The files at the branch head, by path; blob SHAs are the paths.</summary>
    public Dictionary<string, string> Files { get; } = [];

    public bool TreeTruncated { get; set; }

    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) =>
        RepositoryError is { } error ? Task.FromException<GitHubRepository>(error) : Task.FromResult(Repository);

    public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct)
    {
        RequestedBranch = branch;
        return Branches.Contains(branch)
            ? Task.FromResult("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678")
            : Task.FromException<string>(new GitHubNotFoundException($"Branch '{branch}' not found."));
    }

    public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct) =>
        Task.FromResult(new GitTree(
            [.. Files.Select(f => new GitTreeEntry(f.Key, GitEntryType.Blob, f.Key, System.Text.Encoding.UTF8.GetByteCount(f.Value))), new GitTreeEntry("src", GitEntryType.Tree, "tree:src", null)],
            TreeTruncated));

    public Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct) => Task.FromResult(Files[blobSha]);
}
