using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlindSpotScanner.Api.Hubs;
using BlindSpotScanner.Domain.Reports;
using BlindSpotScanner.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BlindSpotScanner.Api.IntegrationTests;

/// <summary>M6 acceptance (doc 11): POST scan → the worker runs it with fake GitHub/LLM → report stored → GET returns it.</summary>
public class ScanFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private async Task<Guid> StartScanAsync(HttpClient client, Guid repositoryId)
    {
        using var response = await client.PostAsJsonAsync($"/api/repositories/{repositoryId}/scans", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();
    }

    /// <summary>Polls GET /api/runs/{id} until it returns a report or a terminal status.</summary>
    internal static async Task<JsonElement> WaitForOutcomeAsync(HttpClient client, Guid runId)
    {
        using var deadline = new CancellationTokenSource(Timeout);
        while (true)
        {
            var body = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}", deadline.Token);
            var finished = body.TryGetProperty("meta", out _)
                || body.GetProperty("status").GetString() is "Succeeded" or "Failed" or "Cancelled";
            if (finished)
            {
                return body;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), deadline.Token);
        }
    }

    /// <summary>Polls the steps until <paramref name="phase"/> has been recorded.</summary>
    private static async Task WaitForStepAsync(HttpClient client, Guid runId, string phase)
    {
        using var deadline = new CancellationTokenSource(Timeout);
        while (!(await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}/steps", deadline.Token)).EnumerateArray().Any(s => s.GetProperty("phase").GetString() == phase))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
        }
    }

    [Fact]
    public async Task PostScan_WorkerRuns_GetReturnsReport()
    {
        // Arrange
        using var client = factory.CreateClient();
        var repositoryId = await factory.SeedRepositoryAsync();

        // Act
        var runId = await StartScanAsync(client, repositoryId);
        var body = await WaitForOutcomeAsync(client, runId);

        // Assert
        var json = body.GetRawText();
        var report = ReportJsonSerializer.Deserialize(json);
        new
        {
            SchemaErrors = new ReportSchemaValidator().Validate(json),
            report.Meta.RunId,
            report.Summary.Endpoints.Total,
            report.Validation.Passed,
        }.Should().BeEquivalentTo(new { SchemaErrors = Array.Empty<string>(), RunId = runId, Total = 6, Passed = true });
    }

    [Fact]
    public async Task PostScan_ForgedCitation_RunFailed()
    {
        // Arrange
        using var client = factory.CreateClient();
        var repositoryId = await factory.SeedRepositoryAsync(marker: FakeClaudeServer.ForgeMarker);

        // Act
        var runId = await StartScanAsync(client, repositoryId);
        var body = await WaitForOutcomeAsync(client, runId);

        // Assert
        new
        {
            Status = body.GetProperty("status").GetString(),
            Reasons = body.GetProperty("failureReasons").EnumerateArray().Select(r => r.GetString()),
        }.Should().BeEquivalentTo(new
        {
            Status = "Failed",
            Reasons = new[] { "citation: tests/Bookstore.IntegrationTests/BooksApiTests.cs::Deletes_book: no method named Deletes_book" },
        });
    }

    [Fact]
    public async Task GetSteps_AfterScan_ListsEveryPhase()
    {
        // Arrange
        using var client = factory.CreateClient();
        var runId = await StartScanAsync(client, await factory.SeedRepositoryAsync());
        await WaitForOutcomeAsync(client, runId);

        // Act
        var steps = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}/steps");

        // Assert
        steps.EnumerateArray().Select(s => s.GetProperty("phase").GetString()).Should().Equal("P1", "P2", "P3", "P4", "P4b", "P5", "P6", "P7");
    }

    [Fact]
    public async Task Hub_Subscriber_ReceivesProgressAndCompleted()
    {
        // Arrange
        using var client = factory.CreateClient();
        await factory.Services.GetRequiredService<ScanEventRelay>().Listening.WaitAsync(Timeout);
        var progress = new List<string>();
        var completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/scans"), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        hub.On<Guid, string, int, string>("ScanProgress", (_, phase, _, _) => progress.Add(phase));
        hub.On<Guid, string>("ScanCompleted", (_, status) => completed.TrySetResult(status));
        await hub.StartAsync();
        var repositoryId = await factory.SeedRepositoryAsync();

        // Act
        var runId = await StartScanAsync(client, repositoryId);
        await hub.InvokeAsync("Subscribe", runId);
        var status = await completed.Task.WaitAsync(Timeout);

        // Assert
        new { status, HasProgress = progress.Count > 0 }.Should().BeEquivalentTo(new { status = "Succeeded", HasProgress = true });
    }

    [Fact]
    public async Task CancelRunningScan_StopsAtNextPhase_EndsCancelled()
    {
        // Arrange (P4b's LLM answer is slow; cancel once P4 is done, so it lands inside P4b)
        using var client = factory.CreateClient();
        var runId = await StartScanAsync(client, await factory.SeedRepositoryAsync(marker: FakeClaudeServer.SlowMarker));
        await WaitForStepAsync(client, runId, "P4");

        // Act
        using var cancel = await client.PostAsync($"/api/runs/{runId}/cancel", null);
        var body = await WaitForOutcomeAsync(client, runId);

        // Assert
        var steps = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}/steps");
        new
        {
            cancel.StatusCode,
            Status = body.GetProperty("status").GetString(),
            HasReport = body.TryGetProperty("meta", out _),
            LastPhase = steps.EnumerateArray().Last().GetProperty("phase").GetString(),
        }.Should().BeEquivalentTo(new { StatusCode = HttpStatusCode.Accepted, Status = "Cancelled", HasReport = false, LastPhase = "P4b" });
    }

    [Fact]
    public async Task PostScan_UnknownRepository_Returns404()
    {
        // Arrange
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync($"/api/repositories/{Guid.NewGuid()}/scans", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostScan_NoProfile_Returns409()
    {
        // Arrange
        using var client = factory.CreateClient();
        var repositoryId = await factory.SeedRepositoryAsync(withProfile: false);

        // Act
        using var response = await client.PostAsJsonAsync($"/api/repositories/{repositoryId}/scans", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}

public class QueuedScanTests(QueueOnlyApiFactory factory) : IClassFixture<QueueOnlyApiFactory>
{
    [Fact]
    public async Task CancelQueuedRun_IsCancelled()
    {
        // Arrange (no worker runs in this host, so the scan stays queued)
        using var client = factory.CreateClient();
        var repositoryId = await factory.SeedRepositoryAsync();
        using var start = await client.PostAsJsonAsync($"/api/repositories/{repositoryId}/scans", new { });
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetGuid();

        // Act
        using var cancel = await client.PostAsync($"/api/runs/{runId}/cancel", null);

        // Assert
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}");
        new { cancel.StatusCode, Status = run.GetProperty("status").GetString() }.Should().BeEquivalentTo(new { StatusCode = HttpStatusCode.Accepted, Status = "Cancelled" });
    }

    [Fact]
    public async Task PostScan_NegativeMaxCost_Returns400()
    {
        // Arrange
        using var client = factory.CreateClient();
        var repositoryId = await factory.SeedRepositoryAsync();

        // Act
        using var response = await client.PostAsJsonAsync($"/api/repositories/{repositoryId}/scans", new { maxCostUsd = -1 });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
