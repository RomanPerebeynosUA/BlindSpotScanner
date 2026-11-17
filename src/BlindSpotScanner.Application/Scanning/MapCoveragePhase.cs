using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlindSpotScanner.Application.GitHub;
using BlindSpotScanner.Application.Llm;
using BlindSpotScanner.Domain.Entities;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Scanning;

/// <summary>
/// P5 (doc 05, prompts/map-coverage.md): one LLM call per controller, with the full text of the
/// integration tests that mention its routes. The hard rules are enforced on the answer, not
/// trusted: every endpoint gets exactly one status; <c>none</c> only when every candidate test was
/// read; covered/weak/skipped evidence must name a test method of a file that was sent.
/// </summary>
public sealed partial class MapCoveragePhase(ILlmClient llm, IBlobReader blobs)
{
    private const string Unresolved = "evidence did not resolve to a test that was read";

    public async Task<PhaseResult<IReadOnlyList<ControllerCoverage>>> MapAsync(
        RunContext run, EndpointInventory endpoints, TestClassification tests, RepositoryInventory inventory, RepoProfile profile, int maxFileBytes, CancellationToken ct)
    {
        var notes = new List<string>();
        var readable = new List<(ClassifiedTestFile File, string Content)>();
        var withheld = false;
        foreach (var file in tests.IntegrationTests)
        {
            if (LlmContentPolicy.IsDenied(file.Path))
            {
                withheld = true;
                notes.Add($"{file.Path}: not sent to the LLM (denied path)");
                continue;
            }

            var content = await blobs.ReadAsync(run.Owner, run.Name, file.BlobSha, ct);
            if (Encoding.UTF8.GetByteCount(content) > maxFileBytes)
            {
                withheld = true;
                notes.Add($"{file.Path}: not sent to the LLM (larger than {maxFileBytes} bytes)");
                continue;
            }

            readable.Add((file, content));
        }

        var allIncluded = !withheld && tests.Ambiguous.Count == 0 && inventory.UnverifiedReasons.Count == 0;
        var usage = LlmUsage.None;
        var coverage = new List<ControllerCoverage>();
        foreach (var controller in endpoints.Controllers)
        {
            var candidates = readable.Where(t => MentionsRoute(t.Content, controller)).ToList();
            IReadOnlyList<EndpointResult> results;
            if (candidates.Count == 0)
            {
                results = controller.Endpoints.Select(e => allIncluded
                    ? Result(e, CoverageStatus.None, verified: true, [], null, "no candidate integration tests were read")
                    : Result(e, CoverageStatus.Unverified, verified: false, [], null, "no candidate integration tests were read")).ToList();
            }
            else
            {
                var result = await llm.CompleteAsync(new LlmRequest(Prompts.MapCoverage, Input(controller, candidates, allIncluded, profile), LlmSchemas.Get(Prompts.MapCoverage)), ct);
                usage = usage.Add(result.Usage);
                if (result.Output is { } output)
                {
                    results = Apply(controller, output, candidates.Select(c => c.File).ToList(), allIncluded);
                }
                else
                {
                    notes.Add($"{controller.Controller}: mapping failed ({result.Errors.FirstOrDefault() ?? "no answer"})");
                    results = controller.Endpoints.Select(e => Result(e, CoverageStatus.Unverified, verified: false, [], null, "mapping failed")).ToList();
                }
            }

            coverage.Add(new ControllerCoverage
            {
                Controller = controller.Controller,
                SourcePath = controller.SourcePath,
                RoutePrefix = controller.RoutePrefix,
                Status = CoverageStatus.Unverified, // ReportAssembler computes the rollup.
                Endpoints = results,
            });
        }

        return new PhaseResult<IReadOnlyList<ControllerCoverage>>(coverage, usage, notes);
    }

    /// <summary>A test is a candidate if it mentions the prefix or a route's literal part before its first parameter.</summary>
    private static bool MentionsRoute(string content, ExtractedController controller) =>
        new[] { controller.RoutePrefix }
            .Concat(controller.Endpoints.Select(e => e.Route.Split('{')[0]))
            .Select(r => r?.Trim('/'))
            .Where(r => !string.IsNullOrEmpty(r))
            .Any(r => content.Contains(r!, StringComparison.OrdinalIgnoreCase));

