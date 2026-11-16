using System.Diagnostics.CodeAnalysis;

namespace BlindSpotScanner.Domain;

/// <summary>A GitHub repository URL: https://github.com/{owner}/{repo}[/tree/{branch}] (doc 05, P1).</summary>
public sealed record GitHubRepoUrl(string Owner, string Name, string? Branch)
{
    private const string ExpectedForm = "https://github.com/{owner}/{repo}[/tree/{branch}]";

    public static bool TryParse(string? text, [NotNullWhen(true)] out GitHubRepoUrl? url)
    {
        url = null;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                 || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length < 2)
        {
            return false;
        }

        var owner = segments[0];
        var name = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];
        if (name.Length == 0)
        {
            return false;
        }

        if (segments.Length == 2)
        {
            url = new GitHubRepoUrl(owner, name, null);
            return true;
        }

        // Branch names may contain "/", so everything after "tree" is the branch.
        if (segments.Length >= 4 && segments[2] == "tree")
        {
            url = new GitHubRepoUrl(owner, name, string.Join('/', segments[3..]));
            return true;
        }

        return false;
    }

    /// <exception cref="FormatException">The text is not a GitHub repository URL.</exception>
    public static GitHubRepoUrl Parse(string text) =>
        TryParse(text, out var url)
            ? url
            : throw new FormatException($"'{text}' is not a GitHub repository URL. Expected {ExpectedForm}.");
}
