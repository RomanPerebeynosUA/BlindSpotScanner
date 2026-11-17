using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// GitHub's REST API for the bookstore fixture at one commit, served by WireMock from the files in
/// <c>tests/fixtures/bookstore</c> (CLAUDE.md: WireMock for GitHub in integration tests). Every
/// repository name under <c>example-org</c> serves the same files, so tests can register distinct
/// repositories (names are unique, D22); other owners are 404.
/// </summary>
public sealed class FakeGitHubServer : IDisposable
{
    public const string Owner = "example-org";
    public const string Name = "bookstore";
    public const string CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    public const string HtmlUrl = "https://github.com/example-org/bookstore";

    /// <summary>A regex for any repository of <see cref="Owner"/>; the name is captured for the repository endpoint.</summary>
    private const string Repo = $"/repos/{Owner}/(?<name>[^/]+)";

    private readonly WireMockServer _server = WireMockServer.Start();

    public FakeGitHubServer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "bookstore");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Bytes: File.ReadAllBytes(f)))
            .ToList();

        _server.Given(Request.Create().WithPath(new RegexMatcher($"^{Repo}$")).UsingGet())
            .RespondWith(Response.Create().WithCallback(request =>
            {
                var name = Regex.Match(request.Path, $"^{Repo}$").Groups["name"].Value;
                return new WireMock.ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new WireMock.Util.BodyData
                    {
                        BodyAsJson = new { name, owner = new { login = Owner }, default_branch = "main", html_url = $"https://github.com/{Owner}/{name}" },
                        DetectedBodyType = WireMock.Types.BodyType.Json,
                    },
                };
            }));
        Json($"{Repo}/branches/main", new { name = "main", commit = new { sha = CommitSha } });
        Json($"{Repo}/git/trees/{CommitSha}", new
        {
            sha = CommitSha,
            truncated = false,
            tree = files.Select(f => new { path = f.Path, mode = "100644", type = "blob", sha = BlobSha(f.Bytes), size = f.Bytes.Length }),
        });

        // Fix-PR writes (D25): the Git Data API, the draft PR and its label.
        Json($"{Repo}/git/commits/{CommitSha}", new { sha = CommitSha, tree = new { sha = "base-tree" } });
        Write("git/blobs", 201, new { sha = "new-blob" });
        Write("git/trees", 201, new { sha = "new-tree" });
        Write("git/commits", 201, new { sha = "new-commit" });
        Write("git/refs", 201, new { @ref = "refs/heads/coverage/fix" });
        Write("pulls", 201, new { number = 7, html_url = $"https://github.com/{Owner}/{Name}/pull/7" });
        Write("issues/7/labels", 200, Array.Empty<object>());

        // Repositories whose name says how a write should fail (tests pick the name).
        Fail("branch-exists", "git/refs", 422, new { message = "Reference already exists" });
        Fail("pull-fails", "pulls", 500, new { message = "Server Error" });

        foreach (var (path, bytes) in files)
        {
            Json($"{Repo}/git/blobs/{BlobSha(bytes)}", new { sha = BlobSha(bytes), encoding = "base64", content = Convert.ToBase64String(bytes) });
            _server.Given(Request.Create().WithPath(new RegexMatcher($"^{Repo}/contents/{Regex.Escape(path)}$")).WithParam("ref", CommitSha).UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody(Encoding.UTF8.GetString(bytes)));
        }
    }

    public string Url => _server.Url!;

    /// <summary>The fix-PR requests GitHub received for one repository, in order: "METHOD path-after-repo" (D25).</summary>
    public IReadOnlyList<string> CallsFor(string name) =>
        [.. _server.LogEntries
            .Select(e => e.RequestMessage!)
            .Where(m => m.Path.StartsWith($"/repos/{Owner}/{name}/", StringComparison.Ordinal) && (m.Method == "POST" || m.Path.EndsWith("/branches/main", StringComparison.Ordinal)))
            .OrderBy(m => m.DateTime)
            .Select(m => $"{m.Method} {m.Path[$"/repos/{Owner}/{name}/".Length..]}")];

    /// <summary>The body of the pulls request for one repository.</summary>
    public System.Text.Json.Nodes.JsonNode PullBodyFor(string name) =>
        System.Text.Json.Nodes.JsonNode.Parse(_server.LogEntries.Select(e => e.RequestMessage!).Single(m => m.Method == "POST" && m.Path == $"/repos/{Owner}/{name}/pulls").Body!)!;

    public void Dispose() => _server.Stop();

    /// <param name="path">Starts with <see cref="Repo"/>; the rest is matched literally.</param>
    private void Json(string path, object body) =>
        _server.Given(Request.Create().WithPath(new RegexMatcher($"^{Repo}{Regex.Escape(path[Repo.Length..])}$")).UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(body));

    private void Fail(string nameMarker, string path, int status, object body) =>
        _server.Given(Request.Create().WithPath(new RegexMatcher($"^/repos/{Owner}/[^/]*{Regex.Escape(nameMarker)}[^/]*/{Regex.Escape(path)}$")).UsingPost())
            .AtPriority(1)
            .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));

    private void Write(string path, int status, object body) =>
        _server.Given(Request.Create().WithPath(new RegexMatcher($"^{Repo}/{Regex.Escape(path)}$")).UsingPost())
            .AtPriority(10) // the named failures (priority 1) win for their repositories
            .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));

    private static string BlobSha(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));
}
