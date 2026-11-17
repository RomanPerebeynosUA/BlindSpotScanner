using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>
/// Jira Cloud REST v3 served by WireMock (CLAUDE.md: WireMock for Jira): create answers with
/// increasing keys, JQL search finds nothing, and every issue's status is "To Do".
/// </summary>
public sealed class FakeJiraServer : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private int _next;

    public FakeJiraServer()
    {
        _server.Given(Request.Create().WithPath("/rest/api/3/issue").UsingPost())
            .RespondWith(Response.Create().WithCallback(_ =>
            {
                var key = $"BSS-{Interlocked.Increment(ref _next)}";
                return new WireMock.ResponseMessage
                {
                    StatusCode = 201,
                    Headers = new Dictionary<string, WireMock.Types.WireMockList<string>> { ["Content-Type"] = new("application/json") },
                    BodyData = new WireMock.Util.BodyData
                    {
                        BodyAsString = $$"""{"id":"1{{_next}}","key":"{{key}}","self":"{{Url}}/rest/api/3/issue/1{{_next}}"}""",
                        DetectedBodyType = WireMock.Types.BodyType.String,
                    },
                };
            }));
        _server.Given(Request.Create().WithPath("/rest/api/3/search/jql").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { issues = Array.Empty<object>() }));
        _server.Given(Request.Create().WithPath(new WireMock.Matchers.RegexMatcher("^/rest/api/3/issue/[^/]+$")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { fields = new { status = new { name = "To Do" } } }));
    }

    public string Url => _server.Url!;

    /// <summary>The create bodies Jira received whose text mentions <paramref name="marker"/> (the run id is in the report link).</summary>
    public IReadOnlyList<JsonNode> CreatedFor(string marker) =>
        [.. _server.LogEntries
            .Where(e => e.RequestMessage!.Path == "/rest/api/3/issue" && e.RequestMessage.Method == "POST" && e.RequestMessage.Body!.Contains(marker, StringComparison.Ordinal))
            .Select(e => JsonNode.Parse(e.RequestMessage!.Body!)!)];

    public void Dispose() => _server.Stop();
}
