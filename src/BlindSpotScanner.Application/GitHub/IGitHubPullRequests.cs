namespace BlindSpotScanner.Application.GitHub;

public sealed record OpenedPullRequest(int Number, string Url);

/// <summary>A fix-PR write failed; the message names the step and never the token (D25).</summary>
public sealed class GitHubWriteException(string message, Exception inner) : Exception(message, inner);

/// <summary>The branch for a fix PR already exists (GitHub answers 422 "Reference already exists" on the ref).</summary>
public sealed class GitHubBranchExistsException(string message) : Exception(message);

/// <summary>
/// The GitHub writes a fix PR needs (doc 09): the Git Data API (blobs → tree → commit → ref), then a
/// draft PR and its label, plus the PR's status. Separate from the scan's read-only <see cref="IGitHubClient"/>.
/// </summary>
public interface IGitHubPullRequests
{
    /// <summary>The commit's tree SHA, the base tree for the fix commit.</summary>
    Task<string> GetCommitTreeShaAsync(string owner, string name, string commitSha, CancellationToken ct);

    Task<string> CreateBlobAsync(string owner, string name, string content, CancellationToken ct);

    Task<string> CreateTreeAsync(string owner, string name, string baseTreeSha, IReadOnlyList<(string Path, string BlobSha)> files, CancellationToken ct);

    Task<string> CreateCommitAsync(string owner, string name, string message, string treeSha, string parentSha, CancellationToken ct);

    /// <exception cref="GitHubBranchExistsException">The branch already exists.</exception>
    Task CreateBranchAsync(string owner, string name, string branch, string commitSha, CancellationToken ct);

    Task<OpenedPullRequest> CreateDraftPullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch, CancellationToken ct);

    Task AddLabelsAsync(string owner, string name, int number, IReadOnlyList<string> labels, CancellationToken ct);

    /// <summary>"Merged", "Closed", "Open", or "Open · checks passing|failing|pending" (D25).</summary>
    Task<string> GetPullRequestStatusAsync(string owner, string name, int number, CancellationToken ct);
}
