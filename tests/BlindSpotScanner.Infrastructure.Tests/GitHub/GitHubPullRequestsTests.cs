using System.Text;
using System.Text.Json.Nodes;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Infrastructure.GitHub;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.GitHub;

public sealed class GitHubPullRequestsTests : IDisposable
{
    private const string Token = "ghp_test-token-value";
    private const string Repo = "/repos/example-org/bookstore-api";
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private IGitHubPullRequests Client() =>
        new GitHubClient(new HttpClient(), Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = Token }));

    private void Post(string path, int status, object body) =>
        _server.Given(Request.Create().WithPath(Repo + path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));

    private void Get(string path, object body) =>
        _server.Given(Request.Create().WithPath(Repo + path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(body));

    private JsonNode Sent() => JsonNode.Parse(_server.LogEntries.Single().RequestMessage!.Body!)!;

    private static bool Same(JsonNode actual, string expected) => JsonNode.DeepEquals(actual, JsonNode.Parse(expected));

    [Fact]
    public async Task CreateBlobAsync_SendsBase64Content()
    {
        // Arrange
        Post("/git/blobs", 201, new { sha = "blob1" });

        // Act
        var sha = await Client().CreateBlobAsync("example-org", "bookstore-api", "class A {}", CancellationToken.None);

        // Assert
        var expected = $$"""{"content":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes("class A {}"))}}","encoding":"base64"}""";
        new { sha, Body = Same(Sent(), expected) }.Should().Be(new { sha = "blob1", Body = true });
    }

    [Fact]
    public async Task CreateTreeAsync_SendsBaseTreeAndBlobEntries()
    {
        // Arrange
        Post("/git/trees", 201, new { sha = "tree2" });

        // Act
        var sha = await Client().CreateTreeAsync("example-org", "bookstore-api", "tree1", [("tests/A.cs", "blob1")], CancellationToken.None);

        // Assert
        var expected = """{"base_tree":"tree1","tree":[{"path":"tests/A.cs","mode":"100644","type":"blob","sha":"blob1"}]}""";
        new { sha, Body = Same(Sent(), expected) }.Should().Be(new { sha = "tree2", Body = true });
    }

    [Fact]
    public async Task CreateCommitAsync_SendsParent()
    {
        // Arrange
        Post("/git/commits", 201, new { sha = "commit2" });

        // Act
        var sha = await Client().CreateCommitAsync("example-org", "bookstore-api", "test: add", "tree2", "head1", CancellationToken.None);

        // Assert
        new { sha, Body = Same(Sent(), """{"message":"test: add","tree":"tree2","parents":["head1"]}""") }.Should().Be(new { sha = "commit2", Body = true });
    }

    [Fact]
    public async Task CreateBranchAsync_SendsRefsHeads()
    {
        // Arrange
        Post("/git/refs", 201, new { @ref = "refs/heads/coverage/fix-x" });

        // Act
        await Client().CreateBranchAsync("example-org", "bookstore-api", "coverage/fix-x", "commit2", CancellationToken.None);

        // Assert
        Same(Sent(), """{"ref":"refs/heads/coverage/fix-x","sha":"commit2"}""").Should().BeTrue();
    }

    [Fact]
    public async Task CreatePullRequestAsync_RefExists_ThrowsBranchExists()
    {
        // Arrange
        Post("/git/refs", 422, new { message = "Reference already exists" });

        // Act
        var act = () => Client().CreateBranchAsync("example-org", "bookstore-api", "coverage/fix-x", "commit2", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubBranchExistsException>()).Which.Message.Should().Be("Branch coverage/fix-x already exists; rename it.");
    }

    [Fact]
    public async Task CreateBranchAsync_InvalidRef_ThrowsHttpRequestNotBranchExists()
    {
        // Arrange (GitHub also answers 422 for a malformed ref name)
        Post("/git/refs", 422, new { message = "Reference update failed" });

        // Act
        var act = () => Client().CreateBranchAsync("example-org", "bookstore-api", "coverage/fix-x", "commit2", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("Reference update failed");
    }

    [Fact]
    public async Task CreateDraftPullRequestAsync_SendsDraftTrue_ReturnsNumberAndUrl()
    {
        // Arrange
        Post("/pulls", 201, new { number = 7, html_url = "https://github.com/example-org/bookstore-api/pull/7" });

        // Act
        var pr = await Client().CreateDraftPullRequestAsync("example-org", "bookstore-api", "test: add", "Body", "coverage/fix-x", "main", CancellationToken.None);

        // Assert
        var expected = """{"title":"test: add","body":"Body","head":"coverage/fix-x","base":"main","draft":true}""";
        new { pr, Body = Same(Sent(), expected) }.Should().Be(new { pr = new OpenedPullRequest(7, "https://github.com/example-org/bookstore-api/pull/7"), Body = true });
    }

    [Fact]
    public async Task AddLabelsAsync_SendsLabels()
    {
        // Arrange
        Post("/issues/7/labels", 200, Array.Empty<object>());

        // Act
        await Client().AddLabelsAsync("example-org", "bookstore-api", 7, ["integration-coverage"], CancellationToken.None);

        // Assert
        Same(Sent(), """{"labels":["integration-coverage"]}""").Should().BeTrue();
    }

    [Fact]
    public async Task GetPullRequestStatusAsync_MergedPr_ReturnsMerged()
    {
        // Arrange
        Get("/pulls/7", new { state = "closed", merged = true, head = new { sha = "c1" } });

        // Act
        var status = await Client().GetPullRequestStatusAsync("example-org", "bookstore-api", 7, CancellationToken.None);

        // Assert
        status.Should().Be("Merged");
    }

    [Fact]
    public async Task GetPullRequestStatusAsync_OpenWithFailingCheck_ReturnsChecksFailing()
    {
        // Arrange
        Get("/pulls/7", new { state = "open", merged = false, head = new { sha = "c1" } });
        Get("/commits/c1/check-runs", new
        {
            check_runs = new[]
            {
                new { status = "completed", conclusion = (string?)"success" },
                new { status = "completed", conclusion = (string?)"failure" },
            },
        });

        // Act
        var status = await Client().GetPullRequestStatusAsync("example-org", "bookstore-api", 7, CancellationToken.None);

        // Assert
        status.Should().Be("Open · checks failing");
    }

    [Fact]
    public async Task CreateBlobAsync_Unauthorized_ThrowsWithoutToken()
    {
        // Arrange
        Post("/git/blobs", 401, new { message = "Bad credentials" });

        // Act
        var act = () => Client().CreateBlobAsync("example-org", "bookstore-api", "x", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<GitHubAuthenticationException>()).Which.Message.Should().NotContain(Token);
    }
}
