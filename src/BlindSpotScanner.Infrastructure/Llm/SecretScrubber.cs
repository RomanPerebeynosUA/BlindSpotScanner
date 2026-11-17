using System.Text.RegularExpressions;

namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>
/// Principle 9 / doc 10: replaces common secret formats with «redacted» before any text reaches the
/// LLM. Connection-string passwords keep their key (<c>Password=«redacted»</c>) so the code still reads.
/// </summary>
public static partial class SecretScrubber
{
    public const string Redacted = "«redacted»";

    public static string Scrub(string text)
    {
        text = PrivateKeyBlock().Replace(text, Redacted);
        text = AwsAccessKey().Replace(text, Redacted);
        text = GitHubToken().Replace(text, Redacted);
        text = GitHubFineGrainedToken().Replace(text, Redacted);
        text = Jwt().Replace(text, Redacted);
        return ConnectionStringPassword().Replace(text, "${key}" + Redacted);
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bgithub_pat_[A-Za-z0-9_]{22,}")]
    private static partial Regex GitHubFineGrainedToken();

    [GeneratedRegex(@"\beyJ[\w-]+\.[\w-]+\.[\w-]+")]
    private static partial Regex Jwt();

    /// <summary>Connection-string style only (no spaces around "="), so <c>var password = x;</c> in code stays readable.</summary>
    [GeneratedRegex(@"(?<key>\b(?:password|pwd)=)[^;""'\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();
}
