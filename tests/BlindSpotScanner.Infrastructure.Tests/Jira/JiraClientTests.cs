using System.Text;
using System.Text.Json.Nodes;
using BlindSpotScanner.Application.Remediation;
using BlindSpotScanner.Infrastructure.Jira;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.Jira;

public sealed class JiraClientTests : IDisposable
{
    private const string Token = "jira-test-token-value";
    private const string Email = "dev@example.com";
    private const string StableKey = "uncovered-endpoint:POST api/orders/{}/cancel";
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private JiraClient CreateClient() => new(new HttpClient(), Options.Create(new JiraOptions
    {
        BaseUrl = _server.Url!,
        Email = Email,
        ApiToken = Token,
        ProjectKey = "BSS",
    }));

    private static JiraIssueRequest Draft(string description = "## Problem\nNo test") =>
        new("BSS", "Task", "[Integration coverage] Add test for POST api/orders/{id}/cancel", description, "High", ["integration-coverage"], StableKey);

    private void RespondCreated() =>
        _server.Given(Request.Create().WithPath("/rest/api/3/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { id = "10000", key = "BSS-1", self = $"{_server.Url}/rest/api/3/issue/10000" }));

    private void RespondSearch(object body) =>
        _server.Given(Request.Create().WithPath("/rest/api/3/search/jql").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(body));

    private static object Issue(string key, string footerKey) => new
    {
        key,
        fields = new
        {
            status = new { name = "In Progress" },
            description = new
            {
                type = "doc",
                version = 1,
                content = new object[] { new { type = "paragraph", content = new object[] { new { type = "text", text = $"BlindSpotScanner key: {footerKey}" } } } },
            },
        },
    };

    private JsonNode SentBody() => JsonNode.Parse(_server.LogEntries.Single().RequestMessage!.Body!)!;

    [Fact]
    public async Task CreateIssueAsync_Draft_SendsExactPayload()
    {
        // Arrange
        RespondCreated();

        // Act
        await CreateClient().CreateIssueAsync(Draft(), CancellationToken.None);

        // Assert
        var expected = JsonNode.Parse("""
            {"fields":{
              "project":{"key":"BSS"},
              "issuetype":{"name":"Task"},
              "summary":"[Integration coverage] Add test for POST api/orders/{id}/cancel",
              "description":{"type":"doc","version":1,"content":[
                {"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Problem"}]},
                {"type":"paragraph","content":[{"type":"text","text":"No test"}]},
                {"type":"paragraph","content":[{"type":"text","text":"BlindSpotScanner key: uncovered-endpoint:POST api/orders/{}/cancel"}]}]},
              "priority":{"name":"High"},
              "labels":["integration-coverage"]}}
            """);
        JsonNode.DeepEquals(SentBody(), expected).Should().BeTrue(SentBody().ToJsonString());
    }

    [Fact]
    public async Task CreateIssueAsync_SendsBasicAuth()
    {
        // Arrange
        RespondCreated();

        // Act
        await CreateClient().CreateIssueAsync(Draft(), CancellationToken.None);

        // Assert
        _server.LogEntries.Single().RequestMessage!.Headers!["Authorization"].Single()
            .Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Email}:{Token}")));
    }

    [Fact]
    public async Task CreateIssueAsync_Created_ReturnsBrowseUrl()
    {
        // Arrange
        RespondCreated();

        // Act
        var issue = await CreateClient().CreateIssueAsync(Draft(), CancellationToken.None);

        // Assert
        issue.Should().Be(new JiraIssueRef("BSS-1", $"{_server.Url}/browse/BSS-1", "To Do"));
    }

    [Fact]
    public async Task CreateIssueAsync_Unauthorized_ThrowsWithoutToken()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/rest/api/3/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(401).WithBodyAsJson(new { errorMessages = new[] { "Client must be authenticated." } }));

        // Act
        var act = () => CreateClient().CreateIssueAsync(Draft(), CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<JiraException>())
            .Which.Message.Should().Contain("401").And.Contain("Client must be authenticated.").And.NotContain(Token).And.NotContain("Basic");
    }

    [Fact]
    public async Task FindByStableKeyAsync_EscapesSpecialCharacters()
    {
        // Arrange
        RespondSearch(new { issues = Array.Empty<object>() });

        // Act
        await CreateClient().FindByStableKeyAsync(StableKey, CancellationToken.None);

        // Assert
        SentBody()["jql"]!.GetValue<string>().Should().Be("labels = integration-coverage AND text ~ \"uncovered endpoint POST api orders cancel\"");
    }

    [Fact]
    public async Task FindByStableKeyAsync_ExactFooter_ReturnsIssue()
    {
        // Arrange
        RespondSearch(new { issues = new[] { Issue("BSS-7", StableKey) } });

        // Act
        var issue = await CreateClient().FindByStableKeyAsync(StableKey, CancellationToken.None);

        // Assert
        issue.Should().Be(new JiraIssueRef("BSS-7", $"{_server.Url}/browse/BSS-7", "In Progress"));
    }

    [Fact]
    public async Task FindByStableKeyAsync_FuzzyHitWithOtherKey_ReturnsNull()
    {
        // Arrange (text search is fuzzy: the plain order endpoint matches the cancel endpoint's words)
        RespondSearch(new { issues = new[] { Issue("BSS-8", "uncovered-endpoint:POST api/orders") } });

        // Act
        var issue = await CreateClient().FindByStableKeyAsync(StableKey, CancellationToken.None);

        // Assert
        issue.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsStatusName()
    {
        // Arrange
        _server.Given(Request.Create().WithPath("/rest/api/3/issue/BSS-1").WithParam("fields", "status").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { key = "BSS-1", fields = new { status = new { name = "Done" } } }));

        // Act
        var status = await CreateClient().GetStatusAsync("BSS-1", CancellationToken.None);

        // Assert
        status.Should().Be("Done");
    }
}
