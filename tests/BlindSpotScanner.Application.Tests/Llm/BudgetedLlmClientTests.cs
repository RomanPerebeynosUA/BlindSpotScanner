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
    public async Task Complete_BudgetReached_StopsCalling()
    {
        // Arrange (each call costs 0.0004; the cap is reached after the second)
        var inner = new FakeLlmClient(_ => FakeLlmClient.Json("{}"));
        var budgeted = new BudgetedLlmClient(inner, maxCostUsd: 0.0006m);

        // Act
        await budgeted.CompleteAsync(Request, CancellationToken.None);
        await budgeted.CompleteAsync(Request, CancellationToken.None);
        var third = await budgeted.CompleteAsync(Request, CancellationToken.None);

        // Assert
        new { Calls = inner.Requests.Count, budgeted.Exhausted, third.Output, third.Errors, third.Usage }.Should().BeEquivalentTo(new
        {
            Calls = 2,
            Exhausted = true,
            Output = (System.Text.Json.JsonElement?)null,
            Errors = new[] { "LLM budget of $0.0006 reached" },
            Usage = LlmUsage.None,
        });
    }
}
