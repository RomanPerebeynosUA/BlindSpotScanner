using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Application.Reports;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;
using Microsoft.Extensions.FileSystemGlobbing;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>The output of P6: per-dimension grades, findings and top risks (totals are computed later, never here).</summary>
public sealed record QualityGrade(IReadOnlyList<ScoreDimension> Scorecard, IReadOnlyList<ReportFinding> Findings, IReadOnlyList<TopRisk> TopRisks);

/// <summary>
/// P6 (doc 05, prompts/grade-quality.md): one LLM call grades the 7 rubric dimensions from the full
/// integration tests and their fixtures. Dimension names and weights come from <see cref="Rubric"/>.
/// Without tests or without an answer, every dimension is unscorable (principle 5: N/A, not a guess).
/// Uncovered-endpoint findings are derived from P5 in code. Every scorecard citation must name a
/// method or class declared in a file the model was given, else the answer is re-asked (principle 1);
/// finding and risk evidence that does not resolve is dropped and noted instead (D33).
/// </summary>
public sealed partial class GradeQualityPhase(ILlmClient llm, IBlobReader blobs, ITestSyntaxReader reader)
{
    private const string NotAssessed = "N/A — insufficient input";
    private const int MaxRisks = 5;

    public async Task<PhaseResult<QualityGrade>> GradeAsync(
        RunContext run, TestClassification tests, IReadOnlyList<ControllerCoverage> coverage, RepoProfile profile, int maxFileBytes, CancellationToken ct)
    {
        var notes = new List<string>();
        var uncovered = UncoveredEndpointFindings(coverage);
        var declared = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var testFiles = await ReadAsync(run, tests.IntegrationTests, maxFileBytes, notes, declared, ct);
        if (testFiles.Count == 0)
        {
            notes.Add("quality not graded: no integration tests could be read");
            return new PhaseResult<QualityGrade>(new QualityGrade(Unscorable(), uncovered, []), LlmUsage.None, notes);
        }

        var fixtures = await ReadAsync(run, Fixtures(tests, profile), maxFileBytes, notes, declared, ct);
        var endpoints = coverage.SelectMany(c => c.Endpoints).ToList();
        var input = JsonSerializer.Serialize(
            new
            {
                integrationMappedTo = Kebab(profile.IntegrationMappedTo),
                profileMarkdown = profile.Markdown,
                productionSeams = Array.Empty<string>(),
                tests = testFiles,
                fixtures,
                coverageSummaryForContext = new
                {
                    endpointsTotal = endpoints.Count,
                    none = endpoints.Where(e => e.Status == CoverageStatus.None).Select(e => EndpointKey.Display(e.Method, e.Route)),
                },
            },
            ClassifyAmbiguousPhase.Json);

        var result = await llm.CompleteAsync(new LlmRequest(Prompts.GradeQuality, input, LlmSchemas.Get(Prompts.GradeQuality), answer => Check(answer, declared)), ct);
        if (result.Output is not { } output)
        {
            notes.Add($"quality grading failed ({result.Errors.FirstOrDefault() ?? "no answer"})");
            return new PhaseResult<QualityGrade>(new QualityGrade(Unscorable(), uncovered, []), result.Usage, notes);
        }

        string Full(string citation) => Complete(citation, declared);

        // D33: finding and risk evidence that can't be verified is dropped (and noted), not re-asked.
        List<string> Kept(string where, IEnumerable<string> evidence) => evidence.Select(e => (Given: e, Full: Full(e)))
            .Where(e =>
            {
                var kept = Citation().IsMatch(e.Full) && Resolves(e.Full, declared);
                if (!kept)
                {
                    notes.Add($"{where}: dropped evidence '{e.Given}' (not a method or class of a file the model was given)");
                }

                return kept;
            })
            .Select(e => e.Full)
            .ToList();

        var findings = uncovered.Concat(output.GetProperty("findings").EnumerateArray().Select(f => LlmFinding(f, Kept)))
            .Select((f, i) => f with { Id = $"f{i + 1}" })
            .ToList();
        var risks = output.GetProperty("topRisks").EnumerateArray().Select(r => Risk(r, Kept)).ToList();
        return new PhaseResult<QualityGrade>(new QualityGrade(Scorecard(output, Full), findings, risks), result.Usage, notes);
    }

    private async Task<List<object>> ReadAsync(RunContext run, IEnumerable<ClassifiedTestFile> files, int maxFileBytes, List<string> notes, Dictionary<string, IReadOnlyList<string>> declared, CancellationToken ct)
    {
        var read = new List<object>();
        foreach (var file in files)
        {
            if (LlmContentPolicy.IsDenied(file.Path))
            {
                notes.Add($"{file.Path}: not sent to the LLM (denied path)");
                continue;
            }

            var content = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
            if (Encoding.UTF8.GetByteCount(content) > maxFileBytes)
            {
                notes.Add($"{file.Path}: not sent to the LLM (larger than {maxFileBytes} bytes)");
                continue;
            }

            read.Add(new { path = file.Path, content });
            var facts = reader.Read(content);
            declared[file.Path] = [.. facts.Methods, .. facts.Classes];
        }

        return read;
    }

