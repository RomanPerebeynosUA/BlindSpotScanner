using System.Globalization;

namespace BlindSpotScanner.Application.Llm;

/// <summary>
/// One scan's cost cap (doc 02 / doc 10, <c>Llm:MaxCostUsdPerScan</c>). Once the spend reaches the
/// cap, further calls are not made: they fail at once, so their items become unverified (never guessed).
/// </summary>
public sealed class BudgetedLlmClient(ILlmClient inner, decimal maxCostUsd) : ILlmClient
{
    public decimal MaxCostUsd { get; } = maxCostUsd;

    public decimal SpentUsd { get; private set; }

    public bool Exhausted => SpentUsd >= MaxCostUsd;

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (Exhausted)
        {
            return new LlmResult(null, [$"LLM budget of ${MaxCostUsd.ToString(CultureInfo.InvariantCulture)} reached"], LlmUsage.None, FromCache: false);
        }

        var result = await inner.CompleteAsync(request, ct);
        SpentUsd += result.Usage.CostUsd;
        return result;
    }
}
