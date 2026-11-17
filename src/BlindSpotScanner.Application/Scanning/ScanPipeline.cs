using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

public sealed record ScanPipelineOptions(string ModelId, string PromptSetVersion, int MaxFileBytes);

/// <param name="Violations">ReportValidator's findings; empty means the report may be published (after M6's citation check).</param>
public sealed record ScanOutcome(IntegrationReport Report, IReadOnlyList<string> Violations, LlmUsage Usage);

/// <summary>
/// One scan, P1 → P7 (doc 05) without the M6 parts (citation verifier, queue, budgets, progress):
/// resolve, enumerate, extract, classify (rules, then LLM), map, grade, then assemble and validate.
/// </summary>
public sealed class ScanPipeline(
    IGitHubClient github,
    IBlobReader blobs,
    IEndpointExtractor extractor,
    ITestSyntaxReader reader,
    ILlmClient llm,
    ReportValidator validator,
    TimeProvider time,
    ScanPipelineOptions options)
{
    public const string SchemaVersion = "1.0";

    public async Task<ScanOutcome> RunAsync(Guid runId, string repoUrl, RepoProfile profile, CancellationToken ct)
    {
        var run = await new ResolvePhase(github).ResolveAsync(repoUrl, ct);
        var inventory = await new EnumeratePhase(github).EnumerateAsync(run, profile, ct);
        var endpoints = await new ExtractPhase(blobs, extractor).ExtractAsync(run, inventory, ct);
        var rules = await new ClassifyPhase(blobs, reader).ClassifyAsync(run, inventory, profile, ct);
        var classified = await new ClassifyAmbiguousPhase(llm, blobs).ClassifyAsync(run, rules, profile, options.MaxFileBytes, ct);
        var mapped = await new MapCoveragePhase(llm, blobs).MapAsync(run, endpoints, classified.Value, inventory, profile, options.MaxFileBytes, ct);
        var graded = await new GradeQualityPhase(llm, blobs).GradeAsync(run, classified.Value, mapped.Value, profile, options.MaxFileBytes, ct);
        var usage = classified.Usage.Add(mapped.Usage).Add(graded.Usage);

        var tests = classified.Value;
        var detail = new ReportDetail
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
                UnverifiedReasons = [.. endpoints.UnverifiedReasons, .. classified.Notes, .. mapped.Notes, .. graded.Notes],
            },
            EndpointCoverage = mapped.Value,
            Scorecard = graded.Value.Scorecard,
            Findings = graded.Value.Findings,
            TopRisks = graded.Value.TopRisks,
        };

        var report = ReportAssembler.Assemble(detail, previous: null);
        var violations = validator.Validate(report, previous: null);
        report = report with { Validation = new ReportValidation { Passed = violations.Count == 0, Method = "script", Violations = violations } };
        return new ScanOutcome(report, violations, usage);
    }
}
