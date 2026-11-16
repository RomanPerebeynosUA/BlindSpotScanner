using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlindSpotScanner.Application.GitHub;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.GitHub;

/// <summary>
/// A small typed client for the four GitHub REST endpoints the scan needs (D16). Its HttpClient's
/// handler chain should include <see cref="GitHubRateLimitHandler"/>.
/// </summary>
public sealed class GitHubClient : IGitHubClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public GitHubClient(HttpClient http, IOptions<GitHubOptions> options)
    {
        _http = http;
        _http.BaseAddress = new Uri(options.Value.ApiBaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindSpotScanner", null));
        if (!string.IsNullOrEmpty(options.Value.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.Token);
        }
    }

    public async Task<GitHubRepository> GetRepositoryAsync(string owner, string name, CancellationToken ct)
    {
        var repo = await GetAsync<RepoDto>(owner, name, Repo(owner, name), ct);
        return new GitHubRepository(repo.Owner.Login, repo.Name, repo.DefaultBranch, repo.HtmlUrl);
    }

    public async Task<string> GetBranchHeadShaAsync(string owner, string name, string branch, CancellationToken ct) =>
        (await GetAsync<BranchDto>(
            owner,
            name,
            $"{Repo(owner, name)}/branches/{Uri.EscapeDataString(branch)}",
            ct,
            notFound: $"Branch '{branch}' was not found in {owner}/{name} (or the repository is empty).")).Commit.Sha;

    public async Task<GitTree> GetTreeAsync(string owner, string name, string treeSha, bool recursive, CancellationToken ct)
    {
        var path = $"{Repo(owner, name)}/git/trees/{Uri.EscapeDataString(treeSha)}{(recursive ? "?recursive=1" : string.Empty)}";
        var tree = await GetAsync<TreeDto>(owner, name, path, ct);
        return new GitTree(
            tree.Tree.Select(e => new GitTreeEntry(e.Path, ParseType(e.Type), e.Sha, e.Size)).ToList(),
            tree.Truncated);
    }

    public async Task<string> GetBlobTextAsync(string owner, string name, string blobSha, CancellationToken ct)
    {
        var blob = await GetAsync<BlobDto>(owner, name, $"{Repo(owner, name)}/git/blobs/{Uri.EscapeDataString(blobSha)}", ct);
        // GitHub wraps base64 content in newlines.
        return Encoding.UTF8.GetString(Convert.FromBase64String(blob.Content.Replace("\n", string.Empty, StringComparison.Ordinal)));
    }

    private static string Repo(string owner, string name) => $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";

    /// <param name="notFound">The 404 message for this call; defaults to the repository message.</param>
    private async Task<T> GetAsync<T>(string owner, string name, string path, CancellationToken ct, string? notFound = null)
    {
        using var response = await _http.GetAsync(path, ct);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new GitHubNotFoundException(notFound ?? $"Repository {owner}/{name} was not found or the token has no access to it.");
            case HttpStatusCode.Unauthorized:
                throw new GitHubAuthenticationException("GitHub rejected the credentials; check the configured token.");
            case HttpStatusCode.Forbidden:
                // Rate-limit 403s were already handled by GitHubRateLimitHandler; this is missing permission or SSO.
                throw new GitHubAuthenticationException($"GitHub denied access to {owner}/{name}; the token lacks permission or SSO authorization.");
            case HttpStatusCode.Conflict:
                throw new GitHubNotFoundException($"Repository {owner}/{name} is empty.");
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"GitHub returned an empty body for {path}.");
    }

    private static GitEntryType ParseType(string type) => type switch
    {
        "blob" => GitEntryType.Blob,
        "tree" => GitEntryType.Tree,
        "commit" => GitEntryType.Commit,
        _ => throw new InvalidOperationException($"Unknown Git tree entry type '{type}'."),
    };

    private sealed record RepoDto(string Name, OwnerDto Owner, string DefaultBranch, string HtmlUrl);

    private sealed record OwnerDto(string Login);

    private sealed record BranchDto(string Name, CommitDto Commit);

    private sealed record CommitDto(string Sha);

    private sealed record TreeDto(string Sha, bool Truncated, IReadOnlyList<TreeEntryDto> Tree);

    private sealed record TreeEntryDto(string Path, string Type, string Sha, long? Size);

    private sealed record BlobDto(string Sha, string Content);
}
