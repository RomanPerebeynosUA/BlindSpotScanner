using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlindSpotScanner.Application.Remediation;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Jira;

/// <summary>
/// A small typed client for Jira Cloud REST v3: create an issue, find one by stable key (JQL), read
/// its status. Basic auth (email + API token) goes on each request, so the injected HttpClient is
/// never mutated, and no error message carries the token (principle 9).
/// </summary>
public sealed partial class JiraClient(HttpClient http, IOptions<JiraOptions> options) : IJiraClient
{
    /// <summary>The description footer that makes a ticket findable by its finding's stable key (D24).</summary>
    public const string FooterPrefix = "BlindSpotScanner key: ";

    private readonly JiraOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    private string BaseUrl => _options.BaseUrl.TrimEnd('/');

    public async Task<JiraIssueRef> CreateIssueAsync(JiraIssueRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["fields"] = new JsonObject
            {
                ["project"] = new JsonObject { ["key"] = request.ProjectKey },
                ["issuetype"] = new JsonObject { ["name"] = request.IssueType },
                ["summary"] = request.Summary,
                ["description"] = AdfConverter.ToAdf($"{request.DescriptionMarkdown.TrimEnd()}\n\n{FooterPrefix}{request.StableKey}"),
                ["priority"] = new JsonObject { ["name"] = request.Priority },
                ["labels"] = new JsonArray([.. request.Labels.Select(l => (JsonNode)l)]),
            },
        };

        var created = await SendAsync(HttpMethod.Post, "/rest/api/3/issue", body, "creating an issue", ct);
        var key = created["key"]!.GetValue<string>();

        // Jira's create response has no status; a new issue starts in the workflow's first status.
        return new JiraIssueRef(key, Browse(key), "To Do");
    }

    public async Task<JiraIssueRef?> FindByStableKeyAsync(string stableKey, CancellationToken ct)
    {
        // Text search is fuzzy and reserves most punctuation, so search the key's words and then
        // keep only a hit whose footer holds the exact key.
        var phrase = Spaces().Replace(NonAlphanumeric().Replace(stableKey, " "), " ").Trim();
        var body = new JsonObject
        {
            ["jql"] = $"labels = integration-coverage AND text ~ \"{phrase}\"",
            ["fields"] = new JsonArray("status", "description"),
            ["maxResults"] = 10,
        };

        var found = await SendAsync(HttpMethod.Post, "/rest/api/3/search/jql", body, "searching issues", ct);
        var footer = FooterPrefix + stableKey;
        var issue = (found["issues"] as JsonArray ?? []).FirstOrDefault(i => Texts(i?["fields"]?["description"]).Contains(footer));
        return issue is null
            ? null
            : new JiraIssueRef(issue["key"]!.GetValue<string>(), Browse(issue["key"]!.GetValue<string>()), Status(issue));
    }

    public async Task<string> GetStatusAsync(string key, CancellationToken ct) =>
        Status(await SendAsync(HttpMethod.Get, $"/rest/api/3/issue/{Uri.EscapeDataString(key)}?fields=status", null, "reading an issue", ct));

    private string Browse(string key) => $"{BaseUrl}/browse/{key}";

    private static string Status(JsonNode issue) => issue["fields"]?["status"]?["name"]?.GetValue<string>() ?? "Unknown";

    private static IEnumerable<string> Texts(JsonNode? node) => node switch
    {
        JsonObject o => (o["text"] is JsonValue t ? [t.GetValue<string>()] : Enumerable.Empty<string>()).Concat(Texts(o["content"])),
        JsonArray a => a.SelectMany(Texts),
        _ => [],
    };

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, string operation, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Email}:{_options.ApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new JiraException($"Jira returned {(int)response.StatusCode} for {operation}{Messages(text)}");
        }

        return JsonNode.Parse(text) ?? new JsonObject();
    }

    /// <summary>Jira's own <c>errorMessages</c> and field <c>errors</c>, which never echo credentials.</summary>
    private static string Messages(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            var messages = (node?["errorMessages"] as JsonArray ?? []).Select(m => m?.GetValue<string>())
                .Concat((node?["errors"] as JsonObject ?? []).Select(e => $"{e.Key}: {e.Value}"))
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();
            return messages.Count == 0 ? "." : $": {string.Join("; ", messages)}";
        }
        catch (JsonException)
        {
            return ".";
        }
    }

    [GeneratedRegex("[^A-Za-z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(" {2,}")]
    private static partial Regex Spaces();
}
