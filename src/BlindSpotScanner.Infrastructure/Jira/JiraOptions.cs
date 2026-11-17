namespace BlindSpotScanner.Infrastructure.Jira;

/// <summary>The "Jira" configuration section (doc 10, D24). The API token comes from user secrets or an env var, never from appsettings.</summary>
public sealed class JiraOptions
{
    /// <summary>The Jira Cloud site, for example https://example.atlassian.net.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;

    public string ProjectKey { get; set; } = string.Empty;

    public string IssueType { get; set; } = "Task";

    public string[] Labels { get; set; } = ["integration-coverage"];

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(ApiToken) && !string.IsNullOrWhiteSpace(ProjectKey);
}
