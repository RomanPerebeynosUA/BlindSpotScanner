namespace BlindSpotScanner.Application.Remediation;

/// <param name="DescriptionMarkdown">The (edited) description; the client appends the stable-key footer (D24).</param>
public sealed record JiraIssueRequest(
    string ProjectKey, string IssueType, string Summary, string DescriptionMarkdown, string Priority, IReadOnlyList<string> Labels, string StableKey);

public sealed record JiraIssueRef(string Key, string Url, string Status);

/// <summary>A Jira call failed. The message never carries the token or the Authorization header (principle 9).</summary>
public sealed class JiraException(string message) : Exception(message);

/// <summary>The three Jira Cloud REST calls M9 needs (doc 09).</summary>
public interface IJiraClient
{
    /// <summary>False until the Jira site, email, API token and project key are all configured (D24).</summary>
    bool IsConfigured { get; }

    Task<JiraIssueRef> CreateIssueAsync(JiraIssueRequest request, CancellationToken ct);

    /// <summary>The ticket whose description footer holds exactly this stable key, if Jira has one.</summary>
    Task<JiraIssueRef?> FindByStableKeyAsync(string stableKey, CancellationToken ct);

    Task<string> GetStatusAsync(string key, CancellationToken ct);
}
