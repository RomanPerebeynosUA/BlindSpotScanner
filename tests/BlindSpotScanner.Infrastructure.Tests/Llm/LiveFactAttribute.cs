namespace BlindSpotScanner.Infrastructure.Tests.Llm;

/// <summary>A test that calls the real Claude and GitHub APIs (billed); it runs only when ANTHROPIC_API_KEY and BSS_LIVE_REPO_URL are set (D20).</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BSS_LIVE_REPO_URL")))
        {
            Skip = "Live test: set ANTHROPIC_API_KEY and BSS_LIVE_REPO_URL to run it (it calls the billed Claude API).";
        }
    }
}
