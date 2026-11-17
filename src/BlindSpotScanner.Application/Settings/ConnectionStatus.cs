namespace BlindSpotScanner.Application.Settings;

/// <param name="Kind">github, jira or llm.</param>
/// <param name="Masked">The secret as ••••last4, or null when none is set (doc 10: never returned in full).</param>
/// <param name="Details">Non-secret settings worth showing (URLs, project, model).</param>
public sealed record ConnectionStatus(string Kind, bool Configured, string? Masked, IReadOnlyDictionary<string, string> Details);

public sealed record ConnectionTestResult(bool Ok, string Message);

/// <summary>The read-only settings page (D26): what is configured, and a cheap live check per connection.</summary>
public interface IConnectionSettings
{
    IReadOnlyList<ConnectionStatus> Statuses();

    /// <returns>Null for an unknown kind.</returns>
    Task<ConnectionTestResult?> TestAsync(string kind, CancellationToken ct);
}

public static class SecretMask
{
    /// <summary>"••••" plus the last 4 characters; secrets of 4 characters or fewer show only "••••"; empty is null.</summary>
    public static string? Mask(string? secret) =>
        string.IsNullOrWhiteSpace(secret) ? null : secret.Length <= 4 ? "••••" : "••••" + secret[^4..];
}
