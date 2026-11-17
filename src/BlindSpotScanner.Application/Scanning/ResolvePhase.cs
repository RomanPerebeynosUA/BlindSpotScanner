using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Domain;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P1 (doc 05): parse the URL, check access, and pin the branch to a commit SHA. Invalid URLs
/// (<see cref="FormatException"/>) and missing or inaccessible repositories
/// (<see cref="GitHubNotFoundException"/>) surface unchanged, so the run fails fast with a clear message.
/// Loading the repo profile is the orchestrator's job (M6).
/// </summary>
public sealed class ResolvePhase(IGitHubClient github)
{
    public async Task<RunContext> ResolveAsync(string repositoryUrl, CancellationToken ct)
    {
        var url = GitHubRepoUrl.Parse(repositoryUrl);
        var repository = await github.GetRepositoryAsync(url.Owner, url.Name, ct);
        var branch = url.Branch ?? repository.DefaultBranch;
        var sha = await github.GetBranchHeadShaAsync(repository.Owner, repository.Name, branch, ct);
        return new RunContext(repository.Owner, repository.Name, branch, sha, repository.HtmlUrl);
    }
}
