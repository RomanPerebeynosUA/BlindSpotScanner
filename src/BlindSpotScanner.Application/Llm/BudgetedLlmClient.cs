using System.Globalization;

namespace BlindSpotScanner.Application.Llm;

/// <summary>
/// One scan's cost cap (doc 02 / doc 10, <c>Llm:MaxCostUsdPerScan</c>). A call is not made once the
/// spend has reached the cap, or when one more call as costly as the costliest so far would cross it,
/// so the cap is kept rather than overshot. Refused calls fail at once: their items become unverified.
/// </summary>
public sealed class BudgetedLlmClient(ILlmClient inner, decimal maxCostUsd) : ILlmClient
{
    private decimal _costliestCall;
    private bool _refused;

    public decimal MaxCostUsd { get; } = maxCostUsd;

    public decimal SpentUsd { get; private set; }

    public bool Exhausted => _refused || SpentUsd >= MaxCostUsd;

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (SpentUsd >= MaxCostUsd || SpentUsd + _costliestCall > MaxCostUsd)
        {
            _refused = true;
            return new LlmResult(null, [$"LLM budget of ${MaxCostUsd.ToString(CultureInfo.InvariantCulture)} reached"], LlmUsage.None, FromCache: false);
        }

        var result = await inner.CompleteAsync(request, ct);
        SpentUsd += result.Usage.CostUsd;
        _costliestCall = Math.Max(_costliestCall, result.Usage.CostUsd);
        return result;
    }
}
