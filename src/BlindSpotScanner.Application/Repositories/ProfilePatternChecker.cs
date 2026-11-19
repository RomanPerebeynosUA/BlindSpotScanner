using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Application.Repositories;

public enum PatternCheckStatus
{
    Checked,
    RepositoryNotFound,
    GitHubFailed,
}

/// <param name="Errors">Syntax errors; malformed patterns are still counted.</param>
public sealed record PatternCheckResult(PatternCheckStatus Status, IReadOnlyList<PatternMatch> Matches, IReadOnlyList<string> Errors);

/// <summary>Recounts edited profile patterns against the default branch head (the editor's "Check patterns", D29).</summary>
public sealed class ProfilePatternChecker(IRepositoryStore repositories, IGitHubClient github)
{
    public async Task<PatternCheckResult> CheckAsync(Guid repositoryId, ProfileRules rules, CancellationToken ct)
    {
        if (await repositories.FindAsync(repositoryId, ct) is not { } repository)
        {
            return new PatternCheckResult(PatternCheckStatus.RepositoryNotFound, [], []);
        }

        try
        {
            var head = await github.GetBranchHeadShaAsync(repository.Owner, repository.Name, repository.DefaultBranch, ct);
            var tree = await github.GetTreeAsync(repository.Owner, repository.Name, head, recursive: true, ct);
            var paths = tree.Entries.Where(e => e.Type == GitEntryType.Blob).Select(e => e.Path);
            return new PatternCheckResult(PatternCheckStatus.Checked, ProfilePatterns.Count(paths, rules), ProfilePatterns.SyntaxErrors(rules));
        }
        catch (Exception ex) when (ex is GitHubNotFoundException || GitHubFailures.IsUnavailable(ex))
        {
            return new PatternCheckResult(PatternCheckStatus.GitHubFailed, [], [ex is HttpRequestException ? "GitHub did not answer; try again later." : ex.Message]);
        }
    }
}
