using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlindSpotScanner.Domain.Enums;
using BlindSpotScanner.Domain.Reports;

namespace BlindSpotScanner.Application.Reports;

/// <summary>
/// Doc 07 validator checklist. Re-derives every computed field with <see cref="ReportAssembler"/>
/// and diffs it leaf by leaf, so any tampered number is reported by its JSON path.
/// A non-empty result means the run must fail; it is never published as Succeeded.
/// </summary>
public sealed partial class ReportValidator(IReportSchemaValidator schema)
{
    private const int MaxHeadlineWords = 25;
    private const int MaxHeadlineChars = 200;
    private const int MaxWhyItMattersWords = 35;
    private const int MaxWhyItMattersChars = 280;

    public IReadOnlyList<string> Validate(IntegrationReport report, IntegrationReport? previous)
    {
        var violations = new List<string>();

        // 1. Schema conformance.
        violations.AddRange(schema.Validate(ReportJsonSerializer.Serialize(report)).Select(v => $"schema: {v}"));

        // 2–4, 7, 8. Re-derive everything the assembler owns and compare it with what the report claims.
        var expected = ReportAssembler.Assemble(ReportDetail.From(report), previous);
        Diff(Node(expected.EndpointCoverage), Node(report.EndpointCoverage), "endpointCoverage", violations);
        Diff(Node(expected.TopRisks), Node(report.TopRisks), "topRisks", violations);
        Diff(Node(expected.Summary), Node(report.Summary), "summary", violations);
        Diff(Node(expected.Delta), Node(report.Delta), "delta", violations);
        if (report.Inventory.EndpointsTotal != expected.Summary.Endpoints.Total)
        {
            violations.Add($"inventory/endpointsTotal: {report.Inventory.EndpointsTotal} does not equal the endpoint tally {expected.Summary.Endpoints.Total}");
        }

        CheckWeights(report.Scorecard, violations);
        CheckEvidence(report, violations);
        CheckRisks(report.TopRisks, violations);

        // 9. Citation resolution is checked by the CitationVerifier at the pinned SHA (M6).

        // 10. Only the scripted validator may sign off a report.
        if (report.Validation.Method != "script")
        {
            violations.Add($"validation/method: must be \"script\", found \"{report.Validation.Method}\"");
        }

        // jsonb rejects \u0000, so a report containing one could never be stored.
        CheckNoNulCharacters(Node(report), path: null, violations);
        return violations;
    }

    private static void CheckWeights(IReadOnlyList<ScoreDimension> scorecard, List<string> violations)
    {
        for (var i = 0; i < scorecard.Count; i++)
        {
            if (scorecard[i].Weight is <= 0 or > 1)
            {
                violations.Add($"scorecard/{i}/weight: must be in (0, 1], found {Invariant(scorecard[i].Weight)}");
            }
        }

        var sum = scorecard.Sum(d => d.Weight);
        if (sum != 1m)
        {
            violations.Add($"scorecard: weights sum to {Invariant(sum)}, not 1");
        }
    }

    private static void CheckEvidence(IntegrationReport report, List<string> violations)
    {
        for (var c = 0; c < report.EndpointCoverage.Count; c++)
        {
            var endpoints = report.EndpointCoverage[c].Endpoints;
            for (var e = 0; e < endpoints.Count; e++)
            {
                var endpoint = endpoints[e];
                var path = $"endpointCoverage/{c}/endpoints/{e}";
                if (endpoint.Status is CoverageStatus.Covered or CoverageStatus.Weak)
                {
                    var status = endpoint.Status.ToString().ToLowerInvariant();
                    if (!endpoint.Verified)
                    {
                        violations.Add($"{path}: {status} endpoint is not verified");
                    }

                    if (endpoint.Evidence.Count == 0)
                    {
                        violations.Add($"{path}: {status} endpoint has no evidence");
                    }
                }

                for (var k = 0; k < endpoint.Evidence.Count; k++)
                {
                    if (!Citation().IsMatch(endpoint.Evidence[k]))
                    {
                        violations.Add($"{path}/evidence/{k}: malformed citation '{endpoint.Evidence[k]}'");
                    }
                }
            }
        }

        for (var i = 0; i < report.Scorecard.Count; i++)
        {
            var dimension = report.Scorecard[i];
            if (dimension.Scorable && (dimension.JustificationBullets.Count < 3 || !dimension.JustificationBullets[2].Contains("::", StringComparison.Ordinal)))
            {
                violations.Add($"scorecard/{i}/justificationBullets/2: the evidence bullet of a scored dimension has no citation");
            }
        }
    }

