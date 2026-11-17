using BlindSpotScanner.Application.Llm;

namespace BlindSpotScanner.Application.Scanning;

/// <param name="ReadInFull">Files read in full by the phase (doc 05: every ScanStep records it).</param>
/// <param name="Resolved">Set by P1: the branch and commit the scan is pinned to.</param>
public sealed record PhaseReport(string Phase, LlmUsage Usage, IReadOnlyList<string> Notes, int ReadInFull, RunContext? Resolved = null);

/// <summary>Watches a scan phase by phase: the orchestrator records steps and progress, and stops a cancelled scan.</summary>
public interface IScanObserver
{
    /// <summary>Throw <see cref="OperationCanceledException"/> to stop the scan before the phase runs.</summary>
    Task PhaseStartedAsync(string phase, CancellationToken ct);

    Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct);

    /// <summary>The phase threw (not a cancel); <paramref name="reason"/> is safe to store.</summary>
    Task PhaseFailedAsync(string phase, string reason, CancellationToken ct);
}

public static class ScanPhases
{
    public const string Resolve = "P1";
    public const string Enumerate = "P2";
    public const string Extract = "P3";
    public const string Classify = "P4";
    public const string ClassifyWithLlm = "P4b";
    public const string Map = "P5";
    public const string Grade = "P6";
    public const string Assemble = "P7";

    public static readonly IReadOnlyList<string> All = [Resolve, Enumerate, Extract, Classify, ClassifyWithLlm, Map, Grade, Assemble];
}

/// <summary>An observer that records nothing.</summary>
public sealed class NoScanObserver : IScanObserver
{
    public static readonly NoScanObserver Instance = new();

    public Task PhaseStartedAsync(string phase, CancellationToken ct) => Task.CompletedTask;

    public Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct) => Task.CompletedTask;

    public Task PhaseFailedAsync(string phase, string reason, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>How a scan failure is described: GitHub and URL errors name only the repository; anything else only its type.</summary>
public static class ScanFailure
{
    public static string Describe(Exception ex) =>
        ex is GitHub.GitHubNotFoundException or GitHub.GitHubAuthenticationException or GitHub.GitHubRateLimitException or FormatException
            ? ex.Message
            : $"scan failed: {ex.GetType().Name}";
}
