using System.Text.Json;
using Anthropic;
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

    private readonly PromptLibrary _prompts;
    private readonly LlmOptions _options;
    private readonly AnthropicClient _client;

    public ClaudeLlmClient(PromptLibrary prompts, IOptions<LlmOptions> options)
    {
        _prompts = prompts;
        _options = options.Value;
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        _client = _options.BaseUrl is { } baseUrl
            ? new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl }
            : new AnthropicClient { ApiKey = apiKey };
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var schema = JsonSchema.FromText(request.OutputSchemaJson);
        var format = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.OutputSchemaJson)
            ?? throw new ArgumentException("The output schema is empty.", nameof(request));
        var messages = new List<BetaMessageParam>
        {
            new BetaUserMessageParam { Content = $"{_prompts.Task(request.Prompt)}\n\nInput:\n{SecretScrubber.Scrub(request.InputJson)}" },
        };

        var usage = LlmUsage.None;
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var response = await _client.Beta.Messages.Create(
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

            usage = usage.Add(Usage(response));
            var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
            errors = Check(response, text, schema, request.Validate, out var output);
            if (errors.Count == 0)
            {
                return new LlmResult(output, [], usage, FromCache: false);
            }

            messages.Add(new BetaAssistantMessageParam { Content = text.Length > 0 ? text : "(no answer)" });
            messages.Add(new BetaUserMessageParam { Content = $"Your previous answer was rejected:\n- {string.Join("\n- ", errors)}\nReturn corrected JSON only." });
        }

        return new LlmResult(null, errors, usage, FromCache: false);
    }

    private static IReadOnlyList<string> Check(BetaMessage response, string text, JsonSchema schema, Func<JsonElement, IReadOnlyList<string>>? validate, out JsonElement output)
    {
        output = default;
        var stopReason = response.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            return [$"the answer stopped early ({stopReason})"];
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

    private LlmUsage Usage(BetaMessage response)
    {
        var input = (int)response.Usage.InputTokens;
        var output = (int)response.Usage.OutputTokens;
        return new LlmUsage(input, output, (input * _options.InputUsdPerMTok + output * _options.OutputUsdPerMTok) / 1_000_000m);
    }
}