    private static void CheckRisks(IReadOnlyList<TopRisk> risks, List<string> violations)
    {
        for (var i = 0; i < risks.Count; i++)
        {
            CheckPlainLanguage(risks[i].Headline, $"topRisks/{i}/headline", MaxHeadlineWords, MaxHeadlineChars, violations);
            CheckPlainLanguage(risks[i].WhyItMatters, $"topRisks/{i}/whyItMatters", MaxWhyItMattersWords, MaxWhyItMattersChars, violations);
        }
    }

    private static void CheckPlainLanguage(string text, string path, int maxWords, int maxChars, List<string> violations)
    {
        if (TechnicalReference().IsMatch(text))
        {
            violations.Add($"{path}: contains a path, citation or file name");
        }

        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > maxWords)
        {
            violations.Add($"{path}: more than {maxWords} words");
        }

        if (text.Length > maxChars)
        {
            violations.Add($"{path}: longer than {maxChars} characters");
        }
    }

    private static void CheckNoNulCharacters(JsonNode? node, string? path, List<string> violations)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    CheckNoNulCharacters(child, Join(path, key), violations);
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    CheckNoNulCharacters(array[i], Join(path, i.ToString(CultureInfo.InvariantCulture)), violations);
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && text.Contains('\0'):
                violations.Add($"{path}: contains a NUL character");
                break;
        }
    }

    /// <summary>Leaf-by-leaf comparison of two JSON trees; one violation per differing leaf.</summary>
    private static void Diff(JsonNode? expected, JsonNode? actual, string path, List<string> violations)
    {
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            foreach (var key in expectedObject.Select(p => p.Key).Union(actualObject.Select(p => p.Key)))
            {
                var hasExpected = expectedObject.TryGetPropertyValue(key, out var expectedChild);
                var hasActual = actualObject.TryGetPropertyValue(key, out var actualChild);
                if (hasExpected != hasActual)
                {
                    violations.Add($"{path}/{key}: expected {(hasExpected ? Text(expectedChild) : "(absent)")}, found {(hasActual ? Text(actualChild) : "(absent)")}");
                    continue;
                }

                Diff(expectedChild, actualChild, $"{path}/{key}", violations);
            }

            return;
        }

        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            for (var i = 0; i < Math.Max(expectedArray.Count, actualArray.Count); i++)
            {
                if (i >= expectedArray.Count || i >= actualArray.Count)
                {
                    violations.Add($"{path}/{i}: expected {(i < expectedArray.Count ? Text(expectedArray[i]) : "(absent)")}, found {(i < actualArray.Count ? Text(actualArray[i]) : "(absent)")}");
                    continue;
                }

                Diff(expectedArray[i], actualArray[i], $"{path}/{i}", violations);
            }

            return;
        }

        if (!JsonNode.DeepEquals(expected, actual))
        {
            violations.Add($"{path}: expected {Text(expected)}, found {Text(actual)}");
        }
    }

    private static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, ReportJsonSerializer.Options);

    private static string Text(JsonNode? node) => node?.ToJsonString(ReportJsonSerializer.Options) ?? "null";

    private static string Join(string? path, string segment) => path is null ? segment : $"{path}/{segment}";

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The schema's citation pattern: path/File.ext::TestName.</summary>
    [GeneratedRegex(@"^[^:]+\.[A-Za-z0-9]+::[A-Za-z_][\w.]*$")]
    private static partial Regex Citation();

    /// <summary>Doc 07 check 6: risk text stays plain language (no paths, citations or file names).</summary>
    [GeneratedRegex(@"/|::|\w\.(cs|ts|js|py|java|go|rb|json|ya?ml|xml)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TechnicalReference();
}
