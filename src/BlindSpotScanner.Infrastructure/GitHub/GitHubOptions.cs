namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>The "GitHub" configuration section (doc 10). The token comes from user secrets or an env var, never from appsettings.</summary>
public sealed class GitHubOptions
{
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>A personal access token (D16). Null sends unauthenticated requests.</summary>
    public string? Token { get; set; }

    /// <summary>The longest a request waits for a rate limit to reset before failing.</summary>
    public TimeSpan MaxRateLimitWait { get; set; } = TimeSpan.FromSeconds(60);
}
