using Microsoft.Extensions.FileSystemGlobbing;

namespace BlindSpotScanner.Application.Llm;

/// <summary>Doc 10: files that are never sent to the LLM, whatever their content.</summary>
public static class LlmContentPolicy
{
    private static readonly Matcher Denied = CreateMatcher();

    public static bool IsDenied(string path) => Denied.Match(path).HasMatches;

    private static Matcher CreateMatcher()
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(["**/.env*", "**/*.pem", "**/*.pfx", "**/appsettings.*.json", "**/secrets.*"]);
        return matcher;
    }
}
