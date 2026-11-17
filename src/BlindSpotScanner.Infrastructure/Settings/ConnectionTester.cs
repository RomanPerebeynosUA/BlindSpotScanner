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
        return await ReadAsync(request, "Claude", _ => "Connected to Claude.", ct);
    }

    /// <summary>Only the status code reaches the message; response bodies and request headers never do.</summary>
    private async Task<ConnectionTestResult> ReadAsync(HttpRequestMessage request, string name, Func<JsonNode?, string> ok, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new ConnectionTestResult(false, $"{name} answered {(int)response.StatusCode}; check the configured credentials.");
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

    private static string Name(string kind) => kind switch { "github" => "GitHub", "jira" => "Jira", _ => "Claude" };
}
