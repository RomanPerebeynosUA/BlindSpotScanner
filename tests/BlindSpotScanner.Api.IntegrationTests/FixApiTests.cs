using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>M10 acceptance (doc 11): branch → blobs → tree → commit → ref → PR (draft), the guard rejects a production-code edit, and read-only mode disables PRs.</summary>
public class FixApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    private async Task<(Guid RunId, string RepoName)> SeedAsync()
    {
        var repositoryId = await factory.SeedRepositoryAsync();
        var (runId, repoName) = (Guid.NewGuid(), $"fix-{Guid.NewGuid():N}");
        await factory.SeedSucceededRunAsync(repositoryId, "sample-report.json", runId, null, repoName);
        return (runId, repoName);
    }

    private static async Task<JsonObject> PreviewAsync(HttpClient client, Guid runId, string findingId = "f2")
    {
        var response = await client.PostAsync($"/api/runs/{runId}/findings/{findingId}/fix-preview", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid runId, JsonObject draft, JsonArray? files = null) =>
        client.PostAsJsonAsync($"/api/runs/{runId}/findings/f2/pull-request", new JsonObject
        {
            ["files"] = files ?? new JsonArray([.. draft["files"]!.AsArray().Select(f => (JsonNode)new JsonObject
            {
                ["path"] = f!["path"]!.GetValue<string>(),
                ["action"] = f["action"]!.GetValue<string>(),
                ["content"] = f["content"]!.GetValue<string>(),
            })]),
            ["branch"] = draft["branch"]!.GetValue<string>(),
            ["title"] = draft["title"]!.GetValue<string>(),
            ["body"] = draft["body"]!.GetValue<string>(),
            ["force"] = false,
        });

    [Fact]
    public async Task PullRequest_Finding_CallsGitHubInOrderWithDraft()
    {
        // Arrange
        var (runId, repoName) = await SeedAsync();
        using var client = factory.CreateClient();
        var draft = await PreviewAsync(client, runId);

        // Act
        var response = await CreateAsync(client, runId, draft);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var calls = factory.GitHub.CallsFor(repoName);
        calls.SkipWhile(c => c != "GET branches/main").Skip(1).Should().Equal(
            "POST git/blobs", "POST git/trees", "POST git/commits", "POST git/refs", "POST pulls", "POST issues/7/labels");
        calls.Should().Contain("GET branches/main");
        factory.GitHub.PullBodyFor(repoName)["draft"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task PullRequest_ProductionEdit_Returns422()
    {
        // Arrange
        var (runId, repoName) = await SeedAsync();
        using var client = factory.CreateClient();
        var draft = await PreviewAsync(client, runId);
        var edit = new JsonArray(new JsonObject { ["path"] = "src/Bookstore.Api/Controllers/OrdersController.cs", ["action"] = "modify", ["content"] = "public class X { }" });

        // Act
        var response = await CreateAsync(client, runId, draft, edit);

        // Assert
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        new { response.StatusCode, Reason = problem.GetProperty("reasons")[0].GetString()!.Contains("outside the test project", StringComparison.Ordinal), Writes = factory.GitHub.CallsFor(repoName).Count(c => c.StartsWith("POST", StringComparison.Ordinal)) }
            .Should().Be(new { StatusCode = HttpStatusCode.UnprocessableEntity, Reason = true, Writes = 0 });
    }

    [Fact]
    public async Task Preview_UncoveredSeam_Returns422()
    {
        // Arrange
        var (runId, _) = await SeedAsync();
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync($"/api/runs/{runId}/findings/f4/fix-preview", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Features_Default_PullRequestsTrue()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var features = await client.GetFromJsonAsync<JsonElement>("/api/features");

        // Assert
        new { PullRequests = features.GetProperty("pullRequests").GetBoolean(), Jira = features.GetProperty("jira").GetBoolean() }
            .Should().Be(new { PullRequests = true, Jira = true });
    }
}

public class ReadOnlyFixApiTests(ReadOnlyApiFactory factory) : IClassFixture<ReadOnlyApiFactory>
{
    [Fact]
    public async Task Preview_ReadOnly_Returns403()
    {
        // Arrange
        var repositoryId = await factory.SeedRepositoryAsync();
        var runId = Guid.NewGuid();
        await factory.SeedSucceededRunAsync(repositoryId, "sample-report.json", runId, null);
        using var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync($"/api/runs/{runId}/findings/f2/fix-preview", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Features_ReadOnly_PullRequestsFalse()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        var features = await client.GetFromJsonAsync<JsonElement>("/api/features");

        // Assert
        features.GetProperty("pullRequests").GetBoolean().Should().BeFalse();
    }
}
