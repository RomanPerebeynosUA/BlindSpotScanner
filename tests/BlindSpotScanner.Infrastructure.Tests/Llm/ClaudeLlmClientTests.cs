using System.Text.Json;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Infrastructure.Llm;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

public sealed class ClaudeLlmClientTests : IDisposable
{
    private const string Schema = """
        { "type": "object", "properties": { "answer": { "type": "string" } }, "required": ["answer"], "additionalProperties": false }
        """;

    private const string Scenario = "attempts";

    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private ClaudeLlmClient CreateClient() => new(new PromptLibrary(), Options.Create(new LlmOptions { ApiKey = "test-key", BaseUrl = _server.Url }));

    private static LlmRequest Call(string input = """{"q":1}""", Func<JsonElement, IReadOnlyList<string>>? validate = null) =>
        new(Prompts.ClassifyTests, input, Schema, validate);

    private static object Reply(string text, string stopReason = "end_turn") => new
    {
        id = "msg_1",
        type = "message",
        role = "assistant",
        model = "claude-sonnet-5-5",
        content = new[] { new { type = "text", text } },
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 1000, output_tokens = 200 },
    };

    /// <summary>Replies in order, one per request; the last reply repeats.</summary>
    private void RespondInOrder(params object[] replies)
    {
        for (var i = 0; i < replies.Length; i++)
        {
            var step = _server.Given(Request.Create().WithPath("/v1/messages").UsingPost()).InScenario(Scenario);
            step = i == 0 ? step : step.WhenStateIs($"after-{i}");
            if (i < replies.Length - 1)
            {
                step = step.WillSetStateTo($"after-{i + 1}");
            }

            step.RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(replies[i]));
        }
    }

    private List<string> RequestBodies() => _server.LogEntries.Select(e => e.RequestMessage!.Body!).ToList();

    [Fact]
    public async Task Complete_ValidJson_ReturnsOutputAndUsage()
    {
        // Arrange
        RespondInOrder(Reply("""{"answer":"integration"}"""));

        // Act
        var result = await CreateClient().CompleteAsync(Call(), CancellationToken.None);

        // Assert
        new { Answer = result.Output?.GetProperty("answer").GetString(), result.Usage, result.FromCache }.Should().BeEquivalentTo(new
        {
            Answer = "integration",
            Usage = new LlmUsage(1000, 200, 0.004m),
            FromCache = false,
        });
    }

    [Fact]
    public async Task Complete_Request_SendsModelSystemAndSchema()
    {
        // Arrange
        RespondInOrder(Reply("""{"answer":"x"}"""));

        // Act
        await CreateClient().CompleteAsync(Call(), CancellationToken.None);

        // Assert
        using var body = JsonDocument.Parse(RequestBodies().Single());
        new
        {
            Model = body.RootElement.GetProperty("model").GetString(),
            System = body.RootElement.GetProperty("system").ToString().Contains("integration-test auditor", StringComparison.Ordinal),
            Format = body.RootElement.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString(),
            Fallbacks = body.RootElement.GetProperty("fallbacks").ToString(),
        }.Should().BeEquivalentTo(new { Model = "claude-sonnet-5-5", System = true, Format = "json_schema", Fallbacks = "default" });
    }

    [Fact]
    public async Task Complete_SecretInInput_IsScrubbedFromRequest()
    {
        // Arrange
        var token = "ghp_" + new string('a', 36);
        RespondInOrder(Reply("""{"answer":"x"}"""));

        // Act
        await CreateClient().CompleteAsync(Call($$"""{"content":"Host=db;Password=s3cret; token {{token}}"}"""), CancellationToken.None);

        // Assert
        var body = RequestBodies().Single();
        new { HasPassword = body.Contains("s3cret", StringComparison.Ordinal), HasToken = body.Contains(token, StringComparison.Ordinal), Redacted = body.Contains("«redacted»", StringComparison.Ordinal) || body.Contains("\\u00ABredacted\\u00BB", StringComparison.Ordinal) }
            .Should().BeEquivalentTo(new { HasPassword = false, HasToken = false, Redacted = true });
    }

    [Fact]
    public async Task Complete_SchemaInvalidThenValid_ReasksWithErrors()
    {
        // Arrange
        RespondInOrder(Reply("""{"wrong":1}"""), Reply("""{"answer":"ok"}"""));

        // Act
        var result = await CreateClient().CompleteAsync(Call(), CancellationToken.None);

        // Assert
        new { Answer = result.Output?.GetProperty("answer").GetString(), Requests = RequestBodies().Count, Reasked = RequestBodies().Last().Contains("Your previous answer was rejected", StringComparison.Ordinal) }
            .Should().BeEquivalentTo(new { Answer = "ok", Requests = 2, Reasked = true });
    }

    [Fact]
    public async Task Complete_ValidatorErrors_TriggerReask()
    {
        // Arrange
        RespondInOrder(Reply("""{"answer":"bad"}"""), Reply("""{"answer":"good"}"""));
        static IReadOnlyList<string> Validate(JsonElement output) => output.GetProperty("answer").GetString() == "bad" ? ["e1: bad"] : [];

        // Act
        var result = await CreateClient().CompleteAsync(Call(validate: Validate), CancellationToken.None);

        // Assert
        new { Answer = result.Output?.GetProperty("answer").GetString(), ErrorSent = RequestBodies().Last().Contains("e1: bad", StringComparison.Ordinal) }
            .Should().BeEquivalentTo(new { Answer = "good", ErrorSent = true });
    }

    [Fact]
    public async Task Complete_InvalidThreeTimes_ReturnsNullOutputWithErrors()
    {
        // Arrange
        RespondInOrder(Reply("not json"));

        // Act
        var result = await CreateClient().CompleteAsync(Call(), CancellationToken.None);

        // Assert
        new { result.Output, HasErrors = result.Errors.Count > 0, Requests = RequestBodies().Count, result.Usage }.Should().BeEquivalentTo(new
        {
            Output = (JsonElement?)null,
            HasErrors = true,
            Requests = 3,
            Usage = new LlmUsage(3000, 600, 0.012m),
        });
    }

    [Fact]
    public async Task Complete_Refusal_CountsAsFailedAttempt()
    {
        // Arrange
        RespondInOrder(Reply("", stopReason: "refusal"), Reply("""{"answer":"ok"}"""));

        // Act
        var result = await CreateClient().CompleteAsync(Call(), CancellationToken.None);

        // Assert
        new { Answer = result.Output?.GetProperty("answer").GetString(), Requests = RequestBodies().Count }.Should().BeEquivalentTo(new { Answer = "ok", Requests = 2 });
    }
}
