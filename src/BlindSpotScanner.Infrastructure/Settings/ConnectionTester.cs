using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using BlindSpotScanner.Application.Settings;
using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Jira;
using BlindSpotScanner.Infrastructure.Llm;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Settings;

/// <summary>
/// The settings status (D26): each connection's non-secret details and its secret masked, plus one
/// cheap read to test it (GitHub <c>/user</c>, Jira <c>/myself</c>, Claude <c>/v1/models</c>).
/// Results never carry a secret (doc 10).
/// </summary>
public sealed class ConnectionTester(HttpClient http, IOptions<GitHubOptions> github, IOptions<JiraOptions> jira, IOptions<LlmOptions> llm) : IConnectionSettings
{
    private const int MaxReasonLength = 200;

    private string? ClaudeKey => llm.Value.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    public IReadOnlyList<ConnectionStatus> Statuses()
    {
        var (g, j, l) = (github.Value, jira.Value, llm.Value);
        return
        [
            new("github", !string.IsNullOrWhiteSpace(g.Token), SecretMask.Mask(g.Token), new Dictionary<string, string> { ["apiBaseUrl"] = g.ApiBaseUrl }),
            new("jira", j.IsConfigured, SecretMask.Mask(j.ApiToken), new Dictionary<string, string>
            {
                ["baseUrl"] = j.BaseUrl, ["email"] = j.Email, ["projectKey"] = j.ProjectKey, ["issueType"] = j.IssueType,
            }),
            new("llm", !string.IsNullOrWhiteSpace(ClaudeKey), SecretMask.Mask(ClaudeKey), new Dictionary<string, string>
            {
                ["model"] = l.Model,
                ["maxCostUsdPerScan"] = l.MaxCostUsdPerScan.ToString(CultureInfo.InvariantCulture),
                ["promptSetVersion"] = l.PromptSetVersion,
            }),
        ];
    }

    public async Task<ConnectionTestResult?> TestAsync(string kind, CancellationToken ct)
    {
        try
        {
            return kind switch
            {
                "github" => await GitHubAsync(ct),
                "jira" => await JiraAsync(ct),
                "llm" => await ClaudeAsync(ct),
                _ => null,
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ConnectionTestResult(false, $"Could not reach {Name(kind)}.");
        }
    }

    private async Task<ConnectionTestResult> GitHubAsync(CancellationToken ct)
    {
        var options = github.Value;
        if (string.IsNullOrWhiteSpace(options.Token))
        {
            return new ConnectionTestResult(false, "No GitHub token is configured (public repositories still work, at a low rate limit).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.ApiBaseUrl.TrimEnd('/')}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BlindSpotScanner", null));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return await ReadAsync(request, "GitHub", body => $"Connected to GitHub as {body?["login"]?.GetValue<string>()}.", ct);
    }

    private async Task<ConnectionTestResult> JiraAsync(CancellationToken ct)
    {
        var options = jira.Value;
        if (!options.IsConfigured)
        {
            return new ConnectionTestResult(false, "Jira isn't configured: set Jira:BaseUrl, Jira:Email, Jira:ApiToken and Jira:ProjectKey.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.BaseUrl.TrimEnd('/')}/rest/api/3/myself");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Email}:{options.ApiToken}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await ReadAsync(request, "Jira", body => $"Connected to Jira as {body?["displayName"]?.GetValue<string>()}.", ct);
    }

    private async Task<ConnectionTestResult> ClaudeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ClaudeKey))
        {
            return new ConnectionTestResult(false, "No Claude API key is configured (ANTHROPIC_API_KEY).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{(llm.Value.BaseUrl ?? "https://api.anthropic.com").TrimEnd('/')}/v1/models");
        request.Headers.Add("x-api-key", ClaudeKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        return await ReadAsync(request, "Claude", _ => "Connected to Claude.", ct, ClaudeReason, ClaudeKey);
    }

    /// <summary>
    /// The status code reaches the message, plus the reason <paramref name="reason"/> reads from an error
    /// body (never request headers). A reason that contains <paramref name="secret"/> is dropped (doc 10).
    /// </summary>
    private async Task<ConnectionTestResult> ReadAsync(
        HttpRequestMessage request, string name, Func<JsonNode?, string> ok, CancellationToken ct,
        Func<JsonNode?, string?>? reason = null, string? secret = null)
    {
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var why = reason is null ? null : reason(await TryParseAsync(response, ct));
            var safe = why is not null && (secret is null || !why.Contains(secret, StringComparison.Ordinal)) ? $" ({why})" : string.Empty;
            return new ConnectionTestResult(false, $"{name} answered {(int)response.StatusCode}{safe}; check the configured credentials.");
        }

        JsonNode? body;
        try
        {
            body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (System.Text.Json.JsonException)
        {
            return new ConnectionTestResult(false, $"{name} sent an unreadable answer; check the configured URL.");
        }

        return new ConnectionTestResult(true, ok(body));
    }

    private static async Task<JsonNode?> TryParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Claude's error body: <c>{"type":"error","error":{"type":"…","message":"…"}}</c>, at most 200 characters.</summary>
    private static string? ClaudeReason(JsonNode? body)
    {
        if (body?["error"] is not JsonObject error || error["type"] is not JsonValue type || error["message"] is not JsonValue message
            || !type.TryGetValue<string>(out var kind) || !message.TryGetValue<string>(out var text))
        {
            return null;
        }

        var reason = $"{kind}: {text}";
        return reason.Length <= MaxReasonLength ? reason : reason[..MaxReasonLength] + "…";
    }

    private static string Name(string kind) => kind switch { "github" => "GitHub", "jira" => "Jira", _ => "Claude" };
}
