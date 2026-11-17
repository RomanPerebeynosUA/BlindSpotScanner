namespace BlindSpotScanner.Application.GitHub;

/// <summary>The repository (or ref) doesn't exist, or the token has no access to it. P1 fails fast with this message.</summary>
public sealed class GitHubNotFoundException(string message) : Exception(message);

/// <summary>GitHub rejected the credentials. The message never contains the token.</summary>
public sealed class GitHubAuthenticationException(string message) : Exception(message);

/// <summary>Failures that mean GitHub can't answer now (credentials, rate limit, server error), as opposed to "not found".</summary>
public static class GitHubFailures
{
    public static bool IsUnavailable(Exception ex) => ex is GitHubAuthenticationException or GitHubRateLimitException or HttpRequestException;
}

/// <summary>GitHub's rate limit is exhausted for longer than the configured wait, or retries ran out.</summary>
public sealed class GitHubRateLimitException(DateTimeOffset resetAt)
    : Exception($"GitHub rate limit exceeded; it resets at {resetAt:u}.")
{
    public DateTimeOffset ResetAt { get; } = resetAt;
}
