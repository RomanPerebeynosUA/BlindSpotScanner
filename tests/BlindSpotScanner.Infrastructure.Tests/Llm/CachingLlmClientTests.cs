using System.Text.Json;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Infrastructure.Llm;
using BlindSpotScanner.Infrastructure.Tests.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BlindSpotScanner.Infrastructure.Tests.Llm;

[Collection("postgres")]
public class CachingLlmClientTests(PostgresFixture db)
{
    private const string Schema = """{ "type": "object" }""";

    private static readonly PromptLibrary Prompts = new();

    /// <summary>Each test uses its own input, so cached rows from other tests never match.</summary>
    private static LlmRequest NewRequest(Func<JsonElement, IReadOnlyList<string>>? validate = null) =>
        new(Application.Llm.Prompts.ClassifyTests, $$"""{"id":"{{Guid.NewGuid()}}"}""", Schema, validate);

    private static LlmResult Answer(string json) =>
        new(JsonDocument.Parse(json).RootElement.Clone(), [], new LlmUsage(100, 20, 0.0004m), FromCache: false);

    private async Task<LlmResult> CompleteWithFreshContextAsync(CountingLlmClient inner, LlmRequest request, string model = "claude-sonnet-5-5")
    {
        await using var context = db.CreateContext();
        var client = new CachingLlmClient(context, inner, Prompts, Options.Create(new LlmOptions { Model = model }));
        return await client.CompleteAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task Complete_SecondIdenticalRequest_ServedFromCache()
    {
        // Arrange
        var inner = new CountingLlmClient(Answer("""{"kind":"integration"}"""));
        var request = NewRequest();
        await CompleteWithFreshContextAsync(inner, request);

        // Act
        var second = await CompleteWithFreshContextAsync(inner, request);

        // Assert
        new { inner.Calls, second.FromCache, second.Usage, Kind = second.Output?.GetProperty("kind").GetString() }.Should().BeEquivalentTo(new
        {
            Calls = 1,
            FromCache = true,
            Usage = LlmUsage.None,
            Kind = "integration",
        });
    }

    [Fact]
    public async Task Complete_DifferentModel_IsAMiss()
    {
        // Arrange
        var inner = new CountingLlmClient(Answer("""{"kind":"integration"}"""));
        var request = NewRequest();
        await CompleteWithFreshContextAsync(inner, request, model: "claude-sonnet-5-5");

        // Act
        await CompleteWithFreshContextAsync(inner, request, model: "claude-opus-5-5");

        // Assert
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Complete_ChangedSystemPrompt_IsAMiss()
    {
        // Arrange (same task prompt and version; only the system prompt was edited)
        var inner = new CountingLlmClient(Answer("""{"kind":"integration"}"""));
        var request = NewRequest();
        static PromptLibrary Library(string system) => new(new Dictionary<string, string>
        {
            ["_system"] = system,
            [Application.Llm.Prompts.ClassifyTests] = "# Task: classify ambiguous test files — v1.0.0",
        });
        await using (var first = db.CreateContext())
        {
            await new CachingLlmClient(first, inner, Library("Rules v1"), Options.Create(new LlmOptions())).CompleteAsync(request, CancellationToken.None);
        }

        // Act
        await using var second = db.CreateContext();
        await new CachingLlmClient(second, inner, Library("Rules v1, plus a new rule"), Options.Create(new LlmOptions())).CompleteAsync(request, CancellationToken.None);

        // Assert
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Complete_MissAndHit_DoNotKeepEntriesTracked()
    {
        // Arrange
        var inner = new CountingLlmClient(Answer("""{"kind":"integration"}"""));
        var request = NewRequest();
        await using var context = db.CreateContext();
        var client = new CachingLlmClient(context, inner, Prompts, Options.Create(new LlmOptions()));

        // Act
        await client.CompleteAsync(request, CancellationToken.None);
        await client.CompleteAsync(request, CancellationToken.None);

        // Assert
        context.ChangeTracker.Entries<Domain.Entities.LlmCacheEntry>().Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_FailedOutput_IsNotCached()
    {
        // Arrange
        var inner = new CountingLlmClient(new LlmResult(null, ["bad"], LlmUsage.None, FromCache: false));
        var request = NewRequest();
        await CompleteWithFreshContextAsync(inner, request);

        // Act
        await CompleteWithFreshContextAsync(inner, request);

        // Assert
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Complete_CachedAnswerFailingValidator_IsAMiss()
    {
        // Arrange
        var inner = new CountingLlmClient(Answer("""{"kind":"integration"}"""));
        var request = NewRequest();
        await CompleteWithFreshContextAsync(inner, request);

        // Act (the rule check now rejects what was cached)
        await CompleteWithFreshContextAsync(inner, request with { Validate = _ => ["rule changed"] });

        // Assert
        inner.Calls.Should().Be(2);
    }

    private sealed class CountingLlmClient(LlmResult result) : ILlmClient
    {
        public int Calls { get; private set; }

        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
