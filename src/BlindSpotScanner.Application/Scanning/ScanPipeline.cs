using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

public sealed record ScanPipelineOptions(string ModelId, string PromptSetVersion, int MaxFileBytes);

/// <param name="Run">What P1 resolved (branch and pinned commit).</param>
/// <param name="Violations">ReportValidator's findings; empty means the report may be published once its citations verify.</param>
public sealed record ScanOutcome(RunContext Run, IntegrationReport Report, IReadOnlyList<string> Violations, LlmUsage Usage);

/// <summary>
/// One scan, P1 → P7 (doc 05): resolve, enumerate, extract, classify (rules, then LLM), map, grade,
/// then assemble (with the delta against <paramref name="previous"/>) and validate. The observer sees
/// every phase; the orchestrator adds citation verification, storage and progress (M6).
/// </summary>
public sealed class ScanPipeline(
    IGitHubClient github,
    IBlobReader blobs,
    IEndpointExtractor extractor,
    ITestSyntaxReader reader,
    ITestAssertionReader assertionReader,
    ILlmClient llm,
    ReportValidator validator,
    TimeProvider time,
    ScanPipelineOptions options)
{
    public const string SchemaVersion = "1.1";

    public async Task<ScanOutcome> RunAsync(Guid runId, string repoUrl, RepoProfile profile, IntegrationReport? previous, IScanObserver observer, CancellationToken ct)
    {
        async Task<T> Phase<T>(string phase, Func<Task<T>> work, Func<T, PhaseReport> report)
        {
            await observer.PhaseStartedAsync(phase, ct);
            T value;
            try
            {
                value = await work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await observer.PhaseFailedAsync(phase, ScanFailure.Describe(ex), CancellationToken.None);
                throw;
            }

            await observer.PhaseCompletedAsync(report(value), ct);
            return value;
        }

        static PhaseReport Plain(string phase, int readInFull = 0) => new(phase, LlmUsage.None, [], readInFull);

        var run = await Phase(ScanPhases.Resolve, () => new ResolvePhase(github).ResolveAsync(repoUrl, ct), r => new PhaseReport(ScanPhases.Resolve, LlmUsage.None, [], 0, r));
        var inventory = await Phase(ScanPhases.Enumerate, () => new EnumeratePhase(github).EnumerateAsync(run, profile, ct), i => new PhaseReport(ScanPhases.Enumerate, LlmUsage.None, i.UnverifiedReasons, 0));
        var endpoints = await Phase(ScanPhases.Extract, () => new ExtractPhase(blobs, extractor).ExtractAsync(run, inventory, ct), _ => Plain(ScanPhases.Extract, inventory.ControllerFileCount));
        var rules = await Phase(ScanPhases.Classify, () => new ClassifyPhase(blobs, reader).ClassifyAsync(run, inventory, profile, ct), _ => Plain(ScanPhases.Classify, inventory.TestFileCount));
        var classified = await Phase(ScanPhases.ClassifyWithLlm, () => new ClassifyAmbiguousPhase(llm, blobs).ClassifyAsync(run, rules, profile, options.MaxFileBytes, ct), r => new PhaseReport(ScanPhases.ClassifyWithLlm, r.Usage, r.Notes, 0));
        var mapped = await Phase(ScanPhases.Map, () => new MapCoveragePhase(llm, blobs).MapAsync(run, endpoints, classified.Value, inventory, profile, options.MaxFileBytes, ct), r => new PhaseReport(ScanPhases.Map, r.Usage, r.Notes, 0));
        // P6 (D27): assertion depth by code, then the LLM's grade of the other dimensions.
        var (assessed, graded) = await Phase(
            ScanPhases.Grade,
            async () =>
            {
                var assertions = await new AssessAssertionsPhase(blobs, assertionReader).AssessAsync(run, classified.Value, ct);
                var grade = await new GradeQualityPhase(llm, blobs).GradeAsync(run, classified.Value, mapped.Value, profile, assertions.Value, options.MaxFileBytes, ct);
                return (assertions, grade);
            },
            r => new PhaseReport(ScanPhases.Grade, r.grade.Usage, [.. r.assertions.Notes, .. r.grade.Notes], 0));
        var usage = classified.Usage.Add(mapped.Usage).Add(graded.Usage);

        var detail = Detail(runId, run, profile, inventory, endpoints, classified, mapped.Value, graded.Value, usage, [.. endpoints.UnverifiedReasons, .. classified.Notes, .. mapped.Notes, .. assessed.Notes, .. graded.Notes])
            with { TestAssertions = assessed.Value };
        var (report, violations) = await Phase(ScanPhases.Assemble, () => Task.FromResult(Assemble(detail, previous)), r => new PhaseReport(ScanPhases.Assemble, LlmUsage.None, r.Violations, 0));
        return new ScanOutcome(run, report, violations, usage);
    }

    /// <summary>Assembles and validates; the orchestrator calls it again when it adds an unverified reason.</summary>
    public (IntegrationReport Report, IReadOnlyList<string> Violations) Assemble(ReportDetail detail, IntegrationReport? previous)
    {
        var report = ReportAssembler.Assemble(detail, previous);
        var violations = validator.Validate(report, previous);
        return (report with { Validation = new ReportValidation { Passed = violations.Count == 0, Method = "script", Violations = violations } }, violations);
    }

    private ReportDetail Detail(
        Guid runId, RunContext run, RepoProfile profile, RepositoryInventory inventory, EndpointInventory endpoints,
        PhaseResult<TestClassification> classified, IReadOnlyList<ControllerCoverage> coverage, QualityGrade grade, LlmUsage usage, IReadOnlyList<string> unverifiedReasons)
    {
        var tests = classified.Value;
        return new ReportDetail
        {
            Meta = new ReportMeta
            {
                SchemaVersion = SchemaVersion,
                RunId = runId,
                Repo = new ReportRepo { Owner = run.Owner, Name = run.Name, Url = run.HtmlUrl },
                Branch = run.Branch,
                CommitSha = run.CommitSha,
                GeneratedAt = time.GetUtcNow(),
                ModelId = options.ModelId,
                PromptSetVersion = options.PromptSetVersion,
                ProfileVersion = profile.Version,
                IntegrationMappedTo = profile.IntegrationMappedTo,
                CostUsd = usage.CostUsd,
            },
            Inventory = new ReportInventory
            {
                ControllerFiles = inventory.ControllerFileCount,
                EndpointsTotal = endpoints.EndpointCount,
                TestFilesTotal = inventory.TestFileCount,
                IntegrationTestFiles = tests.IntegrationTests.Count,
                Classification = new Dictionary<string, int>
                {
                    ["integration"] = tests.IntegrationTests.Count,
                    ["other"] = tests.Files.Count(f => f.Kind == TestFileKind.Other),
                    ["ambiguous"] = tests.Ambiguous.Count,
                },
                // P4's rules parse every test file in full; nothing is sampled.
                ReadInFull = inventory.TestFileCount,
                UnverifiedReasons = unverifiedReasons,
            },
            EndpointCoverage = coverage,
            Scorecard = grade.Scorecard,
            Findings = grade.Findings,
            TopRisks = grade.TopRisks,
        };
    }
}
