using System.Text.Json;
using BlindSpotScanner.Application.Llm;

namespace BlindSpotScanner.Application.Tests.Llm;

/// <summary>Answers each request with the given function and records every request.</summary>
internal sealed class FakeLlmClient(Func<LlmRequest, LlmResult> answer) : ILlmClient
{
    public static readonly LlmUsage CallUsage = new(100, 20, 0.0004m);

    public List<LlmRequest> Requests { get; } = [];

    /// <summary>A successful answer carrying <see cref="CallUsage"/>.</summary>
    public static LlmResult Json(string json) => new(JsonDocument.Parse(json).RootElement.Clone(), [], CallUsage, FromCache: false);

    public static LlmResult Failure(string error) => new(null, [error], CallUsage, FromCache: false);

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }
}
