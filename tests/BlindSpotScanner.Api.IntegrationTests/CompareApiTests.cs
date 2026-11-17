using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>M8 acceptance (doc 11): the two example runs show the expected newly covered and regressed items.</summary>
public class CompareApiTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    private async Task<(Guid Previous, Guid Current)> SeedExamplesAsync(Guid? repositoryId = null)
    {
        var repoId = repositoryId ?? await factory.SeedRepositoryAsync();
        var (previous, current) = (Guid.NewGuid(), Guid.NewGuid());
        await factory.SeedSucceededRunAsync(repoId, "sample-report-previous.json", previous, null);
        await factory.SeedSucceededRunAsync(repoId, "sample-report.json", current, previous);
        return (previous, current);
    }

    [Fact]
    public async Task Compare_ExampleRuns_ReturnsNewlyCoveredAndNoRegressions()
    {
        // Arrange
        var (previous, current) = await SeedExamplesAsync();
        using var client = factory.CreateClient();

        // Act
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/runs/compare?base={previous}&target={current}");

        // Assert
        var delta = body.GetProperty("delta");
        delta.GetProperty("endpoints").GetProperty("newlyCovered").EnumerateArray().Select(e => e.GetString()).Should().Equal("POST api/orders");
        delta.GetProperty("endpoints").GetProperty("regressed").GetArrayLength().Should().Be(0);
        delta.GetProperty("metrics")[0].GetProperty("direction").GetString().Should().Be("improved");
        body.GetProperty("adHoc").GetBoolean().Should().BeFalse();
        body.GetProperty("base").GetProperty("runId").GetGuid().Should().Be(previous);
    }

    [Fact]
    public async Task Compare_SameRun_Returns400()
    {
        // Arrange
        var (_, current) = await SeedExamplesAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/runs/compare?base={current}&target={current}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Compare_RunsFromDifferentRepositories_Returns400()
    {
        // Arrange
        var (previous, _) = await SeedExamplesAsync();
        var (_, other) = await SeedExamplesAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/runs/compare?base={previous}&target={other}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Compare_QueuedRun_Returns409()
    {
        // Arrange
        var repoId = await factory.SeedRepositoryAsync();
        var (previous, _) = await SeedExamplesAsync(repoId);
        using var client = factory.CreateClient();
        using var start = await client.PostAsJsonAsync($"/api/repositories/{repoId}/scans", new { });
        var queued = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();

        // Act
        using var response = await client.GetAsync($"/api/runs/compare?base={previous}&target={queued}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Compare_UnknownRun_Returns404()
    {
        // Arrange
        var (previous, _) = await SeedExamplesAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync($"/api/runs/compare?base={previous}&target={Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
