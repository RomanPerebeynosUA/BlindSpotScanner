using System.Text;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Infrastructure.GitHub;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.GitHub;

public sealed class GitHubClientTests : IDisposable
{
    private const string Token = "ghp_test-token-value";
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private GitHubClient CreateClient() =>
        new(new HttpClient(), Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = Token }));

    private void Respond(string path, int status, object? body = null) =>
        _server.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(body is null ? Response.Create().WithStatusCode(status) : Response.Create().WithStatusCode(status).WithBodyAsJson(body));

    [Fact]
    public async Task GetRepository_Exists_ReturnsDefaultBranch()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api", 200, new
        {
            name = "bookstore-api",
            owner = new { login = "example-org" },
            default_branch = "main",
            html_url = "https://github.com/example-org/bookstore-api",
        });

        // Act
        var repository = await CreateClient().GetRepositoryAsync("example-org", "bookstore-api", CancellationToken.None);

        // Assert
        repository.Should().Be(new GitHubRepository("example-org", "bookstore-api", "main", "https://github.com/example-org/bookstore-api"));
    }

    [Fact]
    public async Task GetRepository_NotFound_ThrowsNotFoundWithoutToken()
    {
        // Arrange
        Respond("/repos/example-org/missing", 404, new { message = "Not Found" });

        // Act
        var act = () => CreateClient().GetRepositoryAsync("example-org", "missing", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubNotFoundException>())
            .Which.Message.Should().Contain("example-org/missing").And.NotContain(Token);
    }

    [Fact]
    public async Task GetRepository_Unauthorized_ThrowsAuthentication()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api", 401, new { message = "Bad credentials" });

        // Act
        var act = () => CreateClient().GetRepositoryAsync("example-org", "bookstore-api", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubAuthenticationException>()).Which.Message.Should().NotContain(Token);
    }

    [Fact]
    public async Task GetBranchHeadSha_BranchNotFound_NamesTheBranch()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api/branches/*", 404, new { message = "Branch not found" });

        // Act
        var act = () => CreateClient().GetBranchHeadShaAsync("example-org", "bookstore-api", "mian", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubNotFoundException>())
            .Which.Message.Should().Be("Branch 'mian' was not found in example-org/bookstore-api (or the repository is empty).");
    }

    [Fact]
    public async Task GetRepository_Forbidden_ThrowsAccessDenied()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api", 403, new { message = "Resource protected by organization SAML enforcement." });

        // Act
        var act = () => CreateClient().GetRepositoryAsync("example-org", "bookstore-api", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubAuthenticationException>())
            .Which.Message.Should().Be("GitHub denied access to example-org/bookstore-api; the token lacks permission or SSO authorization.");
    }

    [Fact]
    public async Task GetTree_EmptyRepository_ThrowsNotFound()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api/git/trees/abc", 409, new { message = "Git Repository is empty." });

        // Act
        var act = () => CreateClient().GetTreeAsync("example-org", "bookstore-api", "abc", recursive: true, CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubNotFoundException>()).Which.Message.Should().Be("Repository example-org/bookstore-api is empty.");
    }

    [Fact]
    public async Task AnyRequest_WithToken_SendsBearerAndApiHeaders()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/repos/example-org/bookstore-api").UsingGet()
                .WithHeader("Authorization", $"Bearer {Token}")
                .WithHeader("Accept", "application/vnd.github+json")
                .WithHeader("X-GitHub-Api-Version", "2022-11-28")
                .WithHeader("User-Agent", "BlindSpotScanner"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                name = "bookstore-api",
                owner = new { login = "example-org" },
                default_branch = "main",
                html_url = "https://github.com/example-org/bookstore-api",
            }));

        // Act
        var act = () => CreateClient().GetRepositoryAsync("example-org", "bookstore-api", CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetBranchHeadSha_BranchWithSlash_IsEscaped()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api/branches/*", 200, new { name = "feature/x", commit = new { sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678" } });

        // Act
        var sha = await CreateClient().GetBranchHeadShaAsync("example-org", "bookstore-api", "feature/x", CancellationToken.None);

        // Assert (the slash travels escaped, so GitHub sees one branch segment, not two path segments)
        new { sha, Url = _server.LogEntries.Single().RequestMessage!.Url }.Should().BeEquivalentTo(new
        {
            sha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
            Url = $"{_server.Url}/repos/example-org/bookstore-api/branches/feature%2Fx",
        });
    }

    [Fact]
    public async Task GetTree_Recursive_ParsesEntriesAndTruncatedFlag()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/repos/example-org/bookstore-api/git/trees/abc").WithParam("recursive", "1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                sha = "abc",
                truncated = true,
                tree = new object[]
                {
                    new { path = "src", mode = "040000", type = "tree", sha = "t1" },
                    new { path = "src/Program.cs", mode = "100644", type = "blob", sha = "b1", size = 120 },
                    new { path = "libs/sub", mode = "160000", type = "commit", sha = "c1" },
                },
            }));

        // Act
        var tree = await CreateClient().GetTreeAsync("example-org", "bookstore-api", "abc", recursive: true, CancellationToken.None);

        // Assert
        tree.Should().BeEquivalentTo(new GitTree(
            [
                new GitTreeEntry("src", GitEntryType.Tree, "t1", null),
                new GitTreeEntry("src/Program.cs", GitEntryType.Blob, "b1", 120),
                new GitTreeEntry("libs/sub", GitEntryType.Commit, "c1", null),
            ],
            Truncated: true));
    }

    [Fact]
    public async Task GetBlobText_Base64Content_IsDecoded()
    {
        // Arrange
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("public class BooksController { } // → ok"));
        var wrapped = string.Join('\n', base64.Chunk(20).Select(chunk => new string(chunk))) + "\n";
        Respond("/repos/example-org/bookstore-api/git/blobs/b1", 200, new { sha = "b1", encoding = "base64", content = wrapped });

        // Act
        var text = await CreateClient().GetBlobTextAsync("example-org", "bookstore-api", "b1", CancellationToken.None);

        // Assert
        text.Should().Be("public class BooksController { } // → ok");
    }

    [Fact]
    public async Task GetBlobText_Utf8Bom_IsStripped()
    {
        // Arrange
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("namespace Api;")).ToArray();
        Respond("/repos/example-org/bookstore-api/git/blobs/b1", 200, new { sha = "b1", encoding = "base64", content = Convert.ToBase64String(bytes) });

        // Act
        var text = await CreateClient().GetBlobTextAsync("example-org", "bookstore-api", "b1", CancellationToken.None);

        // Assert
        text.Should().Be("namespace Api;");
    }

    [Fact]
    public async Task GetBlobText_NotUtf8_DecodesAsLatin1()
    {
        // Arrange ("// café" saved as Windows-1252: é is the single byte 0xE9)
        byte[] bytes = [.. Encoding.ASCII.GetBytes("// caf"), 0xE9];
        Respond("/repos/example-org/bookstore-api/git/blobs/b1", 200, new { sha = "b1", encoding = "base64", content = Convert.ToBase64String(bytes) });

        // Act
        var text = await CreateClient().GetBlobTextAsync("example-org", "bookstore-api", "b1", CancellationToken.None);

        // Assert
        text.Should().Be("// café");
    }

    [Fact]
    public async Task GetTree_SymlinkMode_ParsesAsSymlink()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api/git/trees/abc", 200, new
        {
            sha = "abc",
            truncated = false,
            tree = new object[] { new { path = "src/Link.cs", mode = "120000", type = "blob", sha = "s1", size = 12 } },
        });

        // Act
        var tree = await CreateClient().GetTreeAsync("example-org", "bookstore-api", "abc", recursive: false, CancellationToken.None);

        // Assert
        tree.Entries.Single().Type.Should().Be(GitEntryType.Symlink);
    }

    [Fact]
    public async Task AnyRequest_InjectedHttpClient_IsNotMutated()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api", 200, new
        {
            name = "bookstore-api",
            owner = new { login = "example-org" },
            default_branch = "main",
            html_url = "https://github.com/example-org/bookstore-api",
        });
        var http = new HttpClient();

        // Act
        await new GitHubClient(http, Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = Token }))
            .GetRepositoryAsync("example-org", "bookstore-api", CancellationToken.None);

        // Assert
        new { http.BaseAddress, HeaderCount = http.DefaultRequestHeaders.Count() }.Should().BeEquivalentTo(new { BaseAddress = (Uri?)null, HeaderCount = 0 });
    }

    private const string CommitSha = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    [Fact]
    public async Task GetFileText_Exists_ReturnsRawText()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/repos/example-org/bookstore-api/contents/tests/BooksApiTests.cs").WithParam("ref", CommitSha).UsingGet()
                .WithHeader("Accept", "application/vnd.github.raw+json"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("public class BooksApiTests { }"));

        // Act
        var text = await CreateClient().GetFileTextAsync("example-org", "bookstore-api", "tests/BooksApiTests.cs", CommitSha, CancellationToken.None);

        // Assert
        text.Should().Be("public class BooksApiTests { }");
    }

    [Fact]
    public async Task GetFileText_NotFound_ReturnsNull()
    {
        // Arrange
        Respond("/repos/example-org/bookstore-api/contents/tests/Gone.cs", 404, new { message = "Not Found" });

        // Act
        var text = await CreateClient().GetFileTextAsync("example-org", "bookstore-api", "tests/Gone.cs", CommitSha, CancellationToken.None);

        // Assert
        text.Should().BeNull();
    }

    [Fact]
    public async Task GetFileText_PathWithSpaces_EscapesSegments()
    {
        // Arrange (WireMock decodes %20 before logging, so the outgoing URI is recorded directly)
        var recorder = new RecordingHandler();
        var client = new GitHubClient(new HttpClient(recorder), Options.Create(new GitHubOptions { ApiBaseUrl = "https://api.github.test", Token = Token }));

        // Act
        await client.GetFileTextAsync("example-org", "bookstore-api", "tests/My Tests/A.cs", CommitSha, CancellationToken.None);

        // Assert
        recorder.RequestUri!.AbsoluteUri.Should().Be($"https://api.github.test/repos/example-org/bookstore-api/contents/tests/My%20Tests/A.cs?ref={CommitSha}");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("x") });
        }
    }
}
