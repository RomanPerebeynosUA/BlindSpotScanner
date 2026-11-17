using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Repositories;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Remediation;

/// <summary>
/// A repository's Jira tickets and fix PRs, with statuses older than 10 minutes re-read (doc 09,
/// D24/D25). A failed read keeps the last known status and still moves the check time on, so an
/// unreadable ticket or PR is retried after 10 minutes rather than on every report view.
/// </summary>
public sealed class RemediationActions(IRemediationStore store, IJiraClient jira, IGitHubPullRequests pulls, IRepositoryStore repositories, TimeProvider time)
{
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<RemediationAction>> ActionsAsync(Guid repositoryId, CancellationToken ct)
    {
        var actions = await store.ActionsAsync(repositoryId, ct);
        var now = time.GetUtcNow();
        var stale = actions.Where(a => now - a.StatusCheckedAt >= StatusTtl && (a.Type == ActionType.PullRequest || jira.IsConfigured)).ToList();
        if (stale.Count == 0)
        {
            return actions;
        }

        var repository = stale.Any(a => a.Type == ActionType.PullRequest) ? await repositories.FindAsync(repositoryId, ct) : null;
        foreach (var action in stale)
        {
            try
            {
                action.Status = action.Type == ActionType.JiraTicket
                    ? await jira.GetStatusAsync(action.ExternalKey, ct)
                    : repository is null ? action.Status : await pulls.GetPullRequestStatusAsync(repository.Owner, repository.Name, int.Parse(action.ExternalKey[1..], System.Globalization.CultureInfo.InvariantCulture), ct);
            }
            catch (Exception e) when (e is JiraException or HttpRequestException or GitHubAuthenticationException or GitHubNotFoundException)
            {
                // The report must never fail because Jira or GitHub is down; the cached status stays.
            }

            action.StatusCheckedAt = now;
        }

        await store.SaveChangesAsync(ct);
        return actions;
    }
}
