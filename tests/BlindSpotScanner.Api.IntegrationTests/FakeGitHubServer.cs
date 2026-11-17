using System.Security.Cryptography;
using System.Text;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// GitHub's REST API for one repository, <c>example-org/bookstore</c> at one commit, served by WireMock
/// from the files in <c>tests/fixtures/bookstore</c> (CLAUDE.md: WireMock for GitHub in integration tests).
/// </summary>
public sealed class FakeGitHubServer : IDisposable
{
    public const string Owner = "example-org";
    public const string Name = "bookstore";
    public const string CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";
    public const string HtmlUrl = "https://github.com/example-org/bookstore";

    private const string Repo = $"/repos/{Owner}/{Name}";

    private readonly WireMockServer _server = WireMockServer.Start();

    public FakeGitHubServer()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "bookstore");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Path: Path.GetRelativePath(root, f).Replace('\\', '/'), Bytes: File.ReadAllBytes(f)))
            .ToList();

        Json(Repo, new { name = Name, owner = new { login = Owner }, default_branch = "main", html_url = HtmlUrl });
        Json($"{Repo}/branches/main", new { name = "main", commit = new { sha = CommitSha } });
        Json($"{Repo}/git/trees/{CommitSha}", new
        {
            sha = CommitSha,
            truncated = false,
            tree = files.Select(f => new { path = f.Path, mode = "100644", type = "blob", sha = BlobSha(f.Bytes), size = f.Bytes.Length }),
        });

        foreach (var (path, bytes) in files)
        {
            Json($"{Repo}/git/blobs/{BlobSha(bytes)}", new { sha = BlobSha(bytes), encoding = "base64", content = Convert.ToBase64String(bytes) });
            _server.Given(Request.Create().WithPath($"{Repo}/contents/{path}").WithParam("ref", CommitSha).UsingGet())
                .RespondWith(Response.Create().WithStatusCode(200).WithBody(Encoding.UTF8.GetString(bytes)));
        }
    }

    public string Url => _server.Url!;

    public void Dispose() => _server.Stop();

    private void Json(string path, object body) =>
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(body));

    private static string BlobSha(byte[] bytes) => Convert.ToHexStringLower(SHA1.HashData(bytes));
}
