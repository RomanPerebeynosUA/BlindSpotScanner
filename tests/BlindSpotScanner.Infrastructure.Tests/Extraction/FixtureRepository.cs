using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Infrastructure.Tests.Extraction;

/// <summary>
/// A repository served from a directory on disk: one untruncated recursive tree, blob SHA =
/// "blob:" + path. Enough for P2–P4 to run over a fixture repo without network.
/// </summary>
internal sealed class FixtureRepository(string root) : IGitHubClient, IBlobReader
{
    private const string BlobPrefix = "blob:";

    public static FixtureRepository Bookstore() => new(Path.Combine(AppContext.BaseDirectory, "fixtures", "bookstore"));

    public Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct) =>
        Task.FromResult(new GitHubRepository(owner, name, "main", $"https://github.com/{owner}/{name}"));

    public Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) =>
        Task.FromResult("a1b2c3d4e5f60718293a4b5c6d7e8f9012345678");

    public Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct) =>
        Task.FromResult(new GitTree(
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Select(p => new GitTreeEntry(p, GitEntryType.Blob, BlobPrefix + p, new FileInfo(Path.Combine(root, p)).Length))
                .ToList(),
            Truncated: false));

    public async Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct) =>
        File.Exists(Path.Combine(root, path)) ? await File.ReadAllTextAsync(Path.Combine(root, path), ct) : null;

    public Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct) =>
        File.ReadAllTextAsync(Path.Combine(root, blobSha[BlobPrefix.Length..]), ct);

    public Task<string> ReadAsync(string owner, string name, string blobSha, CancellationToken ct) =>
        GetBlobTextAsync(owner, name, blobSha, ct);
}
