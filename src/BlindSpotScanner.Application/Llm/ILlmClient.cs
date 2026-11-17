using System.Text.Json;

namespace BlindSpotScanner.Application.Llm;

/// <summary>
/// The one way to call the LLM (CLAUDE.md): structured JSON output validated against a schema, with
/// at most 2 re-asks on failure. Implementations scrub secrets from the input before sending it.
/// </summary>
public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}

/// <param name="Prompt">A <see cref="Prompts"/> name; the task prompt loaded from <c>prompts/</c>.</param>
/// <param name="InputJson">The prompt's input, as JSON.</param>
/// <param name="OutputSchemaJson">The JSON Schema the answer must match.</param>
/// <param name="Validate">Rule checks on a schema-valid answer; errors trigger a re-ask like schema errors.</param>
public sealed record LlmRequest(string Prompt, string InputJson, string OutputSchemaJson, Func<JsonElement, IReadOnlyList<string>>? Validate = null);

/// <param name="Output">The accepted answer, or null when every attempt failed.</param>
/// <param name="Errors">Empty on success; the last attempt's errors on failure.</param>
public sealed record LlmResult(JsonElement? Output, IReadOnlyList<string> Errors, LlmUsage Usage, bool FromCache);

public sealed record LlmUsage(int InputTokens, int OutputTokens, decimal CostUsd)
{
    public static readonly LlmUsage None = new(0, 0, 0m);

    public LlmUsage Add(LlmUsage other) => new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens, CostUsd + other.CostUsd);
}

/// <summary>Task prompt names (file names in <c>prompts/</c> without extension).</summary>
public static class Prompts
{
    public const string ClassifyTests = "classify-tests";
    public const string MapCoverage = "map-coverage";
    public const string GradeQuality = "grade-quality";
    public const string DraftProfile = "draft-profile";
    public const string GenerateFix = "generate-fix";
}