    private static string Input(ExtractedController controller, List<(ClassifiedTestFile File, string Content)> candidates, bool allIncluded, RepoProfile profile) =>
        JsonSerializer.Serialize(
            new
            {
                controller = controller.Controller,
                routePrefix = controller.RoutePrefix,
                endpoints = controller.Endpoints.Select((e, i) => new { id = Id(i), method = e.Method.ToString().ToUpperInvariant(), route = e.Route }),
                allCandidateTestsIncluded = allIncluded,
                tests = candidates.Select(c => new { path = c.File.Path, content = c.Content }),
                profileMarkdown = profile.Markdown,
            },
            ClassifyAmbiguousPhase.Json);

    private static List<EndpointResult> Apply(ExtractedController controller, JsonElement output, List<ClassifiedTestFile> sent, bool allIncluded)
    {
        var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var answer in output.GetProperty("endpoints").EnumerateArray())
        {
            answers.TryAdd(answer.GetProperty("id").GetString()!, answer); // First answer per id wins.
        }

        return controller.Endpoints.Select((endpoint, i) =>
        {
            if (!answers.TryGetValue(Id(i), out var answer))
            {
                return Result(endpoint, CoverageStatus.Unverified, verified: false, [], null, "no answer for this endpoint");
            }

            var status = Status(answer.GetProperty("status").GetString()!);
            var notes = answer.GetProperty("notes").GetString();
            switch (status)
            {
                case CoverageStatus.None:
                    return allIncluded
                        ? Result(endpoint, CoverageStatus.None, verified: true, [], null, notes)
                        : Result(endpoint, CoverageStatus.Unverified, verified: false, [], null, "not every candidate test was read, so absence is not confirmed");
                case CoverageStatus.Unverified:
                    return Result(endpoint, CoverageStatus.Unverified, verified: false, [], null, notes);
            }

            var evidence = answer.GetProperty("evidence").EnumerateArray().Select(e => Resolve(e.GetString()!, sent)).ToList();
            if (evidence.Count == 0 || evidence.Any(e => e is null))
            {
                return Result(endpoint, CoverageStatus.Unverified, verified: false, [], null, Unresolved);
            }

            var falsePositive = status is CoverageStatus.Covered or CoverageStatus.Weak ? answer.GetProperty("falsePositive").GetBoolean() : (bool?)null;
            return Result(endpoint, status, verified: true, evidence.Distinct(StringComparer.Ordinal).ToList()!, falsePositive, notes);
        }).ToList();
    }

    /// <summary>"path::Test" when the path was sent and the test is one of its methods (by method name or Class.Method); else null.</summary>
    private static string? Resolve(string citation, List<ClassifiedTestFile> sent)
    {
        if (!Citation().IsMatch(citation))
        {
            return null;
        }

        var at = citation.IndexOf("::", StringComparison.Ordinal);
        var (path, test) = (citation[..at], citation[(at + 2)..]);
        var method = sent.FirstOrDefault(f => f.Path == path)?.TestMethods
            .FirstOrDefault(m => m == test || m.EndsWith("." + test, StringComparison.Ordinal));
        return method is null ? null : $"{path}::{method[(method.LastIndexOf('.') + 1)..]}";
    }

    private static CoverageStatus Status(string status) => status switch
    {
        "covered" => CoverageStatus.Covered,
        "weak" => CoverageStatus.Weak,
        "skipped" => CoverageStatus.Skipped,
        "none" => CoverageStatus.None,
        _ => CoverageStatus.Unverified,
    };

    private static string Id(int index) => $"e{index + 1}";

    private static EndpointResult Result(ExtractedEndpoint endpoint, CoverageStatus status, bool verified, IReadOnlyList<string> evidence, bool? falsePositive, string? notes) =>
        new()
        {
            Method = endpoint.Method,
            Route = endpoint.Route,
            Status = status,
            Verified = verified,
            Evidence = evidence,
            FalsePositive = falsePositive,
            Notes = notes,
        };

    [GeneratedRegex(@"^[^:]+\.[A-Za-z0-9]+::[A-Za-z_][\w.]*$")]
    private static partial Regex Citation();
}
