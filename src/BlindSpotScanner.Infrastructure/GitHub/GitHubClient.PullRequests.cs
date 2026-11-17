using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using BlindSpotScanner.Application.GitHub;

namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>The fix-PR writes (D25): the Git Data API, a draft PR, its label and its status.</summary>
public sealed partial class GitHubClient
{
    private static readonly HashSet<string> FailedConclusions = ["failure", "timed_out", "cancelled", "action_required"];

    public async Task<string> GetCommitTreeShaAsync(string owner, string name, string commitSha, CancellationToken ct) =>
        (await SendJsonAsync(HttpMethod.Get, owner, name, $"{Repo(owner, name)}/git/commits/{Uri.EscapeDataString(commitSha)}", null, ct))["tree"]!["sha"]!.GetValue<string>();

    public async Task<string> CreateBlobAsync(string owner, string name, string content, CancellationToken ct)
    {
        var body = new JsonObject { ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), ["encoding"] = "base64" };
        return Sha(await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/git/blobs", body, ct));
    }

    public async Task<string> CreateTreeAsync(string owner, string name, string baseTreeSha, IReadOnlyList<(string Path, string BlobSha)> files, CancellationToken ct)
    {
        var tree = new JsonArray([.. files.Select(f => (JsonNode)new JsonObject { ["path"] = f.Path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = f.BlobSha })]);
        var body = new JsonObject { ["base_tree"] = baseTreeSha, ["tree"] = tree };
        return Sha(await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/git/trees", body, ct));
    }

    public async Task<string> CreateCommitAsync(string owner, string name, string message, string treeSha, string parentSha, CancellationToken ct)
    {
        var body = new JsonObject { ["message"] = message, ["tree"] = treeSha, ["parents"] = new JsonArray(parentSha) };
        return Sha(await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/git/commits", body, ct));
    }

    public async Task CreateBranchAsync(string owner, string name, string branch, string commitSha, CancellationToken ct)
    {
        var body = new JsonObject { ["ref"] = $"refs/heads/{branch}", ["sha"] = commitSha };
        try
        {
            await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/git/refs", body, ct);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            throw new GitHubBranchExistsException($"Branch {branch} already exists; rename it.");
        }
    }

    public async Task<OpenedPullRequest> CreateDraftPullRequestAsync(string owner, string name, string title, string body, string head, string baseBranch, CancellationToken ct)
    {
        var request = new JsonObject { ["title"] = title, ["body"] = body, ["head"] = head, ["base"] = baseBranch, ["draft"] = true };
        var pr = await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/pulls", request, ct);
        return new OpenedPullRequest(pr["number"]!.GetValue<int>(), pr["html_url"]!.GetValue<string>());
    }

    public async Task AddLabelsAsync(string owner, string name, int number, IReadOnlyList<string> labels, CancellationToken ct)
    {
        var body = new JsonObject { ["labels"] = new JsonArray([.. labels.Select(l => (JsonNode)l)]) };
        await SendJsonAsync(HttpMethod.Post, owner, name, $"{Repo(owner, name)}/issues/{number}/labels", body, ct);
    }

    public async Task<string> GetPullRequestStatusAsync(string owner, string name, int number, CancellationToken ct)
    {
        var pr = await SendJsonAsync(HttpMethod.Get, owner, name, $"{Repo(owner, name)}/pulls/{number}", null, ct);
        if (pr["merged"]?.GetValue<bool>() == true)
        {
            return "Merged";
        }

        if (pr["state"]?.GetValue<string>() == "closed")
        {
            return "Closed";
        }

        var headSha = pr["head"]!["sha"]!.GetValue<string>();
        var runs = (await SendJsonAsync(HttpMethod.Get, owner, name, $"{Repo(owner, name)}/commits/{Uri.EscapeDataString(headSha)}/check-runs", null, ct))["check_runs"] as JsonArray ?? [];
        if (runs.Count == 0)
        {
            return "Open";
        }

        var checks = runs.Any(r => FailedConclusions.Contains(r?["conclusion"]?.GetValue<string>() ?? string.Empty)) ? "failing"
            : runs.Any(r => r?["status"]?.GetValue<string>() != "completed") ? "pending"
            : "passing";
        return $"Open · checks {checks}";
    }

    private static string Sha(JsonNode node) => node["sha"]!.GetValue<string>();

    /// <summary>A JSON request with the scan's headers and auth; access errors map like reads (no token in messages).</summary>
    private async Task<JsonNode> SendJsonAsync(HttpMethod method, string owner, string name, string path, JsonObject? body, CancellationToken ct)
    {
        using var request = NewRequest(path, "application/vnd.github+json");
        request.Method = method;
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GitHubNotFoundException($"Repository {owner}/{name} was not found or the token has no access to it.");
        }

        ThrowForAccessErrors(response, owner, name);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) ?? new JsonObject();
    }
}