    /// <summary>Helpers next to the integration tests (factories, seeders): Other files inside the integration globs.</summary>
    private static IEnumerable<ClassifiedTestFile> Fixtures(TestClassification tests, RepoProfile profile)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(profile.IntegrationGlobs);
        return tests.Files.Where(f => f.Kind == TestFileKind.Other && matcher.Match(f.Path).HasMatches);
    }

    private static List<string> Check(JsonElement output, IReadOnlyDictionary<string, IReadOnlyList<string>> declared)
    {
        var errors = new List<string>();
        var dimensions = output.GetProperty("scorecard").EnumerateArray().ToList();
        foreach (var (id, _, _) in Rubric.Dimensions)
        {
            if (dimensions.Count(d => d.GetProperty("id").GetInt32() == id) != 1)
            {
                errors.Add($"scorecard: dimension {id} must appear exactly once");
            }
        }

        foreach (var dimension in dimensions)
        {
            var id = dimension.GetProperty("id").GetInt32();
            if (Rubric.Dimensions.All(d => d.Id != id))
            {
                errors.Add($"scorecard: unknown dimension {id}");
                continue;
            }

            var scorable = dimension.GetProperty("scorable").GetBoolean();
            var score = dimension.GetProperty("score");
            var hasScore = score.ValueKind == JsonValueKind.Number && score.GetInt32() is >= 1 and <= 10;
            if (scorable != hasScore || (!scorable && score.ValueKind != JsonValueKind.Null))
            {
                errors.Add($"scorecard/{id}: a scorable dimension needs a score from 1 to 10; an unscorable one needs null");
            }

            var bullets = dimension.GetProperty("justificationBullets");
            if (bullets.GetArrayLength() != 4)
            {
                errors.Add($"scorecard/{id}: needs exactly 4 justification bullets");
            }
            else if (scorable && !bullets[2].GetString()!.Contains("::", StringComparison.Ordinal))
            {
                errors.Add($"scorecard/{id}: the evidence bullet of a scored dimension needs a path::Test citation");
            }

            // The report schema types scorecard evidence as citations; the evidence bullet cites in text.
            CheckCitations(errors, $"scorecard/{id}", Strings(dimension.GetProperty("evidence")), Strings(bullets), declared);
        }

        // Report schema limits: at most 5 risks, each with 1 to 8 detail bullets.
        var risks = output.GetProperty("topRisks").EnumerateArray().ToList();
        if (risks.Count > MaxRisks)
        {
            errors.Add($"topRisks: at most {MaxRisks} risks");
        }

        for (var i = 0; i < risks.Count; i++)
        {
            errors.AddRange(ReportValidator.RiskTextViolations(risks[i].GetProperty("headline").GetString()!, risks[i].GetProperty("whyItMatters").GetString()!, $"topRisks/{i}"));
            if (risks[i].GetProperty("detail").GetArrayLength() is < 1 or > 8)
            {
                errors.Add($"topRisks/{i}/detail: needs 1 to 8 bullets");
            }
        }

        return errors;
    }


    /// <summary>
    /// Evidence entries must be well-formed citations, and every citation (in evidence or in text) must
    /// name a method declared in a test or fixture file the model was given; the final
    /// <see cref="CitationVerifier"/> would otherwise fail the run without a chance to correct it.
    /// </summary>
    private static void CheckCitations(
        List<string> errors, string where, IReadOnlyList<string> evidence, IEnumerable<string> texts, IReadOnlyDictionary<string, IReadOnlyList<string>> declared)
    {
        for (var k = 0; k < evidence.Count; k++)
        {
            if (!Citation().IsMatch(evidence[k]))
            {
                errors.Add($"{where}/evidence/{k}: malformed citation '{evidence[k]}'");
            }
        }

        var cited = evidence.Where(e => Citation().IsMatch(e)).Concat(texts.SelectMany(CitationVerifier.CitationsIn)).Distinct(StringComparer.Ordinal);
        foreach (var citation in cited)
        {
            if (!Resolves(Complete(citation, declared), declared))
            {
                errors.Add($"{where}: {citation} is not a method in the test or fixture files you were given");
            }
        }
    }


    /// <summary>
    /// A cited path missing its folders (<c>TodoApiTests.cs::GetTodos</c>) is completed when exactly one
    /// file the model was given ends with it (D32); anything else is returned unchanged.
    /// </summary>
    private static bool Resolves(string citation, IReadOnlyDictionary<string, IReadOnlyList<string>> declared)
    {
        var (path, name) = CitationVerifier.Split(citation);
        return declared.TryGetValue(path, out var names) && CitationVerifier.Declares(names, name);
    }

    private static string Complete(string citation, IReadOnlyDictionary<string, IReadOnlyList<string>> declared)
    {
        var (path, name) = CitationVerifier.Split(citation);
        if (declared.ContainsKey(path))
        {
            return citation;
        }

        var matches = declared.Keys.Where(k => k.EndsWith("/" + path, StringComparison.Ordinal)).Take(2).ToList();
        return matches.Count == 1 ? $"{matches[0]}::{name}" : citation;
    }

    private static List<ScoreDimension> Scorecard(JsonElement output, Func<string, string> full)
    {
        var byId = output.GetProperty("scorecard").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetInt32());
        return Rubric.Dimensions.Select(r =>
        {
            var d = byId[r.Id];
            var scorable = d.GetProperty("scorable").GetBoolean();
            return new ScoreDimension
            {
                Id = r.Id,
                Dimension = r.Name,
                Weight = r.Weight,
                Score = scorable ? d.GetProperty("score").GetInt32() : null,
                Scorable = scorable,
                Confidence = Enum.Parse<Confidence>(d.GetProperty("confidence").GetString()!),
                JustificationBullets = Strings(d.GetProperty("justificationBullets")).Select(b => CitationVerifier.RewriteCitations(b, full)).ToList(),
                Evidence = Strings(d.GetProperty("evidence")).Select(full).ToList(),
            };
        }).ToList();
    }

    private static List<ScoreDimension> Unscorable() => Rubric.Dimensions.Select(r => new ScoreDimension
    {
        Id = r.Id,
        Dimension = r.Name,
        Weight = r.Weight,
        Score = null,
        Scorable = false,
        Confidence = Confidence.Low,
        JustificationBullets = [$"What is tested: {NotAssessed}", $"What is missing: {NotAssessed}", "Evidence: none", $"Why this score: {NotAssessed}"],
        Evidence = [],
    }).ToList();

    private static List<ReportFinding> UncoveredEndpointFindings(IReadOnlyList<ControllerCoverage> coverage) =>
        coverage.SelectMany(c => c.Endpoints).Where(e => e.Status == CoverageStatus.None).Select(e =>
        {
            var display = EndpointKey.Display(e.Method, e.Route);
            var key = EndpointKey.Of(e.Method, e.Route);
            return new ReportFinding
            {
                Id = string.Empty,
                Kind = FindingKind.UncoveredEndpoint,
                Severity = Severity.High,
                // Same shape as the examples: the method upper-case, the route normalized (doc 08).
                StableKey = $"uncovered-endpoint:{display.Split(' ')[0]} {key[(key.IndexOf(' ') + 1)..]}",
                Headline = $"{display} has no integration test",
                Detail = [display],
                Evidence = [],
            };
        }).Select((f, i) => f with { Id = $"f{i + 1}" }).ToList();

    private static ReportFinding LlmFinding(JsonElement f, Func<string, IEnumerable<string>, List<string>> kept)
    {
        var kind = Enum.Parse<FindingKind>(f.GetProperty("kind").GetString()!);
        return new ReportFinding
        {
            Id = string.Empty,
            Kind = kind,
            Severity = Enum.Parse<Severity>(f.GetProperty("severity").GetString()!),
            StableKey = $"{KebabWord().Replace(kind.ToString(), "$1-$2").ToLowerInvariant()}:{f.GetProperty("subject").GetString()}",
            Headline = f.GetProperty("headline").GetString()!,
            Detail = Strings(f.GetProperty("detail")),
            Evidence = kept($"finding '{f.GetProperty("headline").GetString()}'", Strings(f.GetProperty("evidence"))),
        };
    }

    private static TopRisk Risk(JsonElement r, Func<string, IEnumerable<string>, List<string>> kept) => new()
    {
        Rank = 0, // ReportAssembler numbers the risks.
        Headline = r.GetProperty("headline").GetString()!,
        WhyItMatters = r.GetProperty("whyItMatters").GetString()!,
        Severity = Enum.Parse<Severity>(r.GetProperty("severity").GetString()!),
        Detail = Strings(r.GetProperty("detail")),
        Evidence = kept($"risk '{r.GetProperty("headline").GetString()}'", Strings(r.GetProperty("evidence"))),
    };

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToList();

    private static string Kebab(IntegrationMappedTo value) => value switch
    {
        IntegrationMappedTo.RealSeam => "real-seam",
        IntegrationMappedTo.E2eMockedData => "e2e-mocked-data",
        IntegrationMappedTo.E2eRealBackend => "e2e-real-backend",
        IntegrationMappedTo.Contract => "contract",
        _ => "other",
    };

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex KebabWord();

    [GeneratedRegex(@"^[^:]+\.[A-Za-z0-9]+::[A-Za-z_][\w.]*$")]
    private static partial Regex Citation();
}
