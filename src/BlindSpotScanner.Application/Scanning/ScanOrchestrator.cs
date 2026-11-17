using System.Globalization;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// <c>IScanOrchestrator.RunAsync(runId, ct)</c> (doc 03): runs P1–P7 for a run a worker has taken,
/// records a ScanStep per phase, publishes progress, verifies every citation at the pinned commit,
/// and stores exactly one outcome: Succeeded with an immutable report, Failed with reasons, or
/// Cancelled. A cost cap leaves later items unverified but the run still succeeds (doc 10).
/// If the run changed hands meanwhile (lease taken over, failed by the reaper, cancelled while
/// queued), its outcome is dropped and nothing is published: the other party's state stands.
/// </summary>
public sealed class ScanOrchestrator(
    IScanStore store,
    IScanEvents events,
    Func<ILlmClient> llm,
    ScanPipelineFactory pipelines,
    CitationVerifier verifier,
    TimeProvider time)
{
    /// <returns>The stored outcome, or null when the run was no longer this worker's to finish.</returns>
    public async Task<RunStatus?> RunAsync(Guid runId, CancellationToken ct)
    {
        var job = await store.LoadAsync(runId, ct);
        if (job is null)
        {
            return RunStatus.Failed;
        }

        var run = job.Run;
        try
        {
            await ScanAsync(job, ct);
        }
        catch (OperationCanceledException)
        {
            // Only a cancel request ends the run as Cancelled. A host shutdown leaves it Running,
            // so its lease expires and another worker retries it (doc 10).
            if (!await store.IsCancelRequestedAsync(run.Id, CancellationToken.None))
            {
                throw;
            }

            run.Cancel();
        }
        catch (Exception ex) when (ex is GitHubNotFoundException or GitHubAuthenticationException or GitHubRateLimitException or FormatException)
        {
            // These messages name the repository or URL only; they never carry a token.
            run.Fail([ex.Message]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Fail([$"scan failed: {ex.GetType().Name}"]);
        }

        if (!await store.SaveAsync(run, CancellationToken.None))
        {
            return null;
        }

        await events.CompletedAsync(run.Id, run.Status, CancellationToken.None);
        return run.Status;
    }

    private async Task ScanAsync(ScanJob job, CancellationToken ct)
    {
        var run = job.Run;
        var worker = pipelines.Options;
        if (run.ModelId != worker.ModelId || run.PromptSetVersion != worker.PromptSetVersion)
        {
            // The report must state the model and prompts that produced it (principle 6); fail before spending anything.
            run.Fail([$"This worker runs {worker.ModelId} with prompts {worker.PromptSetVersion}, but the scan was queued for {run.ModelId} with prompts {run.PromptSetVersion}."]);
            return;
        }

        if (job.Profile is null)
        {
            run.Fail([$"Profile version {run.ProfileVersion} of the repository no longer exists."]);
            return;
        }

        var budget = new BudgetedLlmClient(llm(), run.MaxCostUsd);
        var pipeline = pipelines.Create(budget);
        var url = job.Repository.HtmlUrl.TrimEnd('/') + (run.RequestedBranch is { Length: > 0 } branch ? $"/tree/{branch}" : string.Empty);
        var outcome = await pipeline.RunAsync(run.Id, url, job.Profile, job.Previous, new StepRecorder(run.Id, store, events, time), ct);
        run.Pin(outcome.Run.Branch, outcome.Run.CommitSha);

        var (report, violations) = (outcome.Report, outcome.Violations);
        if (budget.Exhausted)
        {
            var reason = $"LLM budget of ${run.MaxCostUsd.ToString(CultureInfo.InvariantCulture)} reached; items not yet processed are unverified";
            var detail = ReportDetail.From(report);
            detail = detail with { Inventory = detail.Inventory with { UnverifiedReasons = [.. detail.Inventory.UnverifiedReasons ?? [], reason] } };
            (report, violations) = pipeline.Assemble(detail, job.Previous);
        }

        var citations = await verifier.VerifyAsync(report, ct);
        if (citations.Failures.Count > 0)
        {
            run.Fail(citations.Failures.Select(f => $"citation: {f}").ToList());
        }
        else if (violations.Count > 0)
        {
            run.Fail(violations.Select(v => $"validation: {v}").ToList());
        }
        else
        {
            run.RecordReport(report with { Validation = report.Validation with { CitationsChecked = citations.Checked, CitationsResolved = citations.Resolved } });
            run.Succeed();
        }
    }

    /// <summary>Saves a step per phase, publishes progress, and stops the scan at a phase boundary once cancel is requested.</summary>
    private sealed class StepRecorder(Guid runId, IScanStore store, IScanEvents events, TimeProvider time) : IScanObserver
    {
        private DateTimeOffset _started;

        public async Task PhaseStartedAsync(string phase, CancellationToken ct)
        {
            if (await store.IsCancelRequestedAsync(runId, ct))
            {
                throw new OperationCanceledException($"Scan {runId} was cancelled.");
            }

            _started = time.GetUtcNow();
            var index = ScanPhases.All.ToList().IndexOf(phase);
            await events.ProgressAsync(runId, phase, index * 100 / ScanPhases.All.Count, $"{phase} started", ct);
        }

        public Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct) =>
            store.SaveStepAsync(
                new ScanStep
                {
                    Id = Guid.NewGuid(),
                    ScanRunId = runId,
                    Phase = report.Phase,
                    Status = RunStatus.Succeeded,
                    StartedAt = _started,
                    CompletedAt = time.GetUtcNow(),
                    InputTokens = report.Usage.InputTokens,
                    OutputTokens = report.Usage.OutputTokens,
                    CostUsd = report.Usage.CostUsd,
                    ReadInFull = report.ReadInFull,
                    Notes = report.Notes.Count == 0 ? null : string.Join("\n", report.Notes),
                },
                ct);
    }
}
