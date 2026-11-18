using BlindSpotScanner.Infrastructure.GitHub;
using BlindSpotScanner.Infrastructure.Jira;
using BlindSpotScanner.Infrastructure.Llm;
using BlindSpotScanner.Infrastructure.Settings;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.Settings;

/// <summary>The settings page's "Test" buttons (D26): one cheap read per connection; the result never carries a secret.</summary>
public sealed class ConnectionTesterTests : IDisposable
{
    private const string JiraToken = "jira-secret-token-1234";
    private const string ClaudeKey = "sk-ant-test-key-value-abcdef";
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private ConnectionTester Tester() => new(
        new HttpClient(),
        Options.Create(new GitHubOptions { ApiBaseUrl = _server.Url!, Token = "ghp_test-token" }),
        Options.Create(new JiraOptions { BaseUrl = _server.Url!, Email = "dev@example.com", ApiToken = JiraToken, ProjectKey = "BSS" }),
        Options.Create(new LlmOptions { BaseUrl = _server.Url!, ApiKey = ClaudeKey }));

    private void Respond(string path, int status, object body) =>
        _server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body));

    [Fact]
    public async Task TestJira_ValidToken_ReturnsOk()
    {
        // Arrange
        Respond("/rest/api/3/myself", 200, new { displayName = "Dev Example", emailAddress = "dev@example.com" });

        // Act
        var result = await Tester().TestAsync("jira", CancellationToken.None);

        // Assert
        result.Should().Be(new Application.Settings.ConnectionTestResult(true, "Connected to Jira as Dev Example."));
    }

    [Fact]
    public async Task TestJira_Unauthorized_ReturnsNotOkWithoutToken()
    {
        // Arrange
        Respond("/rest/api/3/myself", 401, new { errorMessages = new[] { "Unauthorized" } });

        // Act
        var result = await Tester().TestAsync("jira", CancellationToken.None);

        // Assert
        new { result!.Ok, Leaks = result.Message.Contains(JiraToken, StringComparison.Ordinal) }.Should().Be(new { Ok = false, Leaks = false });
    }

    [Fact]
    public async Task TestGitHub_ReturnsOk()
    {
        // Arrange
        Respond("/user", 200, new { login = "octocat" });

        // Act
        var result = await Tester().TestAsync("github", CancellationToken.None);

        // Assert
        result.Should().Be(new Application.Settings.ConnectionTestResult(true, "Connected to GitHub as octocat."));
    }

    [Fact]
    public async Task TestLlm_ReturnsOk()
    {
        // Arrange (listing models is free; nothing is generated)
        _server.Given(Request.Create().WithPath("/v1/models").WithHeader("x-api-key", ClaudeKey).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { data = new[] { new { id = "claude-sonnet-5-5" } } }));

        // Act
        var result = await Tester().TestAsync("llm", CancellationToken.None);

        // Assert
        result.Should().Be(new Application.Settings.ConnectionTestResult(true, "Connected to Claude."));
    }

    [Fact]
    public async Task Test_UnknownKind_ReturnsNull()
    {
        // Act
        var result = await Tester().TestAsync("slack", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }
}
