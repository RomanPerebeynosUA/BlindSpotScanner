using BlindSpotScanner.Application.Llm;

namespace BlindSpotScanner.Application.Scanning;

/// <param name="ReadInFull">Files read in full by the phase (doc 05: every ScanStep records it).</param>
public sealed record PhaseReport(string Phase, LlmUsage Usage, IReadOnlyList<string> Notes, int ReadInFull);

/// <summary>Watches a scan phase by phase: the orchestrator records steps and progress, and stops a cancelled scan.</summary>
public interface IScanObserver
{
    /// <summary>Throw <see cref="OperationCanceledException"/> to stop the scan before the phase runs.</summary>
    Task PhaseStartedAsync(string phase, CancellationToken ct);

    Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct);
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
}
