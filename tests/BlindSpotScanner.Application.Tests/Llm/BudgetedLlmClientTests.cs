using BlindSpotScanner.Application.Llm;
using FluentAssertions;

namespace BlindSpotScanner.Application.Tests.Llm;

public class BudgetedLlmClientTests
{
    private static readonly LlmRequest Request = new(Prompts.ClassifyTests, "{}", "{}");

    [Fact]
    public async Task Complete_UnderBudget_CallsInner()
    {
        // Arrange
        var inner = new FakeLlmClient(_ => FakeLlmClient.Json("{}"));
        var budgeted = new BudgetedLlmClient(inner, maxCostUsd: 1.0m);

        // Act
        await budgeted.CompleteAsync(Request, CancellationToken.None);

        // Assert
        new { Calls = inner.Requests.Count, budgeted.SpentUsd, budgeted.Exhausted }.Should().BeEquivalentTo(new { Calls = 1, SpentUsd = 0.0004m, Exhausted = false });
    }

    [Fact]
    public async Task Complete_NextCallWouldCrossCap_IsNotMade()
    {
        // Arrange (each call costs 0.0004; a second one would end at 0.0008, over the 0.0006 cap)
        var inner = new FakeLlmClient(_ => FakeLlmClient.Json("{}"));
        var budgeted = new BudgetedLlmClient(inner, maxCostUsd: 0.0006m);

        // Act
        await budgeted.CompleteAsync(Request, CancellationToken.None);
        var second = await budgeted.CompleteAsync(Request, CancellationToken.None);

        // Assert
        new { Calls = inner.Requests.Count, budgeted.SpentUsd, budgeted.Exhausted, second.Output, second.Errors, second.Usage }.Should().BeEquivalentTo(new
        {
            Calls = 1,
            SpentUsd = 0.0004m,
            Exhausted = true,
            Output = (System.Text.Json.JsonElement?)null,
            Errors = new[] { "LLM budget of $0.0006 reached" },
            Usage = LlmUsage.None,
        });
    }
}
