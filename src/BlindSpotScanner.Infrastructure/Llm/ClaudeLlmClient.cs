using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using BlindSpotScanner.Application.Llm;
using Json.Schema;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Llm;

/// <summary>
/// <see cref="ILlmClient"/> over the official Anthropic SDK (D20). Each attempt sends the system
/// prompt, the task prompt and the scrubbed input, and asks for structured JSON output. An answer
/// that is cut off, refused, not JSON, off-schema or rejected by the request's rule check is re-asked
/// with the errors, at most twice. Server-side refusal fallback is on (claude-api default).
/// </summary>
public sealed class ClaudeLlmClient : ILlmClient
{
    private const int MaxAttempts = 3;
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private static readonly EvaluationOptions SchemaOptions = new() { OutputFormat = OutputFormat.List };

    /// <summary>Relaxed escaping keeps quotes and code readable for the model; the text is a prompt, not HTML.</summary>
    private static readonly JsonSerializerOptions PromptJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly PromptLibrary _prompts;
    private readonly LlmOptions _options;
    private readonly IAnthropicClient _client;

    public ClaudeLlmClient(PromptLibrary prompts, IOptions<LlmOptions> options)
    {
        _prompts = prompts;
        _options = options.Value;
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var client = _options.BaseUrl is { } baseUrl
            ? new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl }
            : new AnthropicClient { ApiKey = apiKey };
        _client = string.IsNullOrWhiteSpace(_options.WorkspaceId)
            ? client
            : client.WithOptions(o => o with { ExtraHeaders = new Dictionary<string, string> { [LlmOptions.WorkspaceHeader] = _options.WorkspaceId } });
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var schema = JsonSchema.FromText(request.OutputSchemaJson);
        var format = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.OutputSchemaJson)
            ?? throw new ArgumentException("The output schema is empty.", nameof(request));
        var messages = new List<BetaMessageParam>
        {
            new BetaUserMessageParam { Content = $"{_prompts.Task(request.Prompt)}\n\nInput:\n{ScrubStrings(request.InputJson)}" },
        };

        var usage = LlmUsage.None;
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            BetaMessage response;
            try
            {
                response = await _client.Beta.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = _options.MaxTokensPerCall,
                    System = _prompts.System,
                    Messages = messages,
                    OutputConfig = new BetaOutputConfig { Format = new BetaJsonOutputFormat { Schema = format } },
                    Betas = [FallbackBeta],
                    Fallbacks = new Default(),
                },
                ct);
            }
            catch (Exception ex) when (ex is AnthropicException or HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // The SDK already retried 429/5xx. Only the exception type is reported: messages may echo request details.
                return new LlmResult(null, [$"the Claude API call failed ({ex.GetType().Name})"], usage, FromCache: false);
            }

            usage = usage.Add(Usage(response));
            var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            errors = Check(response, text, schema, request.Validate, out var output);
            if (errors.Count == 0)
            {
                return new LlmResult(output, [], usage, FromCache: false);
            }

            // A refusal arrives after the whole fallback chain declined; asking again only costs more.
            if (response.StopReason is { } stop && stop == BetaStopReason.Refusal)
            {
                return new LlmResult(null, errors, usage, FromCache: false);
            }

            messages.Add(new BetaAssistantMessageParam { Content = text.Length > 0 ? text : "(no answer)" });
            messages.Add(new BetaUserMessageParam { Content = $"Your previous answer was rejected:\n- {string.Join("\n- ", errors)}\nReturn corrected JSON only." });
        }

        return new LlmResult(null, errors, usage, FromCache: false);
    }

    private static IReadOnlyList<string> Check(BetaMessage response, string text, JsonSchema schema, Func<JsonElement, IReadOnlyList<string>>? validate, out JsonElement output)
    {
        output = default;
        if (response.StopReason is { } reason && (reason == BetaStopReason.Refusal || reason == BetaStopReason.MaxTokens))
        {
            return [$"the answer stopped early ({(reason == BetaStopReason.Refusal ? "refusal" : "max_tokens")})"];
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            output = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return [$"the answer is not valid JSON ({ex.Message})"];
        }

        var results = schema.Evaluate(output, SchemaOptions);
        if (!results.IsValid)
        {
            return (results.Details ?? []).Prepend(results)
                .Where(d => d.Errors is not null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"))
                .ToList();
        }

        return validate?.Invoke(output) ?? [];
    }

    /// <summary>
    /// Scrubs every string value of the input JSON before it is re-serialized. Scrubbing the serialized
    /// text would miss secrets: the escapes for a quote or an ampersand end in a digit, which defeats the
    /// scrubber's word-boundary anchors.
    /// </summary>
    private static string ScrubStrings(string inputJson)
    {
        var node = JsonNode.Parse(inputJson);
        return Scrub(node)?.ToJsonString(PromptJson) ?? "null";

        static JsonNode? Scrub(JsonNode? value) => value switch
        {
            JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Scrub(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Scrub).ToArray()),
            JsonValue leaf when leaf.TryGetValue<string>(out var text) => JsonValue.Create(SecretScrubber.Scrub(text)),
            _ => value?.DeepClone(),
        };
    }

    /// <summary>
    /// With server-side fallback, top-level usage covers only the attempt that answered; usage.iterations
    /// lists every attempt. All are priced at the configured rates (fallback models may bill differently).
    /// </summary>
    private LlmUsage Usage(BetaMessage response)
    {
        var (input, output) = response.Usage.Iterations is { Count: > 0 } iterations
            ? iterations.Select(Tokens).Aggregate((0L, 0L), (sum, t) => (sum.Item1 + t.Input, sum.Item2 + t.Output))
            : (response.Usage.InputTokens, response.Usage.OutputTokens);
        return new LlmUsage((int)input, (int)output, (input * _options.InputUsdPerMTok + output * _options.OutputUsdPerMTok) / 1_000_000m);
    }

    private static (long Input, long Output) Tokens(BetaUsageIteration iteration) => iteration.Value switch
    {
        BetaMessageIterationUsage m => (m.InputTokens, m.OutputTokens),
        BetaFallbackMessageIterationUsage f => (f.InputTokens, f.OutputTokens),
        BetaCompactionIterationUsage c => (c.InputTokens, c.OutputTokens),
        BetaAdvisorMessageIterationUsage a => (a.InputTokens, a.OutputTokens),
        _ => (0, 0),
    };
}
