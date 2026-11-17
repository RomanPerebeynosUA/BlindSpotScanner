using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

public class RepositoriesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string NewUrl() => $"https://github.com/{FakeGitHubServer.Owner}/bookstore-{Guid.NewGuid():N}";

    private static async Task<Guid> AddAsync(HttpClient client, string url)
    {
        using var response = await client.PostAsJsonAsync("/api/repositories", new { url });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Post_ValidUrl_Returns201WithLocation()
    {
        // Arrange
        using var client = factory.CreateClient();
        var url = NewUrl();

        // Act
        using var response = await client.PostAsJsonAsync("/api/repositories", new { url });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        response.Headers.Location!.ToString().Should().EndWith($"/api/repositories/{body.GetProperty("id").GetGuid()}");
        body.GetProperty("fullName").GetString().Should().Be(url["https://github.com/".Length..]);
    }

    [Fact]
    public async Task Post_InvalidUrl_Returns400ProblemDetails()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/repositories", new { url = "https://gitlab.com/a/b" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Post_NotAccessible_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync("/api/repositories", new { url = "https://github.com/someone-else/private" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_Duplicate_Returns409WithExistingId()
    {
        // Arrange
        using var client = factory.CreateClient();
        var url = NewUrl();
        var id = await AddAsync(client, url);

        // Act
        using var response = await client.PostAsJsonAsync("/api/repositories", new { url = url + ".git" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/repositories/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_AddedRepository_IsListed()
    {
        // Arrange
        using var client = factory.CreateClient();
        var id = await AddAsync(client, NewUrl());

        // Act
        var list = await client.GetFromJsonAsync<JsonElement>("/api/repositories");

        // Assert
        list.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(id);
    }

    [Fact]
    public async Task RunRepository_KnownRun_ReturnsRepository()
    {
        // Arrange
        using var client = factory.CreateClient();
        var id = await AddAsync(client, NewUrl());
        var runId = Guid.NewGuid();
        await factory.SeedSucceededRunAsync(id, "sample-report.json", runId, null);

        // Act
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}/repository");

        // Assert
        body.GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task RunRepository_UnknownRun_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/repository");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Runs_UnknownRepository_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/repositories/{Guid.NewGuid()}/runs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StartScan_NotAcknowledged_Returns409()
    {
        // Arrange (doc 10: the user must accept LLM data sharing before the first scan)
        using var client = factory.CreateClient();
        var id = await AddAsync(client, NewUrl());

        // Act
        using var response = await client.PostAsJsonAsync($"/api/repositories/{id}/scans", new { });

        // Assert
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        new { response.StatusCode, Title = problem.GetProperty("title").GetString() }
            .Should().Be(new { StatusCode = HttpStatusCode.Conflict, Title = "LLM data sharing not acknowledged" });
    }

    [Fact]
    public async Task AcknowledgeLlmSharing_ShowsOnTheRepository()
    {
        // Arrange
        using var client = factory.CreateClient();
        var id = await AddAsync(client, NewUrl());

        // Act
        using var response = await client.PostAsync($"/api/repositories/{id}/llm-sharing", null);

        // Assert
        var repository = await client.GetFromJsonAsync<JsonElement>($"/api/repositories/{id}");
        new { response.StatusCode, Acknowledged = repository.GetProperty("llmSharingAcknowledged").GetBoolean() }
            .Should().Be(new { StatusCode = HttpStatusCode.NoContent, Acknowledged = true });
    }

    [Fact]
    public async Task AcknowledgeLlmSharing_RecordsUser()
    {
        // Arrange
        using var client = factory.CreateClient("Member", "dev@example.com");
        var id = await AddAsync(client, NewUrl());

        // Act
        await client.PostAsync($"/api/repositories/{id}/llm-sharing", null);

        // Assert
        (await factory.LlmSharingAcknowledgedByAsync(id)).Should().Be("dev@example.com");
    }

    [Fact]
    public async Task AcknowledgeLlmSharing_Viewer_Returns403()
    {
        // Arrange
        var id = await factory.SeedRepositoryAsync(acknowledged: false);
        using var client = factory.CreateClient("Viewer");

        // Act
        using var response = await client.PostAsync($"/api/repositories/{id}/llm-sharing", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddRepository_ThenScan_SucceedsWithDefaultProfile()
    {
        // Arrange
        using var client = factory.CreateClient();
        var id = await AddAsync(client, NewUrl());
        (await client.PostAsync($"/api/repositories/{id}/llm-sharing", null)).EnsureSuccessStatusCode();

        // Act
        using var start = await client.PostAsJsonAsync($"/api/repositories/{id}/scans", new { });
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
        var outcome = await ScanFlowTests.WaitForOutcomeAsync(client, runId);

        // Assert
        outcome.TryGetProperty("meta", out _).Should().BeTrue(outcome.ToString());
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/repositories/{id}/runs");
        history.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Succeeded");
    }
}
