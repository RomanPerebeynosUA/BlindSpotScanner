using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>M9 acceptance (doc 11): WireMock Jira receives the exact ADF payload, and dedup prevents a second ticket.</summary>
public class JiraApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    private async Task<(Guid RepositoryId, Guid RunId)> SeedAsync(Guid? runId = null)
    {
        var repositoryId = await factory.SeedRepositoryAsync();
        var id = runId ?? Guid.NewGuid();
        await factory.SeedSucceededRunAsync(repositoryId, "sample-report.json", id, null);
        return (repositoryId, id);
    }

    private static async Task<JsonObject> PreviewAsync(HttpClient client, Guid runId, string findingId = "f2")
    {
        var response = await client.PostAsync($"/api/runs/{runId}/findings/{findingId}/jira-preview", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    /// <summary>The draft as the create body, unchanged (what "Create ticket" sends when nothing is edited).</summary>
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid runId, JsonObject draft, bool force = false) =>
        client.PostAsJsonAsync($"/api/runs/{runId}/findings/f2/jira", new
        {
            summary = draft["summary"]!.GetValue<string>(),
            description = draft["description"]!.GetValue<string>(),
            projectKey = draft["projectKey"]!.GetValue<string>(),
            issueType = draft["issueType"]!.GetValue<string>(),
            priority = draft["priority"]!.GetValue<string>(),
            labels = draft["labels"]!.AsArray().Select(l => l!.GetValue<string>()).ToArray(),
            force,
        });

    [Fact]
    public async Task CreateJira_Finding_SendsExactAdfPayload()
    {
        // Arrange
        var runId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        await SeedAsync(runId);
        using var client = factory.CreateClient();
        var draft = await PreviewAsync(client, runId);

        // Act
        var response = await CreateAsync(client, runId, draft);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jira-create-f2.json")));
        var sent = factory.Jira.CreatedFor(runId.ToString()).Single();
        JsonNode.DeepEquals(sent, expected).Should().BeTrue(sent.ToJsonString());
    }

    [Fact]
    public async Task CreateJira_Twice_SecondReturns409AndNoSecondTicket()
    {
        // Arrange
        var (_, runId) = await SeedAsync();
        using var client = factory.CreateClient();
        var draft = await PreviewAsync(client, runId);
        (await CreateAsync(client, runId, draft)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var second = await CreateAsync(client, runId, draft);

        // Assert
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        new { second.StatusCode, Title = problem.GetProperty("title").GetString(), Tickets = factory.Jira.CreatedFor(runId.ToString()).Count }
            .Should().Be(new { StatusCode = HttpStatusCode.Conflict, Title = "Ticket already exists", Tickets = 1 });
    }

    [Fact]
    public async Task Preview_ExistingTicket_IncludesExisting()
    {
        // Arrange
        var (_, runId) = await SeedAsync();
        using var client = factory.CreateClient();
        var created = await (await CreateAsync(client, runId, await PreviewAsync(client, runId))).Content.ReadFromJsonAsync<JsonObject>();

        // Act
        var draft = await PreviewAsync(client, runId);

        // Assert
        draft["existing"]!["externalKey"]!.GetValue<string>().Should().Be(created!["externalKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task Preview_UnknownFinding_Returns404()
    {
        // Arrange
        var (_, runId) = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync($"/api/runs/{runId}/findings/f99/jira-preview", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Actions_AfterCreate_ListsTicketWithStatus()
    {
        // Arrange
        var (repositoryId, runId) = await SeedAsync();
        using var client = factory.CreateClient();
        await CreateAsync(client, runId, await PreviewAsync(client, runId));

        // Act
        var actions = await client.GetFromJsonAsync<JsonElement>($"/api/repositories/{repositoryId}/actions");

        // Assert
        var action = actions.EnumerateArray().Single();
        new
        {
            Type = action.GetProperty("type").GetString(),
            StableKey = action.GetProperty("stableKey").GetString(),
            Status = action.GetProperty("status").GetString(),
            Url = action.GetProperty("url").GetString()!.StartsWith(factory.Jira.Url + "/browse/BSS-", StringComparison.Ordinal),
        }.Should().Be(new { Type = "JiraTicket", StableKey = "uncovered-endpoint:POST api/orders/{}/cancel", Status = "To Do", Url = true });
    }
}

public class JiraNotConfiguredApiTests(NoJiraApiFactory factory) : IClassFixture<NoJiraApiFactory>
{
    [Fact]
    public async Task Preview_JiraNotConfigured_Returns503()
    {
        // Arrange
        var repositoryId = await factory.SeedRepositoryAsync();
        var runId = Guid.NewGuid();
        await factory.SeedSucceededRunAsync(repositoryId, "sample-report.json", runId, null);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync($"/api/runs/{runId}/findings/f2/jira-preview", null);

        // Assert
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        new { response.StatusCode, Detail = problem.GetProperty("detail").GetString() }.Should().Be(new
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            Detail = "Set Jira:BaseUrl, Jira:Email, Jira:ApiToken and Jira:ProjectKey.",
        });
    }
}
