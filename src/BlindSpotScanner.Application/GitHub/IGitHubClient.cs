namespace BlindSpotScanner.Application.GitHub;

/// <summary>Read-only access to a GitHub repository at a pinned commit (doc 05, P1–P3).</summary>
public interface IGitHubClient
{
    /// <exception cref="GitHubNotFoundException">The repository doesn't exist or the token can't see it.</exception>
    /// <exception cref="GitHubAuthenticationException">The token is missing, invalid or expired.</exception>
    Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct);

    /// <summary>Resolves a branch to its head commit SHA; every later read is pinned to it.</summary>
    Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct);

    /// <param name="treeSha">A tree or commit SHA.</param>
    Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct);

    /// <summary>The blob's content decoded as UTF-8.</summary>
    /// <summary>A file's text at a commit, or null when the file doesn't exist there (the citation check, D21).</summary>
    Task<string?> GetFileTextAsync(string owner, string name, string path, string commitSha, CancellationToken ct);

    Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct);
}

public sealed record GitHubRepository(string Owner, string Name, string DefaultBranch, string HtmlUrl);

public sealed record GitTree(IReadOnlyList<GitTreeEntry> Entries, bool Truncated);

/// <param name="Path">Relative to the tree that was requested.</param>
public sealed record GitTreeEntry(string Path, GitEntryType Type, string Sha, long? Size);

public enum GitEntryType
{
    Blob,
    Tree,

    /// <summary>A submodule pointer.</summary>
    Commit,

    /// <summary>A symbolic link (mode 120000); its "content" is the link target, not code.</summary>
    Symlink,
}
