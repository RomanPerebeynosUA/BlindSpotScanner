using System.Text.RegularExpressions;

namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>
/// Principle 9 / doc 10: replaces common secret formats with «redacted» before any text reaches the
/// LLM. Key/value secrets keep their key (<c>Password=«redacted»</c>) so the code still reads.
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
        text = AnthropicKey().Replace(text, Redacted);
        text = SkStyleKey().Replace(text, Redacted);
        text = SlackToken().Replace(text, Redacted);
        text = GoogleApiKey().Replace(text, Redacted);
        text = Jwt().Replace(text, Redacted);
        text = AzureKeyValue().Replace(text, "${key}" + Redacted);
        text = WithPasswordCall().Replace(text, "${key}" + Redacted);
        text = JsonSecretProperty().Replace(text, "${key}" + Redacted);
        return ConnectionStringPassword().Replace(text, "${key}" + Redacted);
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    /// <summary>Long-term (AKIA) and temporary (ASIA) AWS access key ids.</summary>
    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b")]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b")]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\bgithub_pat_[A-Za-z0-9_]{22,}")]
    private static partial Regex GitHubFineGrainedToken();

    [GeneratedRegex(@"\bsk-ant-[A-Za-z0-9_-]{20,}")]
    private static partial Regex AnthropicKey();

    /// <summary>OpenAI-style and similar "sk-" keys.</summary>
    [GeneratedRegex(@"\bsk-[A-Za-z0-9]{20,}")]
    private static partial Regex SkStyleKey();

    [GeneratedRegex(@"\bxox[abposr]-[A-Za-z0-9-]{10,}")]
    private static partial Regex SlackToken();

    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35}")]
    private static partial Regex GoogleApiKey();

    [GeneratedRegex(@"\beyJ[\w-]+\.[\w-]+\.[\w-]+")]
    private static partial Regex Jwt();

    /// <summary>Azure storage, Service Bus and SAS secrets in connection strings and URLs.</summary>
    [GeneratedRegex(@"(?<key>\b(?:AccountKey|SharedAccessKey|SharedAccessSignature)=)[^;""'\s&]+")]
    private static partial Regex AzureKeyValue();

    /// <summary>Testcontainers-style builders: <c>.WithPassword("…")</c>.</summary>
    [GeneratedRegex(@"(?<key>\.WithPassword\(\s*"")[^""]*")]
    private static partial Regex WithPasswordCall();

    /// <summary>JSON settings such as <c>"password": "…"</c>.</summary>
    [GeneratedRegex(@"(?<key>""(?:password|pwd|secret|apikey|api_key)""\s*:\s*"")[^""]*", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretProperty();

    /// <summary>
    /// Connection-string style only: no spaces around "=", not a member access (<c>user.Password</c>)
    /// and not a comparison (<c>==</c>), so code stays readable.
    /// </summary>
    [GeneratedRegex(@"(?<key>(?<![.\w])(?:password|pwd)=)(?!=)[^;""'\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();
}
