using System.Diagnostics;
using System.Globalization;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using Microsoft.Extensions.Logging;

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
    TimeProvider time,
    ILogger<ScanOrchestrator> logger)
{
    /// <returns>The stored outcome, or null when the run was no longer this worker's to finish.</returns>
    public async Task<RunStatus?> RunAsync(Guid runId, CancellationToken ct)
    {
        var job = await store.LoadAsync(runId, ct);
        if (job is null)
        {
            // The run row is gone; there is nothing to record. (A run's repository can't vanish: a foreign key holds it.)
            return null;
        }

        // Doc 10: every log line of the scan carries its run id; phase lines also carry the phase.
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["runId"] = runId });
        using var span = ScanTelemetry.Source.StartActivity("scan.run");
        span?.SetTag("run.id", runId.ToString());
        var started = time.GetUtcNow();
        var run = job.Run;
        var budget = new BudgetedLlmClient(llm(), run.MaxCostUsd);
        var recorder = new StepRecorder(run, store, events, time, logger, span);
        try
        {
            // A retry starts over, so its steps replace those of the attempt that died.
            await store.ClearStepsAsync(run.Id, ct);
            await ScanAsync(job, budget, recorder, ct);
        }
        catch (RunTakenOverException)
        {
            return null;
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
        catch (Exception ex)
        {
            run.Fail([ScanFailure.Describe(ex)]);
        }

        if (run.Status != RunStatus.Succeeded)
        {
            run.RecordSpend(budget.SpentUsd);
        }

        if (!await store.SaveAsync(run, CancellationToken.None))
        {
            return null;
        }

        await events.CompletedAsync(run.Id, run.Status, CancellationToken.None);
        logger.LogInformation("Scan finished {status}", run.Status);
        Record(run, recorder, budget.SpentUsd, time.GetUtcNow() - started, span);
        return run.Status;
    }

    /// <summary>Doc 10 metrics for a finished scan.</summary>
    private static void Record(ScanRun run, StepRecorder recorder, decimal spentUsd, TimeSpan elapsed, Activity? span)
    {
        var status = new KeyValuePair<string, object?>("status", run.Status.ToString());
        span?.SetTag("status", run.Status.ToString());
        ScanTelemetry.Duration.Record(elapsed.TotalSeconds, status);
        ScanTelemetry.Tokens.Add(recorder.InputTokens, new KeyValuePair<string, object?>("direction", "input"));
        ScanTelemetry.Tokens.Add(recorder.OutputTokens, new KeyValuePair<string, object?>("direction", "output"));
        ScanTelemetry.Cost.Add((double)spentUsd);
        if (run.Status == RunStatus.Failed && run.FailureReasons is [var reason, ..])
        {
            ScanTelemetry.Failures.Add(1, new KeyValuePair<string, object?>("reason", reason.Length > 60 ? reason[..60] : reason));
        }
    }

    private async Task ScanAsync(ScanJob job, BudgetedLlmClient budget, StepRecorder recorder, CancellationToken ct)
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

        var pipeline = pipelines.Create(budget);
        var url = job.Repository.HtmlUrl.TrimEnd('/') + (run.RequestedBranch is { Length: > 0 } branch ? $"/tree/{branch}" : string.Empty);
        var outcome = await pipeline.RunAsync(run.Id, url, job.Profile, job.Previous, recorder, ct);

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

    /// <summary>The run's state was changed by someone else mid-scan; this worker's work is dropped.</summary>
    private sealed class RunTakenOverException : Exception;

    /// <summary>
    /// Saves a step per phase (Failed when it throws), pins the run right after P1, publishes progress
    /// as each phase starts and ends (reaching 100), and stops the scan at a phase boundary once a
    /// cancel is requested. Phases run one after another, so one start time field is enough.
    /// </summary>
    private sealed class StepRecorder(ScanRun run, IScanStore store, IScanEvents events, TimeProvider time, ILogger logger, Activity? runSpan) : IScanObserver
    {
        private DateTimeOffset _started;
        private Activity? _phase;

        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        public async Task PhaseStartedAsync(string phase, CancellationToken ct)
        {
            if (await store.IsCancelRequestedAsync(run.Id, ct))
            {
                throw new OperationCanceledException($"Scan {run.Id} was cancelled.");
            }

            _started = time.GetUtcNow();

            // Phases report through these callbacks, so the span gets the run span as its explicit parent.
            _phase = ScanTelemetry.Source.StartActivity("scan.phase", ActivityKind.Internal, runSpan?.Context ?? default);
            _phase?.SetTag("phase", phase);
            logger.LogInformation("Phase {phase} started", phase);
            await events.ProgressAsync(run.Id, phase, Index(phase) * 100 / ScanPhases.All.Count, $"{phase} started", ct);
        }

        public async Task PhaseCompletedAsync(PhaseReport report, CancellationToken ct)
        {
            if (report.Resolved is { } resolved)
            {
                // Pinned as soon as it is known, so even a run that fails later records the commit it read.
                run.Pin(resolved.Branch, resolved.CommitSha);
                if (!await store.SaveAsync(run, ct))
                {
                    throw new RunTakenOverException();
                }
            }

            logger.LogInformation("Phase {phase} done: {inputTokens} input and {outputTokens} output tokens, {costUsd} USD", report.Phase, report.Usage.InputTokens, report.Usage.OutputTokens, report.Usage.CostUsd);
            InputTokens += report.Usage.InputTokens;
            OutputTokens += report.Usage.OutputTokens;
            EndPhase(ActivityStatusCode.Ok);
            await store.SaveStepAsync(Step(report.Phase, RunStatus.Succeeded, report.Usage, report.ReadInFull, report.Notes.Count == 0 ? null : string.Join("\n", report.Notes)), ct);
            await events.ProgressAsync(run.Id, report.Phase, (Index(report.Phase) + 1) * 100 / ScanPhases.All.Count, $"{report.Phase} done", ct);
        }

        public Task PhaseFailedAsync(string phase, string reason, CancellationToken ct)
        {
            logger.LogWarning("Phase {phase} failed: {reason}", phase, reason);
            EndPhase(ActivityStatusCode.Error);
            return store.SaveStepAsync(Step(phase, RunStatus.Failed, LlmUsage.None, 0, reason), ct);
        }

        private void EndPhase(ActivityStatusCode status)
        {
            _phase?.SetStatus(status);
            _phase?.Dispose();
            _phase = null;
        }

        private static int Index(string phase) => ScanPhases.All.ToList().IndexOf(phase);

        private ScanStep Step(string phase, RunStatus status, LlmUsage usage, int readInFull, string? notes) => new()
        {
            Id = Guid.NewGuid(),
            ScanRunId = run.Id,
            Phase = phase,
            Status = status,
            StartedAt = _started,
            CompletedAt = time.GetUtcNow(),
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CostUsd = usage.CostUsd,
            ReadInFull = readInFull,
            Notes = notes,
        };
    }
}
